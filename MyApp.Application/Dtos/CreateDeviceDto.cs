using System;
using System.ComponentModel.DataAnnotations;

namespace MyApp.Application.Dtos
{
    public class CreateDeviceDto
    {
        [Required(ErrorMessage = "Device name is required.")]
        [StringLength(100, MinimumLength = 3, ErrorMessage = "Device name must be between 3 and 100 characters.")]
        public string Name { get; set; } = null!;

        [StringLength(255, ErrorMessage = "Description cannot exceed 255 characters.")]
        public string? Description { get; set; }

      
    }
}
