using System;
using System.Collections.Generic;
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

        public List<DeviceSlaveDto>? Ports { get; set; }

        public DeviceConfigurationDto? Configuration { get; set; }
    }

    public class DeviceSlaveDto
    {
        [Range(0, int.MaxValue, ErrorMessage = "slaveIndex must be a non-negative number.")]
        public int slaveIndex { get; set; }

        [Range(0, 65535, ErrorMessage = "RegisterAddress must be between 0 and 65535.")]
        public int RegisterAddress { get; set; }

        [Range(1, 10, ErrorMessage = "RegisterLength must be between 1 and 10.")]
        public int RegisterLength { get; set; } = 1;

        [Required(ErrorMessage = "DataType is required.")]
        [StringLength(50, ErrorMessage = "DataType cannot exceed 50 characters.")]
        public string DataType { get; set; } = "float32";

        [Range(0.0000001, double.MaxValue, ErrorMessage = "Scale must be a positive number.")]
        public double Scale { get; set; } = 1.0;

        [StringLength(50, ErrorMessage = "Unit cannot exceed 50 characters.")]
        public string? Unit { get; set; }

        public bool IsHealthy { get; set; } = true;
    }
}
