using System;
using System.ComponentModel.DataAnnotations;

namespace MyApp.Domain.Entities
{
    public class DevicePort
    {
        [Key]
        public Guid DevicePortId { get; set; } = Guid.NewGuid();
        public Guid PortSetId { get; set; }
        public DevicePortSet? PortSet { get; set; }
        public int PortIndex { get; set; } // 0..7
        public int RegisterAddress { get; set; }
        public int RegisterLength { get; set; } = 2; // float32 -> 2 registers
        public string DataType { get; set; } = "float32";
        public double Scale { get; set; } = 1.0;
        public string? Unit { get; set; }
        public bool IsHealthy { get; set; } = true;
        public Guid DeviceId { get; set; }
    }
}
