using System;
using System.ComponentModel.DataAnnotations;

namespace MyApp.Domain.Entities
{
    public class DeviceConfiguration
    {
        [Key]
        public Guid ConfigurationId { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = null!;
        public int PollIntervalMs { get; set; } = 1000;
        public string? ProtocolSettingsJson { get; set; } // store Modbus map etc.
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
