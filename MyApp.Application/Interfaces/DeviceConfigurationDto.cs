using MyApp.Application.Dtos;
using MyApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MyApp.Application.Interfaces
{
    public interface IDeviceManager
    {
        // Create new device (ports and portset auto-created)
        Task<Guid> CreateDeviceAsync(CreateDeviceDto dto, CancellationToken ct = default);

        // Update device info, and optionally configuration in the same request
        Task UpdateDeviceAsync(Guid deviceId, UpdateDeviceDto dto, DeviceConfigurationDto? configDto = null, CancellationToken ct = default);

        // Delete device (and its configuration if not shared)
        Task DeleteDeviceAsync(Guid deviceId, CancellationToken ct = default);

        // Get one device (with configuration included)
        Task<Device?> GetDeviceAsync(Guid deviceId, CancellationToken ct = default);

        // Get all devices (with configurations)
        Task<(List<Device> Devices, int TotalCount)> GetAllDevicesAsync(int pageNumber, 
      int pageSize,
      string? searchTerm,
      CancellationToken ct = default);

        Task<List<Device>> GetDeletedDevicesAsync(CancellationToken ct = default);
        Task<Device?> GetDeletedDeviceAsync(Guid deviceId, CancellationToken ct = default);
        Task RestoreDeviceAsync(Guid deviceId, CancellationToken ct = default);
        Task PermanentlyDeleteDeviceAsync(Guid deviceId, CancellationToken ct = default);


        // Create and attach a configuration to a device
        Task<Guid> AddConfigurationAsync(Guid deviceId, DeviceConfigurationDto dto, CancellationToken ct = default);

        Task<List<DeviceSlave>> GetPortsByDeviceAsync(Guid deviceId , CancellationToken ct);

        Task<DeviceSlave?> GetPortAsync(Guid deviceId, int slaveIndex, CancellationToken ct = default);


        Task<List<MatchedDeviceDto>> GetDevicesMatchingRegisterAddressesAsync(int[] registerAddresses, CancellationToken ct);        Task UpdatePortAsync(Guid deviceId, int slaveIndex, AddPortDto dto, CancellationToken ct = default);
        Task<Guid> AddPortAsync(Guid deviceId, AddPortDto dto, CancellationToken ct = default);
    }
}
