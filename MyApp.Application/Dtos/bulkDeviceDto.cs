using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyApp.Application.Dtos
{

    public class BulkCreateDeviceDto
    {
        [Required]
        [MinLength(1)]
        [MaxLength(20, ErrorMessage = "Cannot create more than 20 devices at once.")]
        public List<CreateDeviceDto> Devices { get; set; } = new();
    }

    public class BulkCreateDeviceResultDto
    {
        public List<Guid> CreatedDeviceIds { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }
}
