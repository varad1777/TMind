using System;
using System.ComponentModel.DataAnnotations;

namespace MyApp.Domain.Entities
{
    public class Device
    {
        [Key]
        public Guid DeviceId { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string Protocol { get; set; } = "ModbusTCP";
   
        public Guid? DeviceConfigurationId { get; set; }
        public DeviceConfiguration? DeviceConfiguration { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
