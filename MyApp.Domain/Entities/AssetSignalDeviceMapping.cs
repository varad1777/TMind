using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyApp.Domain.Entities
{
    public class AssetSignalDeviceMapping
    {
        [Key]
        public Guid MappingId { get; set; } = Guid.NewGuid();

        // Asset Service
        [Required]
        public Guid AssetId { get; set; }

        [Required]
        public Guid SignalTypeId { get; set; }

        // Device Service
        [Required]
        public Guid DeviceId { get; set; }

        [Required]
        public Guid DevicePortId { get; set; }

        [Required]
        public string SignalUnit { get; set; }

        public string SignalName { get; set; }

        public int RegisterAdress { get; set; }

        public Guid registerId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
