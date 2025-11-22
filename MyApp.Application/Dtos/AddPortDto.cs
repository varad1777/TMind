using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyApp.Application.Dtos
{


    public class RegisterDto
    {
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
    }

    public class AddPortDto
    {
        [Range(0, int.MaxValue)]
        public int slaveIndex { get; set; }

       
        public List<RegisterDto> Registers { get; set; } = new();

        public bool IsHealthy { get; set; } = true;
    }


}
