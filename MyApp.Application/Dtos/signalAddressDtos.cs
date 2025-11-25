using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyApp.Application.Dtos
{
   /// <summary>
    /// Request sent from frontend to Device Service
    /// </summary>
    public class SignalAddressRequest
    {
        public int[] RegisterAddresses { get; set; } = Array.Empty<int>();
    }

    /// <summary>
    /// Single matched Register
    /// </summary>
    public class MatchedRegisterDto
    {
      

        public Guid RegisterId { get; set; }
        public int RegisterAddress { get; set; }
        public int RegisterLength { get; set; }
        public string DataType { get; set; } = string.Empty;
        public bool IsHealthy { get; set; }
        public double Scale { get; set; }
        public string? Unit { get; set; }
    }

    /// <summary>
    /// Slave that contains matched registers
    /// </summary>
    public class MatchedSlaveDto
    {

      

        public Guid DeviceSlaveId { get; set; }
        public int SlaveIndex { get; set; }
        public bool IsHealthy { get; set; }
        public List<MatchedRegisterDto> MatchedRegisters { get; set; } = new();
    }

    /// <summary>
    /// Device that contains slaves which have matched registers
    /// </summary>
    public class MatchedDeviceDto
    {
       

        public Guid DeviceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Protocol { get; set; }
        public List<MatchedSlaveDto> MatchedSlaves { get; set; } = new();
    }
}
