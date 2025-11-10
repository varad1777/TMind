using System;
using System.ComponentModel.DataAnnotations;

namespace MyApp.Domain.Entities
{
    public class DevicePortSet
    {

        [Key]
        public Guid PortSetId { get; set; } = Guid.NewGuid();
        public Guid DeviceId { get; set; }
        public Device? Device { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string State { get; set; } = "Active"; // Active, Faulty, Retired
    }
}
