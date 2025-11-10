using System.ComponentModel.DataAnnotations;

namespace MyApp.Application.Dtos
{
    // Partial update DTO: only device properties, no configuration handling here.
    public class UpdateDeviceDto
    {
        [StringLength(100, MinimumLength = 3, ErrorMessage = "Device name must be between 3 and 100 characters.")]
        public string? Name { get; set; }

        [StringLength(255, ErrorMessage = "Description cannot exceed 255 characters.")]
        public string? Description { get; set; }

        /// <summary>
        /// Optional: change the Protocol (e.g. "ModbusTCP")
        /// </summary>
        [StringLength(100, ErrorMessage = "Protocol cannot exceed 100 characters.")]
        public string? Protocol { get; set; }
    }
}
