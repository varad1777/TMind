using System;

namespace MyApp.Domain.Entities
{
    public class Telemetry
    {
        public long TelemetryId { get; set; }
        public Guid AssetId { get; set; } // optional, we'll set null if not mapped
        public string SignalType { get; set; } = null!;
        public double Value { get; set; }
        public string? Unit { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Guid? MappingId { get; set; }
        public Guid DevicePortId { get; set; }
    }
}
