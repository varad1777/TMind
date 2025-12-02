using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
// Using in-repo ModbusTcpClient helper instead of external NModbus4 package
using MyApp.Application.Dtos;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Data;
using MyApp.Infrastructure.SignalRHub;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MyApp.Infrastructure.Services
{
    /// <summary>
    /// Background service that polls Modbus TCP devices and sends telemetry via SignalR.
    /// This class preserves your original behavior: grouping ranges, float fallback,
    /// failure counting and marking ports unhealthy, console buffered printing, and
    /// NOT saving telemetry to the DB (only push via SignalR).
    /// Additionally it bounds concurrent device connections using _semaphore.
    /// </summary>
    public class ModbusPollerHostedService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<ModbusPollerHostedService> _log;
        private readonly IConfiguration _config;
        private readonly IHubContext<ModbusHub> _hub;
        private readonly RabbitMqService _rabbit;

        // Semaphore to limit concurrent TCP connections / modbus polls
        // value loaded from config or default 10
        private readonly SemaphoreSlim _semaphore;

        // failure counters and console lock (shared)
        private static readonly ConcurrentDictionary<Guid, int> _failureCounts = new();
        private static readonly object _consoleLock = new();

        // per-device loop tasks
        private readonly ConcurrentDictionary<Guid, Task> _deviceTasks = new();

        private readonly int _failThreshold;

        public IHubContext<ModbusHub> Hub => _hub;

        public ModbusPollerHostedService(IServiceProvider sp, ILogger<ModbusPollerHostedService> log,
                                         IConfiguration config, IHubContext<ModbusHub>? hub, RabbitMqService rabbit)
        {
            _sp = sp;
            _log = log;
            _config = config;
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));

            // read failure threshold and concurrency limit from configuration
            _failThreshold = config?.GetValue<int?>("Modbus:FailureThreshold") ?? 3;
            if (_failThreshold <= 0) _failThreshold = 3;

            int concurrency = config?.GetValue<int?>("Modbus:MaxConcurrentPolls") ?? 10;
            if (concurrency <= 0) concurrency = 10;
            _semaphore = new SemaphoreSlim(concurrency, concurrency);
            _rabbit = rabbit;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("Modbus poller started (device-per-loop mode)");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        using var scope = _sp.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                        // load current device ids
                        var deviceIds = await db.Devices
                            .AsNoTracking()
                            .Where(d => !d.IsDeleted)
                            .Select(d => d.DeviceId)
                            .ToListAsync(stoppingToken);

                        // start a long-running loop task for each device if not already running
                        foreach (var id in deviceIds)
                        {
                            if (_deviceTasks.ContainsKey(id)) continue;

                            // fire-and-forget long running loop for the device
                            var task = Task.Run(() => PollLoopForDeviceAsync(id, stoppingToken), stoppingToken);
                            _deviceTasks.TryAdd(id, task);

                            // cleanup completed tasks (non-blocking)
                            var completed = _deviceTasks.Where(kvp => kvp.Value.IsCompleted).Select(kvp => kvp.Key).ToList();
                            foreach (var k in completed) _deviceTasks.TryRemove(k, out _);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Poll loop manager error");
                    }

                    // small delay before scanning DB again for new/removed devices
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
            finally
            {
                // attempt graceful shutdown of device loops
                try
                {
                    await Task.WhenAll(_deviceTasks.Values.ToArray());
                }
                catch
                {
                    // ignore exceptions during shutdown
                }
            }
        }

        /// <summary>
        /// Per-device loop. Calls PollSingleDeviceOnceAsync repeatedly and delays based on returned poll interval.
        /// </summary>
        private async Task PollLoopForDeviceAsync(Guid deviceId, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    int delayMs = 1000;
                    try
                    {
                        delayMs = await PollSingleDeviceOnceAsync(deviceId, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Unhandled error during single poll for device {Device}", deviceId);
                        // small backoff to avoid tight crash loop
                        delayMs = 1000;
                    }

                    if (delayMs <= 0) delayMs = 1000;
                    await Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error in device loop for {Device}", deviceId);
                    // back off on unexpected loop-level error
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                }
            }
        }

        /// <summary>
        /// Performs a single poll for the given device and returns the device's poll interval in milliseconds.
        /// All original behaviors are preserved:
        /// - parse ProtocolSettingsJson,
        /// - normalize addresses,
        /// - group ranges up to 125 registers,
        /// - decode float32 + fallback,
        /// - failure counting and marking unhealthy ports (DB update),
        /// - buffered console output (atomic),
        /// - send telemetry via SignalR only (no DB save).
        /// Additionally this method uses _semaphore to bound concurrency around the network I/O.
        /// </summary>
        private async Task<int> PollSingleDeviceOnceAsync(Guid deviceId, CancellationToken ct)
        {
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var device = await db.Devices.Include(d => d.DeviceConfiguration).FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
            if (device == null)
            {
                _log.LogDebug("Device {DeviceId} not found - skipping", deviceId);
                return 1000; // safe default
            }

            if (device.IsDeleted)
            {
                _log.LogInformation("Device {DeviceId} is soft-deleted - stopping polling", deviceId);
                return 1000;
            }

            if (device.DeviceConfigurationId == null)
            {
                _log.LogDebug("Device {DeviceId} has no configuration - skipping", device.DeviceId);
                return 1000;
            }

            var cfg = device.DeviceConfiguration!;
            _log.LogInformation("Polling device {DeviceId} using config {CfgId}", device.DeviceId, cfg.ConfigurationId);

            JsonDocument settings;
            try { settings = JsonDocument.Parse(cfg.ProtocolSettingsJson ?? "{}"); }
            catch (Exception ex)
            {
                _log.LogError(ex, "Invalid ProtocolSettingsJson for device {Device}", device.DeviceId);
                return cfg.PollIntervalMs > 0 ? cfg.PollIntervalMs : 1000;
            }

            // read settings strictly from ProtocolSettingsJson (no device.Host/device.Port fallback)
            var ip = TryGetString(settings, "IpAddress");
            var port = TryGetInt(settings, "Port", 502); // default Modbus TCP port is 502
            var slaveIdFromConfig = TryGetInt(settings, "SlaveId", 1); // kept as fallback if no DeviceSlave entry
            var endian = TryGetString(settings, "Endian") ?? "Big";
            var pollIntervalMs = TryGetInt(settings, "PollIntervalMs", cfg.PollIntervalMs > 0 ? cfg.PollIntervalMs : 1000);
            var addressStyleCfg = TryGetString(settings, "AddressStyle");

            if (string.IsNullOrEmpty(ip))
            {
                _log.LogWarning("Device {DeviceId} ProtocolSettingsJson missing IpAddress. Config: {CfgId}. Skipping poll.", device.DeviceId, cfg.ConfigurationId);
                return pollIntervalMs;
            }

            // Load device slaves and their registers. Only include slaves that belong to the device
            // and are not soft-deleted; register-level health is used to filter which registers to poll.
            var slaves = await db.DeviceSlaves
                .Include(ds => ds.Registers)
                .Where(s => s.DeviceId == device.DeviceId && s.IsHealthy)
                .ToListAsync(ct);

            // Flatten registers from slaves; skip slaves without any healthy registers
            var activeRegisters = slaves
                .SelectMany(ds => ds.Registers.Where(r => r.IsHealthy).Select(r => new { DeviceSlave = ds, Register = r }))
                .ToList();

            if (!activeRegisters.Any())
            {
                _log.LogWarning("No healthy registers for device {Device}. Ip={Ip} Port={Port} Settings={Settings}", device.DeviceId, ip, port, cfg.ProtocolSettingsJson);
                return pollIntervalMs;
            }

            const int ModbusMaxRegistersPerRead = 125;

            bool dbUses40001 = false;
            // checking address style, if 1-based or zero-based
            if (!string.IsNullOrEmpty(addressStyleCfg))
                dbUses40001 = string.Equals(addressStyleCfg, "40001", StringComparison.OrdinalIgnoreCase);
            else
                dbUses40001 = slaves.Any(s => s.Registers != null && s.Registers.Any(r => r.RegisterAddress >= 40001));

            int ToProto(int dbAddr)
            {
                // modbus expects zero-based addresses; normalize
                if (dbUses40001) return dbAddr - 40001;
                if (dbAddr > 0 && dbAddr < 40001) return dbAddr - 1;
                return dbAddr;
            }

            // Map registers to protocol addresses and lengths (preserve link to DeviceSlave)
            var protoPorts = activeRegisters.Select(x => new
            {
                DeviceSlave = x.DeviceSlave,
                Register = x.Register,
                ProtoAddr = ToProto(x.Register.RegisterAddress), // zero-based address
                Length = Math.Max(1, x.Register.RegisterLength)
            })
            .OrderBy(x => x.ProtoAddr)
            .ToList();

            if (!protoPorts.Any())
            {
                _log.LogDebug("No ports after normalization for device {Device}", device.DeviceId);
                return pollIntervalMs;
            }

            // Acquire semaphore to limit concurrent network connections/polls.
            // This ensures we don't overload network/DB/CPU when many device loops run.
            await _semaphore.WaitAsync(ct);  // this basically used to controll the concurrent access.
            try
            {
                // Connect to device TCP with a short timeout
                using var tcp = new TcpClient();
                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                // Link tokens so cancelling the overall loop cancels connect
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, connectCts.Token);

                await tcp.ConnectAsync(ip, port, linked.Token);

                // Using in-repo ModbusTcpClient helper to avoid NModbus4 dependency

                var now = DateTime.UtcNow;
                // tuple: (deviceSlaveId, slaveIndex, SignalType, Value, Unit, RegisterAddress)
                var allReads = new List<(Guid deviceSlaveId, int slaveIndex, string SignalType, double Value, string Unit, int RegisterAddress)>();

                // --- Group protoPorts by slaveIndex (unit id) so we never mix different slaves in one request ---
                var protoGroups = protoPorts
                    .GroupBy(x => ((DeviceSlave)x.DeviceSlave).slaveIndex)
                    .ToDictionary(g => g.Key, g => g.OrderBy(p => p.ProtoAddr).ToList());

                // For each slave (unit id) build contiguous ranges and read separately
                foreach (var kv in protoGroups)
                {
                    int unitId = kv.Key; // the slaveIndex/unit id to use for Modbus requests
                    var itemsForSlave = kv.Value; // ordered by ProtoAddr

                    // Build ranges for this slave alone (each up to ModbusMaxRegistersPerRead)
                    var slaveRanges = new List<(int Start, int Count, List<dynamic> Items)>();
                    int j = 0;
                    while (j < itemsForSlave.Count)
                    {
                        int start = itemsForSlave[j].ProtoAddr; //0 
                        int end = start + itemsForSlave[j].Length - 1;
                        var items = new List<dynamic> { itemsForSlave[j] };
                        j++;

                        while (j < itemsForSlave.Count)
                        {
                            var next = itemsForSlave[j];
                            if (next.ProtoAddr <= end + 1)
                            {
                                end = Math.Max(end, next.ProtoAddr + next.Length - 1);
                                items.Add(next);
                                j++;
                            }
                            else break;

                            if (end - start + 1 >= ModbusMaxRegistersPerRead)
                            {
                                end = start + ModbusMaxRegistersPerRead - 1;
                                break;
                            }
                        }

                        int count = Math.Min(ModbusMaxRegistersPerRead, end - start + 1);
                        slaveRanges.Add((start, count, items));
                    }

                    // Now perform reads for each range for THIS unitId
                    foreach (var r in slaveRanges)
                    {
                        if (r.Start < 0 || r.Start > ushort.MaxValue) { _log.LogWarning("Skipping invalid start {Start}", r.Start); continue; }
                        if (r.Count <= 0) continue;

                        StringBuilder sb = new();

                        // Range header (buffered output)
                        sb.AppendLine();
                        sb.AppendLine(new string('=', 80));
                        sb.AppendLine($"Device: {device.DeviceId} | Ip={ip}:{port} | UnitId={unitId} | RangeStart={r.Start} Count={r.Count}");
                        sb.AppendLine(new string('-', 80));

                        // included registers for this range
                        sb.AppendLine("Included registers:");
                        foreach (var ent in r.Items)
                        {
                            var ds = (DeviceSlave)ent.DeviceSlave;
                            var reg = (Register)ent.Register;
                            sb.AppendLine($"  - slaveIndex={ds.slaveIndex}, DBAddr={reg.RegisterAddress}, Length={ent.Length}, DataType={reg.DataType}");
                        }
                        sb.AppendLine();

                        try
                        {
                            // IMPORTANT: use the slave's unit id here, not the single device-level slaveId
                            ushort[] regs = await ModbusTcpClient.ReadHoldingRegistersAsync(tcp, (byte)unitId, (ushort)r.Start, (ushort)r.Count, ct);
                            sb.AppendLine($"Read {regs.Length} registers from unit={unitId} start={r.Start}");
                            sb.AppendLine(new string('-', 80));

                            // Reset failure counts for registers in this successful read
                            foreach (var ent in r.Items)
                            {
                                var reg = (Register)ent.Register;
                                _failureCounts.TryRemove(reg.RegisterId, out _);
                            }

                            // Table header
                            sb.AppendLine($"{"Time (UTC)".PadRight(30)} | {"Unit".PadRight(6)} | {"Register".PadRight(8)} | {"Value".PadRight(15)} | {"Unit".PadRight(8)}");
                            sb.AppendLine(new string('-', 80));

                            // Decode each register in this range (same logic you already have)
                            foreach (var entry in r.Items)
                            {
                                var ds = (DeviceSlave)entry.DeviceSlave;
                                var reg = (Register)entry.Register;
                                int protoAddr = entry.ProtoAddr;
                                int relativeIndex = protoAddr - r.Start;

                                if (relativeIndex < 0 || relativeIndex + (entry.Length - 1) >= regs.Length)
                                {
                                    sb.AppendLine($"Index out-of-range for slave {ds.slaveIndex} (proto {protoAddr})");
                                    continue;
                                }

                                double finalValue = 0.0;
                                try
                                {
                                    if (string.Equals(reg.DataType, "float32", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (relativeIndex + 1 >= regs.Length)
                                        {
                                            sb.AppendLine($"Not enough regs to decode float32 for slave {ds.slaveIndex}");
                                            continue;
                                        }

                                        ushort r1 = regs[relativeIndex];
                                        ushort r2 = regs[relativeIndex + 1];

                                        byte[] bytes = new byte[4] { (byte)(r1 >> 8), (byte)(r1 & 0xFF), (byte)(r2 >> 8), (byte)(r2 & 0xFF) };
                                        if (string.Equals(endian, "Little", StringComparison.OrdinalIgnoreCase)) Array.Reverse(bytes);

                                        float raw = BitConverter.ToSingle(bytes, 0);

                                        if (r2 == 0 && Math.Abs(raw) < 1e-3)
                                        {
                                            double scaledFallback = r1;
                                            finalValue = scaledFallback * reg.Scale;
                                            sb.AppendLine($"Float32 fallback for slave {ds.slaveIndex}: r1={r1}, r2={r2}, scaled={scaledFallback}");
                                        }
                                        else
                                        {
                                            finalValue = raw * reg.Scale;
                                        }
                                    }
                                    else
                                    {
                                        finalValue = regs[relativeIndex] * reg.Scale;
                                    }

                                    // add to telemetry buffer
                                    allReads.Add((ds.deviceSlaveId, ds.slaveIndex, reg.DataType ?? $"Port{ds.slaveIndex}", finalValue, reg.Unit ?? string.Empty, reg.RegisterAddress));

                                    // append row to buffer
                                    sb.AppendLine($"{now:O.PadRight(30)} | {ds.slaveIndex.ToString().PadRight(6)} | {reg.RegisterAddress.ToString().PadRight(8)} | {finalValue.ToString("G6").PadRight(15)} | {(reg.Unit ?? string.Empty).PadRight(8)}");
                                }
                                catch (Exception decodeEx)
                                {
                                    sb.AppendLine($"Decode failed for slave {ds.slaveIndex}: {decodeEx.Message}");
                                }
                            }

                            sb.AppendLine(new string('=', 80));

                            // Print the whole buffer atomically to avoid mixing with other device outputs
                            lock (_consoleLock)
                            {
                                Console.Write(sb.ToString());
                            }
                        }
                        catch (Modbus.SlaveException sex)
                        {
                            _log.LogError(sex, "Modbus SlaveException device {Device} unit={UnitId} start={Start} count={Count}", device.DeviceId, unitId, r.Start, r.Count);

                            var regsToConsider = r.Items.Select(it => ((Register)it.Register).RegisterId).ToList();
                            try
                            {
                                var dbRegs = await db.Registers.Where(reg => regsToConsider.Contains(reg.RegisterId)).ToListAsync(ct);
                                var toMark = new List<Guid>();

                                foreach (var reg in dbRegs)
                                {
                                    var id = reg.RegisterId;
                                    int newCount = _failureCounts.AddOrUpdate(id, 1, (_, old) => old + 1);
                                    _log.LogWarning("Failure count for register {RegisterAddress} (Id={Id}) = {Count}", reg.RegisterAddress, id, newCount);

                                    if (newCount >= _failThreshold && reg.IsHealthy) toMark.Add(id);
                                }

                                if (toMark.Any())
                                {
                                    var markRegs = await db.Registers.Where(reg => toMark.Contains(reg.RegisterId)).ToListAsync(ct);
                                    foreach (var mr in markRegs) mr.IsHealthy = false;
                                    await db.SaveChangesAsync(ct);
                                    _log.LogWarning("Marked {Count} registers unhealthy for device {Device}", markRegs.Count, device.DeviceId);
                                }
                            }
                            catch (Exception markEx)
                            {
                                _log.LogError(markEx, "Failed marking unhealthy registers for device {Device}", device.DeviceId);
                            }
                        }
                        catch (Exception ex)
                        {
                            _log.LogError(ex, "Error reading device {Device} unit={UnitId} start={Start} count={Count}", device.DeviceId, unitId, r.Start, r.Count);
                        }
                    } // foreach slaveRanges
                } // foreach protoGroups

                // Prepare telemetry DTOs and push them to SignalR (no DB save)
                if (allReads.Count > 0)
                {
                    try
                    {
                        var telemetryDtos = allReads.Select(r =>
                        {
                            // map to your TelemetryDto shape - keep DevicePortId param but pass deviceSlaveId for compatibility
                            return new TelemetryDto(
                                DeviceId: device.DeviceId,
                                deviceSlaveId: r.deviceSlaveId,
                                slaveIndex: r.slaveIndex,
                                RegisterAddress: r.RegisterAddress,
                                SignalType: r.SignalType,
                                Value: r.Value,
                                Unit: r.Unit,
                                Timestamp: DateTime.Now

                            );
                        }).ToList();

                        if (telemetryDtos.Any())
                        {
                            try
                            {
                                await Hub.Clients.Group(device.DeviceId.ToString()).SendAsync("TelemetryUpdate", telemetryDtos, ct);
                            }
                            catch (Exception hubEx)
                            {
                                _log.LogWarning(hubEx, "Failed to push telemetry to SignalR for device {Device}", device.DeviceId);
                            }

                            try
                            {
                                // Option B: publish individual telemetry items
                                foreach (var dto in telemetryDtos)
                                {
                                    ct.ThrowIfCancellationRequested();
                                    await _rabbit.PublishAsync(dto, ct);
                                }
                            }
                            catch (Exception rmqEx)
                            {
                                _log.LogWarning(rmqEx, "Failed to publish telemetry to RabbitMQ for device {Device}", device.DeviceId);
                            }
                        }
                    }
                    catch (Exception hubEx)
                    {
                        _log.LogWarning(hubEx, "Failed to prepare telemetry for device {Device}", device.DeviceId);
                    }

                    _log.LogDebug("Prepared {Count} telemetry rows for device {Device}", allReads.Count, device.DeviceId);
                }
            }
            catch (SocketException s_ex)
            {
                _log.LogWarning(s_ex, "Device {Device} unreachable {Ip}:{Port}", device.DeviceId, ip, port);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // polling was cancelled via token, simply return
                _log.LogDebug("Polling cancelled for device {Device}", device.DeviceId);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error polling device {Device}", device.DeviceId);
            }
            finally
            {
                // Always release the semaphore even on exceptions
                _semaphore.Release();
            }

            // Return the poll interval (ms) for next loop delay
            return pollIntervalMs;
        }

        private static string? TryGetString(JsonDocument doc, string propName)
        {
            if (doc.RootElement.TryGetProperty(propName, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();
            return null;
        }

        private static int TryGetInt(JsonDocument doc, string propName, int @default)
        {
            if (doc.RootElement.TryGetProperty(propName, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var x)) return x;
            return @default;
        }
    }
}
