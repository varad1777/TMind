using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyApp.Application.Dtos;
using MyApp.Application.Interfaces;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Data;
using System.Text.Json;


namespace MyApp.Infrastructure.Services
{
    public class DeviceManager : IDeviceManager
    {
        private readonly AppDbContext _db;
        private readonly ILogger<DeviceManager> _log;
        public DeviceManager(AppDbContext db, ILogger<DeviceManager> log) { _db = db; _log = log; }





        public async Task<Guid> CreateDeviceAsync(CreateDeviceDto request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var name = (request.Name ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Device name is required.", nameof(request.Name));

            var exists = await _db.Devices
                                 .AsNoTracking()
                                 .AnyAsync(d => !d.IsDeleted && d.Name.ToLower() == name.ToLower(), ct);
            if (exists) throw new InvalidOperationException($"Device name '{name}' already exists.");

            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var device = new Device
            {
                DeviceId = Guid.NewGuid(),
                Name = name,
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim()
            };

            await _db.Devices.AddAsync(device, ct);

           

           

            if (request.Configuration != null)
            {
                string protoJson = request.Configuration.ProtocolSettingsJson;
                if (string.IsNullOrWhiteSpace(protoJson) || protoJson.Trim() == "{}")
                {
                    protoJson = JsonSerializer.Serialize(request.Configuration);
                }

                var cfg = new DeviceConfiguration
                {
                    ConfigurationId = Guid.NewGuid(),
                    Name = string.IsNullOrWhiteSpace(request.Configuration.Name) ? $"{device.Name}-cfg" : request.Configuration.Name.Trim(),
                    PollIntervalMs = request.Configuration.PollIntervalMs > 0 ? request.Configuration.PollIntervalMs : 1000,
                    ProtocolSettingsJson = protoJson
                };

                await _db.DeviceConfigurations.AddAsync(cfg, ct);
                device.DeviceConfigurationId = cfg.ConfigurationId;
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _log.LogInformation("Created device {DeviceId}", device.DeviceId);
            return device.DeviceId;
        }
        // remove: using System.Text.Json;
        public async Task UpdateDeviceAsync(Guid deviceId, UpdateDeviceDto dto, DeviceConfigurationDto? configDto = null, CancellationToken ct = default)
        {
            var device = await _db.Devices.FindAsync(new object[] { deviceId }, ct);
            if (device == null) throw new KeyNotFoundException("Device not found");

            // Prevent updates to soft-deleted devices
            if (device.IsDeleted)
                throw new InvalidOperationException("Cannot update a deleted device.");

            // Update device fields (only when provided)
            if (dto.Name != null)
            {
                var trimmed = dto.Name.Trim();
                if (trimmed.Length < 3 || trimmed.Length > 100)
                    throw new ArgumentException("Device name must be between 3 and 100 characters.", nameof(dto.Name));

                // Check uniqueness only when the new name is different from current (case-insensitive)
                var newNameNorm = trimmed.ToLowerInvariant();
                var currentNameNorm = (device.Name ?? string.Empty).ToLowerInvariant();

                if (newNameNorm != currentNameNorm)
                {
                    // Exclude this device and any soft-deleted devices from the uniqueness check
                    var exists = await _db.Devices
                                          .AsNoTracking()
                                          .AnyAsync(d =>
                                              d.DeviceId != deviceId
                                              && !d.IsDeleted
                                              && d.Name.ToLower() == newNameNorm,
                                              ct);

                    if (exists)
                        throw new ArgumentException("A device with the same name already exists.");
                }

                device.Name = trimmed;
            }

            if (dto.Description != null)
            {
                var trimmedDesc = dto.Description.Trim();
                if (trimmedDesc.Length > 255)
                    throw new ArgumentException("Description cannot exceed 255 characters.", nameof(dto.Description));
                device.Description = trimmedDesc;
            }

            if (dto.Protocol != null)
            {
                var trimmedProto = dto.Protocol.Trim();
                if (trimmedProto.Length == 0 || trimmedProto.Length > 100)
                    throw new ArgumentException("Protocol must be a non-empty value up to 100 characters.", nameof(dto.Protocol));
                device.Protocol = trimmedProto;
            }

            // Handle configuration update/create when configDto is provided
            if (configDto != null)
            {
                if (string.IsNullOrWhiteSpace(configDto.Name) || configDto.Name.Length > 100)
                    throw new ArgumentException("Configuration name must be between 1 and 100 characters.", nameof(configDto.Name));
                if (configDto.PollIntervalMs < 100 || configDto.PollIntervalMs > 300000)
                    throw new ArgumentOutOfRangeException(nameof(configDto.PollIntervalMs), "Poll interval must be between 100 and 300000 milliseconds.");
                if (configDto.ProtocolSettingsJson == null)
                    throw new ArgumentException("ProtocolSettingsJson is required.", nameof(configDto.ProtocolSettingsJson));

                if (device.DeviceConfigurationId is Guid cfgId)
                {
                    // check if other devices reference same config
                    var otherUses = await _db.Devices
                                             .AsNoTracking()
                                             .AnyAsync(d => d.DeviceId != deviceId && d.DeviceConfigurationId == cfgId, ct);

                    if (!otherUses)
                    {
                        // update in-place if config exists, otherwise create replacement
                        var existingCfg = await _db.DeviceConfigurations.FindAsync(new object[] { cfgId }, ct);
                        if (existingCfg == null)
                        {
                            var replacement = new DeviceConfiguration
                            {
                                ConfigurationId = Guid.NewGuid(), // ensure id immediately
                                Name = configDto.Name.Trim(),
                                PollIntervalMs = configDto.PollIntervalMs,
                                ProtocolSettingsJson = configDto.ProtocolSettingsJson
                            };
                            await _db.DeviceConfigurations.AddAsync(replacement, ct);
                            device.DeviceConfigurationId = replacement.ConfigurationId;
                            _log.LogWarning("Device {DeviceId} referenced missing configuration {CfgId}; created replacement {ReplacementId}", deviceId, cfgId, replacement.ConfigurationId);
                        }
                        else
                        {
                            existingCfg.Name = configDto.Name.Trim();
                            existingCfg.PollIntervalMs = configDto.PollIntervalMs;
                            existingCfg.ProtocolSettingsJson = configDto.ProtocolSettingsJson;
                            _db.DeviceConfigurations.Update(existingCfg);
                            _log.LogInformation("Updated DeviceConfiguration {CfgId} for device {DeviceId}", cfgId, deviceId);
                        }
                    }
                    else
                    {
                        // shared config — create and attach new
                        var newCfg = new DeviceConfiguration
                        {
                            ConfigurationId = Guid.NewGuid(),
                            Name = configDto.Name.Trim(),
                            PollIntervalMs = configDto.PollIntervalMs,
                            ProtocolSettingsJson = configDto.ProtocolSettingsJson
                        };
                        await _db.DeviceConfigurations.AddAsync(newCfg, ct);
                        device.DeviceConfigurationId = newCfg.ConfigurationId;
                        _log.LogInformation("Configuration {CfgId} is shared; created new DeviceConfiguration {NewCfg} and attached to device {DeviceId}", cfgId, newCfg.ConfigurationId, deviceId);
                    }
                }
                else
                {
                    // no config attached -> create and attach
                    var newCfg = new DeviceConfiguration
                    {
                        ConfigurationId = Guid.NewGuid(),
                        Name = configDto.Name.Trim(),
                        PollIntervalMs = configDto.PollIntervalMs,
                        ProtocolSettingsJson = configDto.ProtocolSettingsJson
                    };
                    await _db.DeviceConfigurations.AddAsync(newCfg, ct);
                    device.DeviceConfigurationId = newCfg.ConfigurationId;
                    _log.LogInformation("Created and attached new DeviceConfiguration {CfgId} to device {DeviceId}", newCfg.ConfigurationId, deviceId);
                }
            }

            // device is tracked; no need to call _db.Devices.Update(device)
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Updated device {DeviceId}", deviceId);
        }









        public async Task<(List<Device> Devices, int TotalCount)> GetAllDevicesAsync(
      int pageNumber,
      int pageSize,
      string? searchTerm,
      CancellationToken ct = default)
        {
            // Start query
            var query = _db.Devices
                           .Where(d => !d.IsDeleted)
                           .Include(d => d.DeviceConfiguration)
                           .AsNoTracking();

            // Apply search
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                searchTerm = searchTerm.ToLower();
                query = query.Where(d => d.Name.ToLower().Contains(searchTerm)
                                      || (d.Description != null && d.Description.ToLower().Contains(searchTerm)));
            }

            // Get total count for pagination metadata
            var totalCount = await query.CountAsync(ct);

            // Apply pagination
            var devices = await query
                                .Skip((pageNumber - 1) * pageSize)
                                .Take(pageSize)
                                .ToListAsync(ct);

            return (devices, totalCount);
        }




        public async Task DeleteDeviceAsync(Guid deviceId, CancellationToken ct = default)
        {
            var device = await _db.Devices.FindAsync(new object[] { deviceId }, ct);
            if (device == null)
                throw new KeyNotFoundException("Device not found");

            if (device.IsDeleted)
            {
                _log.LogWarning("Device {DeviceId} is already marked as deleted", deviceId);
                return;
            }

            // Optional: If you want to prevent deletion if config is used elsewhere
            if (device.DeviceConfigurationId is Guid cfgId)
            {
                var otherUses = await _db.Devices
                                         .AsNoTracking()
                                         .AnyAsync(d => d.DeviceId != deviceId &&
                                                        d.DeviceConfigurationId == cfgId &&
                                                        !d.IsDeleted, ct);

                if (otherUses)
                    throw new InvalidOperationException("DeviceConfiguration is referenced by other devices and cannot be deleted. Detach it first or remove other references.");
            }

            // Soft delete instead of physical delete
            device.IsDeleted = true;

            // Optionally update timestamp or audit fields here if needed
            // device.DeletedAt = DateTime.UtcNow;

            _db.Devices.Update(device);
            await _db.SaveChangesAsync(ct);

            _log.LogInformation("Soft deleted device {DeviceId}", deviceId);
        }
        public Task<Device?> GetDeviceAsync(Guid deviceId, CancellationToken ct = default)
            => _db.Devices
                  .Include(d => d.DeviceConfiguration)
                  .AsNoTracking()
                  .FirstOrDefaultAsync(d => d.DeviceId == deviceId && !d.IsDeleted, ct);



        public async Task<List<Device>> GetDeletedDevicesAsync(CancellationToken ct = default)
        {
            return await _db.Devices
                            .Where(d => d.IsDeleted)
                            .Include(d => d.DeviceConfiguration)
                            .AsNoTracking()
                            .ToListAsync(ct);
        }

        // --- Get one soft-deleted device
        public Task<Device?> GetDeletedDeviceAsync(Guid deviceId, CancellationToken ct = default)
            => _db.Devices
                  .Where(d => d.IsDeleted)
                  .Include(d => d.DeviceConfiguration)
                  .AsNoTracking()
                  .FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);

        // --- Restore soft-deleted device
        public async Task RestoreDeviceAsync(Guid deviceId, CancellationToken ct = default)
        {
            var device = await _db.Devices.FindAsync(new object[] { deviceId }, ct);
            if (device == null) throw new KeyNotFoundException("Device not found");
            if (!device.IsDeleted)
            {
                _log.LogWarning("Attempted to restore device {DeviceId} but it is not deleted", deviceId);
                return; // or throw if you prefer
            }

            // If there's any business rule preventing restore (example: config removed) handle here.
            device.IsDeleted = false;
            // Optionally update timestamps: device.UpdatedAt = DateTime.UtcNow;

            _db.Devices.Update(device);
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Restored device {DeviceId}", deviceId);
        }

        // --- Permanently delete (hard delete) a device and related resources (if desired)
        public async Task PermanentlyDeleteDeviceAsync(Guid deviceId, CancellationToken ct = default)
        {
            var device = await _db.Devices.FindAsync(new object[] { deviceId }, ct);
            if (device == null)
                throw new KeyNotFoundException("Device not found");

            // If you want only-allow-hard-delete-for-already-soft-deleted:
            // if (!device.IsDeleted) throw new InvalidOperationException("Device must be soft-deleted first.");

            // Remove related child rows if cascade isn't configured (uncomment if needed)
            // var ports = _db.DeviceSlaves.Where(p => p.DeviceId == deviceId);
            // _db.DeviceSlaves.RemoveRange(ports);
            // var portSets = _db.DeviceSlaveSets.Where(ps => ps.DeviceId == deviceId);
            // _db.DeviceSlaveSets.RemoveRange(portSets);

            if (device.DeviceConfigurationId is Guid cfgId)
            {
                // ensure no other non-deleted devices reference the same config
                var otherUses = await _db.Devices
                                         .AsNoTracking()
                                         .AnyAsync(d => d.DeviceId != deviceId &&
                                                        d.DeviceConfigurationId == cfgId &&
                                                        !d.IsDeleted, ct);

                if (otherUses)
                {
                    // detach only device, keep config
                    _db.Devices.Remove(device);
                    await _db.SaveChangesAsync(ct);
                    _log.LogInformation("Hard-deleted device {DeviceId} but kept shared configuration {CfgId}", deviceId, cfgId);
                    return;
                }

                // safe to delete the config too
                var cfg = await _db.DeviceConfigurations.FindAsync(new object[] { cfgId }, ct);
                if (cfg != null)
                    _db.DeviceConfigurations.Remove(cfg);
            }

            _db.Devices.Remove(device);
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Hard-deleted device {DeviceId} and its configuration if not shared", deviceId);
        }

        public async Task<Guid> AddConfigurationAsync(Guid deviceId, DeviceConfigurationDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var device = await _db.Devices.FindAsync(new object[] { deviceId }, ct);
            if (device == null) throw new KeyNotFoundException("Device not found");
            if (device.IsDeleted) throw new InvalidOperationException("Cannot attach configuration to a deleted device.");

            var cfg = new DeviceConfiguration
            {
                ConfigurationId = Guid.NewGuid(),
                Name = string.IsNullOrWhiteSpace(dto.Name) ? $"{device.Name}-cfg" : dto.Name.Trim(),
                PollIntervalMs = dto.PollIntervalMs > 0 ? dto.PollIntervalMs : 1000,
                ProtocolSettingsJson = string.IsNullOrWhiteSpace(dto.ProtocolSettingsJson) ? "{}" : dto.ProtocolSettingsJson
            };

            await _db.DeviceConfigurations.AddAsync(cfg, ct);
            device.DeviceConfigurationId = cfg.ConfigurationId;
            await _db.SaveChangesAsync(ct);

            _log.LogInformation("Added configuration {CfgId} to device {DeviceId}", cfg.ConfigurationId, deviceId);
            return cfg.ConfigurationId;
        }




















        public async Task<Guid> AddPortAsync(Guid deviceId, AddPortDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var device = await _db.Devices.FindAsync(new object[] { deviceId }, ct);
            if (device == null || device.IsDeleted) throw new KeyNotFoundException("Device not found");

            var exists = await _db.DeviceSlaves.AnyAsync(p => p.DeviceId == deviceId && p.slaveIndex == dto.slaveIndex, ct);
            if (exists) throw new InvalidOperationException($"Port with index {dto.slaveIndex} already exists");

            var port = new DeviceSlave
            {
                DeviceId = deviceId,
                slaveIndex = dto.slaveIndex,
                IsHealthy = dto.IsHealthy,
                Registers = dto.Registers.Select(r => new Register
                {
                    RegisterAddress = r.RegisterAddress,
                    RegisterLength = r.RegisterLength,
                    DataType = r.DataType,
                    Scale = r.Scale,
                    Unit = r.Unit,
                    ByteOrder = r.ByteOrder,
                    WordSwap = r.WordSwap,
                    IsHealthy = r.IsHealthy
                }).ToList()
            };

            await _db.DeviceSlaves.AddAsync(port, ct);
            await _db.SaveChangesAsync(ct);
            return port.deviceSlaveId;
        }

        // Update port: REPLACE registers with DTO list — robust approach
        public async Task UpdatePortAsync(Guid deviceId, int slaveIndex, AddPortDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            // find the port
            var port = await _db.DeviceSlaves
                .AsNoTracking() // load fresh, we'll attach as needed
                .FirstOrDefaultAsync(p => p.DeviceId == deviceId && p.slaveIndex == slaveIndex, ct);

            if (port == null) throw new KeyNotFoundException($"Port {slaveIndex} not found for device");

            // Start a transaction for atomicity
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // 1) Delete existing registers for this port by DB query (ensures matching rows are deleted)
                var existingRegisters = _db.Registers.Where(r => r.deviceSlaveId == port.deviceSlaveId);
                _db.Registers.RemoveRange(existingRegisters);
                await _db.SaveChangesAsync(ct); // commit deletes

                // 2) Attach the port entity so we can update its properties and add new registers
                port = await _db.DeviceSlaves.FirstOrDefaultAsync(p => p.DeviceId == deviceId && p.slaveIndex == slaveIndex, ct);
                if (port == null)
                {
                    // very unlikely (deleted between calls)
                    throw new InvalidOperationException("Port disappeared during update; please retry.");
                }

                port.IsHealthy = dto.IsHealthy;

                // 3) Add new registers from DTO
                var newRegisters = dto.Registers.Select(r => new Register
                {
                    RegisterAddress = r.RegisterAddress,
                    RegisterLength = r.RegisterLength,
                    DataType = r.DataType,
                    Scale = r.Scale,
                    Unit = r.Unit,
                    ByteOrder = r.ByteOrder,
                    WordSwap = r.WordSwap,
                    IsHealthy = r.IsHealthy,
                    deviceSlaveId = port.deviceSlaveId
                }).ToList();

                // Use AddRange on DB set so EF tracks them correctly
                await _db.Registers.AddRangeAsync(newRegisters, ct);

                // Save all changes (adds)
                await _db.SaveChangesAsync(ct);

                // commit transaction
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _log.LogError(ex, "Concurrency error updating port {DeviceId}/{slaveIndex}", deviceId, slaveIndex);
                await tx.RollbackAsync(ct);
                throw new InvalidOperationException("Concurrency error while updating port", ex);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error updating port {DeviceId}/{slaveIndex}", deviceId, slaveIndex);
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        // optional getter
        public async Task<DeviceSlave?> GetPortAsync(Guid deviceId, int slaveIndex, CancellationToken ct = default)
        {
            return await _db.DeviceSlaves
                .Include(p => p.Registers)
                .FirstOrDefaultAsync(p => p.DeviceId == deviceId && p.slaveIndex == slaveIndex, ct);
        }




















        public async Task<List<DeviceSlave>> GetPortsByDeviceAsync(Guid deviceId, CancellationToken ct)
        {
            if (deviceId == Guid.Empty)
                throw new ArgumentException("Device ID cannot be empty.", nameof(deviceId));

            return await _db.DeviceSlaves
                .Include(p => p.Registers)     
                .Where(p => p.DeviceId == deviceId)
                .ToListAsync(ct);
        }
 }
}
