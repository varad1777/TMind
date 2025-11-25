using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyApp.Application.Dtos;
using MyApp.Application.Interfaces;
using MyApp.Domain.Entities;
using MyApp.Infrastructure.Services;
using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MyApp.Api.Controllers
{
    [ApiController]
    [Route("api/devices")]
    public class DevicesController : ControllerBase
    {
        private readonly IDeviceManager _mgr;
        private readonly ILogger<DevicesController> _log;

        public DevicesController(IDeviceManager mgr, ILogger<DevicesController> log)
        {
            _mgr = mgr;
            _log = log;
        }

        // POST /api/devices
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] CreateDeviceDto dto, CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                               .SelectMany(v => v.Errors)
                               .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? e.Exception?.Message : e.ErrorMessage)
                               .Where(s => !string.IsNullOrWhiteSpace(s));
                return BadRequest(ApiResponse<object>.Fail($"Validation failed: {string.Join("; ", errors)}"));
            }

            try
            {
                var id = await _mgr.CreateDeviceAsync(dto, ct);
                var payload = new { deviceId = id };
                return CreatedAtAction(nameof(Get), new { id }, ApiResponse<object>.Ok(payload));
            }
            catch (ArgumentException aex)
            {
                return BadRequest(ApiResponse<object>.Fail(aex.Message));
            }
            catch (InvalidOperationException iex)
            {
                return Conflict(ApiResponse<object>.Fail(iex.Message));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Create device failed");
                return StatusCode((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail("An unexpected error occurred."));
            }
        }

        // GET /api/devices
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAll(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null,
            CancellationToken ct = default)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            try
            {
                var (devices, totalCount) = await _mgr.GetAllDevicesAsync(pageNumber, pageSize, searchTerm, ct);
                var result = new
                {
                    Items = devices,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                };

                return Ok(ApiResponse<object>.Ok(result));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "GetAll devices failed");
                return StatusCode((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail("An unexpected error occurred."));
            }
        }

        // GET /api/devices/{id}
        [HttpGet("{id:guid}")]
        [AllowAnonymous]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct = default)
        {
            try
            {
                var d = await _mgr.GetDeviceAsync(id, ct);
                if (d == null) return NotFound(ApiResponse<object>.Fail("Device not found."));
                return Ok(ApiResponse<object>.Ok(d));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Get device failed for {DeviceId}", id);
                return StatusCode((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail("An unexpected error occurred."));
            }
        }

        // PUT /api/devices/{id}
        [HttpPut("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDeviceRequest request, CancellationToken ct = default)
        {
            if (request == null)
                return BadRequest(ApiResponse<object>.Fail("Request body is required."));

            // Validate nested DTOs cleanly
            ModelState.Clear();
            if (!TryValidateModel(request.Device, nameof(request.Device)))
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Where(s => !string.IsNullOrWhiteSpace(s));
                return BadRequest(ApiResponse<object>.Fail($"Validation failed for device: {string.Join("; ", errors)}"));
            }

            ModelState.Clear();
            if (request.Configuration != null && !TryValidateModel(request.Configuration, nameof(request.Configuration)))
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Where(s => !string.IsNullOrWhiteSpace(s));
                return BadRequest(ApiResponse<object>.Fail($"Validation failed for configuration: {string.Join("; ", errors)}"));
            }

            try
            {
                await _mgr.UpdateDeviceAsync(id, request.Device, request.Configuration, ct);
                return Ok(ApiResponse<object>.Ok(null));
            }
            catch (KeyNotFoundException)
            {
                return NotFound(ApiResponse<object>.Fail("Device not found."));
            }
            catch (ArgumentException aex)
            {
                return BadRequest(ApiResponse<object>.Fail(aex.Message));
            }
            catch (InvalidOperationException ioex)
            {
                return BadRequest(ApiResponse<object>.Fail(ioex.Message));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Update device failed for {DeviceId}", id);
                return StatusCode((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail("An unexpected error occurred."));
            }
        }




        // POST -> create new port
        [HttpPost("{id:guid}/ports")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AddPort(Guid id, [FromBody] AddPortDto dto, CancellationToken ct)
        {
            if (dto == null) return BadRequest(new { error = "Payload is required" });

            try
            {
                var newPortId = await _mgr.AddPortAsync(id, dto, ct);
                // return created with location to GET single port
                return CreatedAtAction(nameof(GetPort), new { deviceId = id, slaveIndex = dto.slaveIndex }, new { deviceSlaveId = newPortId });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                // e.g., port already exists
                return Conflict(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // PUT -> update existing port
        [HttpPut("{id:guid}/ports/{slaveIndex:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdatePort(Guid id, int slaveIndex, [FromBody] AddPortDto dto, CancellationToken ct)
        {
            if (dto == null) return BadRequest(new { error = "Payload required" });
            try
            {
                await _mgr.UpdatePortAsync(id, slaveIndex, dto, ct);
                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                // concurrency or logical errors
                return Conflict(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }


        // GET single port (used by CreatedAtAction)
        [HttpGet("{deviceId:guid}/ports/{slaveIndex:int}")]
        public async Task<IActionResult> GetPort(Guid deviceId, int slaveIndex, CancellationToken ct)
        {
            var port = await _mgr.GetPortAsync(deviceId, slaveIndex, ct);
            if (port == null) return NotFound();
            return Ok(port);
        }




        // POST /api/devices/{id}/configuration
        [HttpPost("{id:guid}/configuration")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AddConfiguration(Guid id, [FromBody] MyApp.Application.Dtos.DeviceConfigurationDto dto, CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? e.Exception?.Message : e.ErrorMessage)
                                               .Where(s => !string.IsNullOrWhiteSpace(s));
                return BadRequest(ApiResponse<object>.Fail($"Validation failed: {string.Join("; ", errors)}"));
            }

            try
            {
                var cfgId = await _mgr.AddConfigurationAsync(id, dto, ct);
                return CreatedAtAction(nameof(Get), new { id }, ApiResponse<object>.Ok(new { deviceId = id, configurationId = cfgId }));
            }
            catch (KeyNotFoundException knf)
            {
                return NotFound(ApiResponse<object>.Fail(knf.Message));
            }
            catch (ArgumentException aex)
            {
                return BadRequest(ApiResponse<object>.Fail(aex.Message));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Add configuration failed for device {Device}", id);
                return StatusCode((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail("An unexpected error occurred."));
            }
        }

        // DELETE /api/devices/{id}  -> soft delete
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
        {
            try
            {
                await _mgr.DeleteDeviceAsync(id, ct);
                return Ok(ApiResponse<object>.Ok(null));
            }
            catch (KeyNotFoundException)
            {
                return NotFound(ApiResponse<object>.Fail("Device not found."));
            }
            catch (InvalidOperationException ioex)
            {
                return BadRequest(ApiResponse<object>.Fail(ioex.Message));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Delete device failed for {DeviceId}", id);
                return StatusCode((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail("An unexpected error occurred."));
            }
        }

        // GET /api/devices/deleted
        [HttpGet("deleted")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetDeletedDevices(CancellationToken ct = default)
        {
            try
            {
                var list = await _mgr.GetDeletedDevicesAsync(ct);
                return Ok(ApiResponse<object>.Ok(list));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "GetDeleted devices failed");
                return StatusCode((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail("An unexpected error occurred."));
            }
        }

        // GET /api/devices/deleted/{id}
        [HttpGet("deleted/{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetDeletedDevice(Guid id, CancellationToken ct = default)
        {
            try
            {
                var device = await _mgr.GetDeletedDeviceAsync(id, ct);
                if (device == null) return NotFound(ApiResponse<object>.Fail("Deleted device not found."));
                return Ok(ApiResponse<object>.Ok(device));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "GetDeletedDevice failed for {DeviceId}", id);
                return StatusCode((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail("An unexpected error occurred."));
            }
        }

        // POST /api/devices/{id}/restore
        [HttpPost("{id:guid}/restore")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RestoreDevice(Guid id, CancellationToken ct = default)
        {
            try
            {
                await _mgr.RestoreDeviceAsync(id, ct);
                return Ok(ApiResponse<object>.Ok(null));
            }
            catch (KeyNotFoundException)
            {
                return NotFound(ApiResponse<object>.Fail("Device not found."));
            }
            catch (InvalidOperationException ioex)
            {
                _log.LogWarning(ioex, "Restore prevented for device {DeviceId}", id);
                return BadRequest(ApiResponse<object>.Fail(ioex.Message));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Restore device failed for {DeviceId}", id);
                return StatusCode((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail("An unexpected error occurred."));
            }
        }

        // DELETE /api/devices/{id}/hard  -- permanent delete
        [HttpDelete("{id:guid}/hard")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> HardDeleteDevice(Guid id, CancellationToken ct = default)
        {
            try
            {
                await _mgr.PermanentlyDeleteDeviceAsync(id, ct);
                return Ok(ApiResponse<object>.Ok(null));
            }
            catch (KeyNotFoundException)
            {
                return NotFound(ApiResponse<object>.Fail("Device not found."));
            }
            catch (InvalidOperationException ioex)
            {
                return BadRequest(ApiResponse<object>.Fail(ioex.Message));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Hard delete failed for {DeviceId}", id);
                return StatusCode((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail("An unexpected error occurred."));
            }
        }

        // GET /api/devices/{deviceId}/ports
        [HttpGet("{deviceId}/ports")]
        public async Task<IActionResult> GetPortsByDevice(Guid deviceId, CancellationToken ct = default)
        {
            try
            {
                var ports = await _mgr.GetPortsByDeviceAsync(deviceId, ct);
                return Ok(ApiResponse<object>.Ok(ports));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Get ports by device failed for {DeviceId}", deviceId);
                return StatusCode((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail("An unexpected error occurred."));
            }
        }

        // GET /api/devices/{deviceId}/ports/{portId}
        [HttpGet("{deviceId:guid}/ports/{portId:guid}")]
        public async Task<IActionResult> GetPort(Guid deviceId, Guid portId, CancellationToken ct = default)
        {
            try
            {
                var ports = await _mgr.GetPortsByDeviceAsync(deviceId, ct);
                var port = ports.FirstOrDefault(p => p.deviceSlaveId == portId);
                if (port == null) return NotFound(ApiResponse<object>.Fail("Port not found."));
                return Ok(ApiResponse<object>.Ok(port));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Get port failed for device {DeviceId} port {PortId}", deviceId, portId);
                return StatusCode((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail("An unexpected error occurred."));
            }
        }




        [HttpPost("match-by-address")]
        public async Task<IActionResult> MatchByAddress([FromBody] SignalAddressRequest request, CancellationToken ct)
        {
            if (request?.RegisterAddresses == null || request.RegisterAddresses.Length == 0)
                return BadRequest("registerAddresses required");

            try
            {
                var matched = await _mgr.GetDevicesMatchingRegisterAddressesAsync(request.RegisterAddresses, ct);
                //return Ok(matched);
                return Ok(ApiResponse<object>.Ok(matched));
            }
            catch (ArgumentException aex)
            {
                return BadRequest(ApiResponse<object>.Fail(aex.Message));
                
            }
            catch (OperationCanceledException)
            {
                return StatusCode(StatusCodes.Status408RequestTimeout, "request cancelled");
            }
            catch (Exception ex)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail(ex.Message));
            }
        }




    }
      







    // If you haven't already moved this DTO to the Application.Dtos project, keep it or move it.
    public class UpdateDeviceRequest
    {
        public UpdateDeviceDto Device { get; set; } = new UpdateDeviceDto();
        public DeviceConfigurationDto? Configuration { get; set; }
    }
}
