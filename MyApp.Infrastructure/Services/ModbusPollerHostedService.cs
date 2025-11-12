using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Modbus.Device;
using MyApp.Application.Dtos;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MyApp.Infrastructure.SignalRHub;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MyApp.Infrastructure.Services
{
    public class ModbusPollerHostedService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<ModbusPollerHostedService> _log;
        private readonly IConfiguration _config;
        private readonly IHubContext<ModbusHub> _hub;

        // failure counters and console lock
        private static readonly ConcurrentDictionary<Guid, int> _failureCounts = new();
        private static readonly object _consoleLock = new();

        // device loop tasks (one per device)
        private readonly ConcurrentDictionary<Guid, Task> _deviceTasks = new();

        private readonly int _failThreshold;

        public IHubContext<ModbusHub> Hub => _hub;

        public ModbusPollerHostedService(IServiceProvider sp, ILogger<ModbusPollerHostedService> log, IConfiguration config, IHubContext<ModbusHub>? hub)
        {
            _sp = sp;
            _log = log;
            _config = config;
            _failThreshold = config?.GetValue<int?>("Modbus:FailureThreshold") ?? 3;
            if (_failThreshold <= 0) _failThreshold = 3;
            _hub = hub;
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

                            // the thread means  -> real worker thread, not thread pool
                            // it created by OS
                            // if CPU has 4 core then it can run 4 thread in parallel without context switching

                            // task takes the thread from thread pool
                            // thread pool can grow upto 32k (max) threads

                            // but the cpu has 4 core, so 4 thread at a time , 
                            // so context swich will happns very fast, and 
                            // context switching is expensive operation
                            // so we will limit the 100 devices to run in parallel

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
        /// Logic is preserved from the original PollSingleDeviceAsync, except the final Task.Delay is removed.
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
            var port = TryGetInt(settings, "Port", 5020); // default Modbus port fallback
            var slaveId = TryGetInt(settings, "SlaveId", 1);
            var endian = TryGetString(settings, "Endian") ?? "Big";
            var pollIntervalMs = TryGetInt(settings, "PollIntervalMs", cfg.PollIntervalMs > 0 ? cfg.PollIntervalMs : 1000);
            var addressStyleCfg = TryGetString(settings, "AddressStyle");

            if (string.IsNullOrEmpty(ip))
            {
                _log.LogWarning("Device {DeviceId} ProtocolSettingsJson missing IpAddress. Config: {CfgId}. Skipping poll.", device.DeviceId, cfg.ConfigurationId);
                return pollIntervalMs;
            }

            var ports = await db.DevicePorts.Where(p => p.DeviceId == device.DeviceId && p.IsHealthy && !device.IsDeleted).ToListAsync(ct);
            if (!ports.Any())
            {
                _log.LogWarning("No healthy ports for device {Device}. Ip={Ip} Port={Port} Settings={Settings}", device.DeviceId, ip, port, cfg.ProtocolSettingsJson);
                return pollIntervalMs;
            }

            const int ModbusMaxRegistersPerRead = 125;
            // here just declaring modbus max registers per read

            bool dbUses40001 = false;
            // checking address style, 
            // if it 1 based or zero based
            if (!string.IsNullOrEmpty(addressStyleCfg))
                dbUses40001 = string.Equals(addressStyleCfg, "40001", StringComparison.OrdinalIgnoreCase);
            else
                dbUses40001 = ports.Any(p => p.RegisterAddress >= 40001);

            int ToProto(int dbAddr)
            {
                //modbus expects zero-based addresses
                // and humans often use 1-based addresses starting at 40001
                // toProto normalizes to zero-based
                if (dbUses40001) return dbAddr - 40001; // 40003  - 40001 = 2
                if (dbAddr > 0 && dbAddr < 40001) return dbAddr - 1;
                return dbAddr;
            }

            var protoPorts = ports.Select(p => new
            {
                Port = p,
                ProtoAddr = ToProto(p.RegisterAddress), // here 0 based address
                Length = Math.Max(1, p.RegisterLength)
            })
            .OrderBy(x => x.ProtoAddr)
            .ToList();

            if (!protoPorts.Any())
            {
                _log.LogDebug("No ports after normalization for device {Device}", device.DeviceId);
                return pollIntervalMs;
            }

            var ranges = new List<(int Start, int Count, List<dynamic> Items)>();
            // ranges -> hold the final list of read block 
            // start-> protocol start index for the modbus read
            // count -> number of registers to read
            // Items port included in the block
            // i made this because -> it will group the nearby/ overlapping register
            // into the contiguous read ranges, 
            // each range size up to max 125 registers
            // that way the poller read many port with fewer musbus call

            // so in my db 8 port of 32 bit 
            // so i declere the 18 port , because one port is 2 register
            // one register -> 16 bits 

            // so the modbus will send the value in 32 bits, 
            // so it required the two register, 
            // Register 40001 → first half of the number
            //Register 40002 → second half of the number
            int idx = 0;
            while (idx < protoPorts.Count)
            {
                int start = protoPorts[idx].ProtoAddr; // 0 , 1, 2 
                int end = start + protoPorts[idx].Length - 1;
                var items = new List<dynamic> { protoPorts[idx] };
                idx++;
                // protoPorts[0] = { ProtoAddr = 0, Length = 2 }
                //→ start = 0
                //→ end = 1
                //→ items = [port0]

                while (idx < protoPorts.Count)
                {
                    //This checks if the next port’s register lies immediately after or overlaps the current range.
                    var next = protoPorts[idx];
                    if (next.ProtoAddr <= end + 1)
                    {
                        end = Math.Max(end, next.ProtoAddr + next.Length - 1);
                        items.Add(next);
                        idx++;
                    }
                    else break;

                    if (end - start + 1 >= ModbusMaxRegistersPerRead)
                    {
                        end = start + ModbusMaxRegistersPerRead - 1;
                        break;
                    }
                    // Modbus cannot read more than 125 registers at once.
                    //If your combined group exceeds 125 → stop grouping here.
                }

                int count = Math.Min(ModbusMaxRegistersPerRead, end - start + 1);
                // Compute how many registers this range covers
                //and add to the final list.
                ranges.Add((start, count, items));
            }

            try
            {
                using var tcp = new TcpClient();
                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await tcp.ConnectAsync(ip, port, connectCts.Token);

                using var master = ModbusIpMaster.CreateIp(tcp);
                // create a modbus master/client that runs on tcp connection
                master.Transport.ReadTimeout = 3000;
                // set modbus read time out 3000

                var now = DateTime.UtcNow;
                var allReads = new List<(int PortIndex, string SignalType, double Value, string Unit, int RegisterAddress)>();

                // We'll build per-range output into a buffer and print atomically
                foreach (var r in ranges)
                {
                    if (r.Start < 0 || r.Start > ushort.MaxValue) { _log.LogWarning("Skipping invalid start {Start}", r.Start); continue; }
                    if (r.Count <= 0) continue;

                    StringBuilder sb = new();

                    // Range header
                    sb.AppendLine();
                    sb.AppendLine(new string('=', 80));
                    sb.AppendLine($"Device: {device.DeviceId} | Ip={ip}:{port} | RangeStart={r.Start} Count={r.Count}");
                    sb.AppendLine(new string('-', 80));

                    // included ports for this range
                    sb.AppendLine("Included ports:");
                    foreach (var ent in r.Items)
                    {
                        var p = (DevicePort)ent.Port;
                        sb.AppendLine($"  - PortIndex={p.PortIndex}, DBAddr={p.RegisterAddress}, Length={ent.Length}, DataType={p.DataType}");
                    }
                    sb.AppendLine();

                    try
                    {
                        ushort[] regs = master.ReadHoldingRegisters((byte)slaveId, (ushort)r.Start, (ushort)r.Count);
                        sb.AppendLine($"Read {regs.Length} registers from slave={slaveId} start={r.Start}");
                        sb.AppendLine(new string('-', 80));

                        foreach (var ent in r.Items)
                        {
                            var p = (DevicePort)ent.Port;
                            _failureCounts.TryRemove(p.DevicePortId, out _);
                        }
                        // above line -> if data comes for port , then the port is working 
                        // remove the failure count

                        // Table header
                        sb.AppendLine($"{"Time (UTC)".PadRight(30)} | {"Port".PadRight(6)} | {"Register".PadRight(8)} | {"Value".PadRight(15)} | {"Unit".PadRight(8)}");
                        sb.AppendLine(new string('-', 80));

                        foreach (var entry in r.Items)
                        {
                            var p = (DevicePort)entry.Port;
                            int protoAddr = entry.ProtoAddr;
                            int relativeIndex = protoAddr - r.Start;

                            if (relativeIndex < 0 || relativeIndex + (entry.Length - 1) >= regs.Length)
                            {
                                sb.AppendLine($"Index out-of-range for port {p.PortIndex} (proto {protoAddr})");
                                continue;
                            }

                            double finalValue = 0.0;
                            try
                            {
                                if (string.Equals(p.DataType, "float32", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (relativeIndex + 1 >= regs.Length)
                                    {
                                        sb.AppendLine($"Not enough regs to decode float32 for port {p.PortIndex}");
                                        continue;
                                    }

                                    ushort r1 = regs[relativeIndex];
                                    ushort r2 = regs[relativeIndex + 1];
                                    // 2 reg -> 16 , 16 bit ill read -> 32 bit float
                                    // and meand 4 byte data 
                                    byte[] bytes = new byte[4] { (byte)(r1 >> 8), (byte)(r1 & 0xFF), (byte)(r2 >> 8), (byte)(r2 & 0xFF) };
                                    if (string.Equals(endian, "Little", StringComparison.OrdinalIgnoreCase)) Array.Reverse(bytes);

                                    float raw = BitConverter.ToSingle(bytes, 0);

                                    if (r2 == 0 && Math.Abs(raw) < 1e-3)
                                    {
                                        double scaledFallback = r1 / 100.0;
                                        finalValue = scaledFallback * p.Scale;
                                        sb.AppendLine($"Float32 fallback for port {p.PortIndex}: r1={r1}, r2={r2}, scaled={scaledFallback}");
                                    }
                                    else finalValue = raw * p.Scale;
                                }
                                else
                                {
                                    finalValue = regs[relativeIndex] * p.Scale;
                                }

                                allReads.Add((p.PortIndex, p.Unit ?? $"Port{p.PortIndex}", finalValue, p.Unit ?? string.Empty, p.RegisterAddress));

                                // append row
                                sb.AppendLine($"{now:O.PadRight(30)} | {p.PortIndex.ToString().PadRight(6)} | {p.RegisterAddress.ToString().PadRight(8)} | {finalValue.ToString("G6").PadRight(15)} | {(p.Unit ?? string.Empty).PadRight(8)}");
                            }
                            catch (Exception decodeEx)
                            {
                                sb.AppendLine($"Decode failed for port {p.PortIndex}: {decodeEx.Message}");
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
                        _log.LogError(sex, "Modbus SlaveException device {Device} start={Start} count={Count}", device.DeviceId, r.Start, r.Count);

                        var idsToConsider = r.Items.Select(it => ((DevicePort)it.Port).DevicePortId).ToList();
                        try
                        {
                            var dbPorts = await db.DevicePorts.Where(dp => idsToConsider.Contains(dp.DevicePortId)).ToListAsync(ct);
                            var toMark = new List<Guid>();

                            foreach (var dp in dbPorts)
                            {
                                var id = dp.DevicePortId;
                                int newCount = _failureCounts.AddOrUpdate(id, 1, (_, old) => old + 1);
                                _log.LogWarning("Failure count for port {PortIndex} (Id={Id}) = {Count}", dp.PortIndex, id, newCount);

                                if (newCount >= _failThreshold && dp.IsHealthy) toMark.Add(id);
                            }

                            if (toMark.Any())
                            {
                                var markPorts = await db.DevicePorts.Where(dp => toMark.Contains(dp.DevicePortId)).ToListAsync(ct);
                                foreach (var mp in markPorts) mp.IsHealthy = false;
                                await db.SaveChangesAsync(ct);
                                _log.LogWarning("Marked {Count} ports unhealthy for device {Device}", markPorts.Count, device.DeviceId);
                            }
                        }
                        catch (Exception markEx)
                        {
                            _log.LogError(markEx, "Failed marking unhealthy ports for device {Device}", device.DeviceId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Error reading device {Device} start={Start} count={Count}", device.DeviceId, r.Start, r.Count);
                    }
                } // ranges

                if (allReads.Count > 0)
                {
                    try
                    {
                        var telemetryRows = allReads.Select(r =>
                        {
                            var dp = ports.FirstOrDefault(p => p.PortIndex == r.PortIndex);
                            return new Telemetry
                            {
                                DevicePortId = dp != null ? dp.DevicePortId : Guid.Empty,
                                SignalType = r.SignalType,
                                Value = r.Value,
                                Unit = r.Unit,
                                Timestamp = now
                            };
                        }).Where(t => t.DevicePortId != Guid.Empty).ToList();

                        // saving omitted by design in original code
                        // Print telemetry buffer atomically
                        // after you've prepared telemetryRows and 'now' variable
                        if (telemetryRows.Any())
                        {
                            var telemetryDtos = telemetryRows.Select(t =>
                            {
                                // find port metadata for port-index mapping
                                var dp = ports.FirstOrDefault(p => p.DevicePortId == t.DevicePortId);
                                // But better: telemetryRows already filled DevicePortId earlier. Use that
                                return new TelemetryDto(
                                    DeviceId: device.DeviceId,
                                    DevicePortId: t.DevicePortId,
                                    PortIndex: ports.FirstOrDefault(p => p.DevicePortId == t.DevicePortId)?.PortIndex ?? -1,
                                    RegisterAddress: ports.FirstOrDefault(p => p.DevicePortId == t.DevicePortId)?.RegisterAddress ?? 0,
                                    SignalType: t.SignalType,
                                    Value: t.Value,
                                    Unit: t.Unit,
                                    Timestamp: t.Timestamp
                                );
                            }).ToList();

                            try
                            {
                                // Send to all clients in the device group (group name = device id string)
                                await Hub.Clients
                                    .Group(device.DeviceId.ToString())
                                    .SendAsync("TelemetryUpdate", telemetryDtos, ct);
                            }
                            catch (Exception hubEx)
                            {
                                _log.LogWarning(hubEx, "Failed to push telemetry to SignalR for device {Device}", device.DeviceId);
                            }
                        }


                        _log.LogDebug("Prepared {Count} telemetry rows for device {Device}", telemetryRows.Count, device.DeviceId);
                    }
                    catch (Exception dbEx)
                    {
                        _log.LogError(dbEx, "Failed to prepare/save telemetry for device {Device}", device.DeviceId);
                    }
                }
            }
            catch (SocketException sex)
            {
                _log.LogWarning(sex, "Device {Device} unreachable {Ip}:{Port}", device.DeviceId, ip, port);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error polling device {Device}", device.DeviceId);
            }

            // Return the poll interval (ms) so the caller loop can delay appropriately
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
