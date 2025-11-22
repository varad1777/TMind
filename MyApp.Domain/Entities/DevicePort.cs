using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MyApp.Domain.Entities
{
    public class DeviceSlave
    {
        [Key]
        public Guid deviceSlaveId { get; set; } = Guid.NewGuid();

        public Guid DeviceId { get; set; }
        public Device Device { get; set; } = null!;

        [Required]
        public int slaveIndex { get; set; }   // logical index

        public List<Register> Registers { get; set; } = new List<Register>();

        public bool IsHealthy { get; set; } = true;
    }

    public class Register
    {
        [Key]
        public Guid RegisterId { get; set; } = Guid.NewGuid();

        [Range(0, 65535)]
        public int RegisterAddress { get; set; }

        [Range(1, 10)]
        public int RegisterLength { get; set; } = 1;

        [Required, StringLength(50)]
        public string DataType { get; set; } = "float32";

        [Range(0.0000001, double.MaxValue)]
        public double Scale { get; set; } = 1.0;

        [StringLength(50)]
        public string? Unit { get; set; }

        public bool IsHealthy { get; set; } = true;

        [RegularExpression("Big|Little")]
        public string? ByteOrder { get; set; }

        public bool WordSwap { get; set; } = false;

        public Guid deviceSlaveId { get; set; }
        [JsonIgnore]
        public DeviceSlave DeviceSlave { get; set; } = null!;

    }



    //public class DeviceSlaveSet
    //{
    //    [Key]
    //    public Guid PortSetId { get; set; } = Guid.NewGuid();

    //    public Guid DeviceId { get; set; }
    //    public Device Device { get; set; } = null!;
    //}
}
