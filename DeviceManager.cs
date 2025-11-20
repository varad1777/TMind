public class DeviceManager : IDeviceManager
{
    private readonly IPortRepository _portRepository;

    public DeviceManager(IPortRepository portRepository)
    {
        _portRepository = portRepository;
    }

    public IEnumerable<Port> GetPortsByDevice(Guid deviceId)
    {
        return _portRepository.GetPortsByDeviceId(deviceId);
    }

    public async Task<IEnumerable<Port>> GetPortsByDeviceAsync(Guid deviceId)
    {
        return await _portRepository.GetPortsByDeviceIdAsync(deviceId);
    }

    public async Task<bool> UpdatePortAsync(Port port)
    {
        return await _portRepository.UpdatePortAsync(port);
    }
}