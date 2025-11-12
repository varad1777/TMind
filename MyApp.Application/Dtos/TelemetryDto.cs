using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyApp.Application.Dtos
{
    public record TelemetryDto(
    Guid DeviceId,
    Guid DevicePortId,
    int PortIndex,
    int RegisterAddress,
    string SignalType,
    double Value,
    string Unit,
    DateTime Timestamp
);
}
