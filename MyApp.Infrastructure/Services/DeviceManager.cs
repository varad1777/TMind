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




        public async Task<Guid> CreateDeviceAsync(CreateDeviceDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new ArgumentException("Name required");

            
            bool nameExists = await _db.Devices
                .AnyAsync(d => d.Name.ToLower() == dto.Name.ToLower() && !d.IsDeleted, ct);
            if (nameExists)
                throw new InvalidOperationException($"Device name '{dto.Name}' already exists.");

            int deviceCount = await _db.Devices.CountAsync(ct);
            if (deviceCount >= 100)
                throw new InvalidOperationException("Maximum number of devices (100) reached.");

            var device = new Device
            {
                Name = dto.Name,
                Description = dto.Description
            };
            await _db.Devices.AddAsync(device, ct);

            var portSet = new DevicePortSet { DeviceId = device.DeviceId };
            await _db.DevicePortSets.AddAsync(portSet, ct);

            int baseAddress = 40001;
            var units = new[] { "V", "A", "°C", "Hz", "mm/s", "L/min", "rpm", "N·m" };

            for (int i = 0; i < 8; i++)
            {
                _db.DevicePorts.Add(new DevicePort
                {
                    PortSetId = portSet.PortSetId,
                    PortIndex = i,
                    RegisterAddress = baseAddress + (i * 2),
                    RegisterLength = 2,
                    DataType = "float32",
                    Scale = 1.0,
                    IsHealthy = true,
                    Unit = units[i],
                    DeviceId = device.DeviceId
                });
            }

            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Created device {DeviceId} with portset {PortSet}", device.DeviceId, portSet.PortSetId);
            return device.DeviceId;
        }


        // remove: using System.Text.Json;

        public async Task UpdateDeviceAsync(Guid deviceId, UpdateDeviceDto dto, DeviceConfigurationDto? configDto = null, CancellationToken ct = default)
        {
            var device = await _db.Devices.FindAsync(new object[] { deviceId }, ct);
            if (device == null) throw new KeyNotFoundException("Device not found");

            // ✅ Prevent updates to soft-deleted devices
            if (device.IsDeleted)
                throw new InvalidOperationException("Cannot update a deleted device.");

            // Update device fields (only when provided)
            if (dto.Name != null)
            {
                var trimmed = dto.Name.Trim();
                if (trimmed.Length < 3 || trimmed.Length > 100)
                    throw new ArgumentException("Device name must be between 3 and 100 characters.", nameof(dto.Name));
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
            // var ports = _db.DevicePorts.Where(p => p.DeviceId == deviceId);
            // _db.DevicePorts.RemoveRange(ports);
            // var portSets = _db.DevicePortSets.Where(ps => ps.DeviceId == deviceId);
            // _db.DevicePortSets.RemoveRange(portSets);

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

    }
}
