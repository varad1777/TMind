using Microsoft.AspNetCore.Mvc;
using MyApp.Application.Dtos;
using MyApp.Application.Interfaces;
using System.Net;

namespace MyApp.Api.Controllers
{
    [ApiController]
    [Route("api/devices")]
    public class DevicesController : ControllerBase
    {
        private readonly IDeviceManager _mgr;
        private readonly ILogger<DevicesController> _log;
        public DevicesController(IDeviceManager mgr, ILogger<DevicesController> log) { _mgr = mgr; _log = log; }

        // POST /api/devices
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateDeviceDto dto, CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
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
            catch (Exception ex)
            {
                _log.LogError(ex, "Create device failed");
                return StatusCode((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail("An unexpected error occurred."));
            }
        }

        // GET /api/devices
        [HttpGet]
        public async Task<IActionResult> GetAll(CancellationToken ct = default)
        {
            try
            {
                var devices = await _mgr.GetAllDevicesAsync(ct);
                return Ok(ApiResponse<object>.Ok(devices));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "GetAll devices failed");
                return StatusCode((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail("An unexpected error occurred."));
            }
        }

        // GET /api/devices/{id}
        [HttpGet("{id:guid}")]
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
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDeviceRequest request, CancellationToken ct = default)
        {
            if (request == null)
                return BadRequest(ApiResponse<object>.Fail("Request body is required."));

            // validate inner DTOs
            if (!TryValidateModel(request.Device))
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return BadRequest(ApiResponse<object>.Fail($"Validation failed for device: {string.Join("; ", errors)}"));
            }

            if (request.Configuration != null && !TryValidateModel(request.Configuration))
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return BadRequest(ApiResponse<object>.Fail($"Validation failed for configuration: {string.Join("; ", errors)}"));
            }

            try
            {
                await _mgr.UpdateDeviceAsync(id, request.Device, request.Configuration, ct);
                return Ok(ApiResponse<object>.Ok(null)); // consistent response format
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

        // DELETE /api/devices/{id}  -> soft delete
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
        {
            try
            {
                await _mgr.DeleteDeviceAsync(id, ct);
                return Ok(ApiResponse<object>.Ok(null)); // soft-deleted successfully
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
    }

    // Small request DTO — move to MyApp.Application.Dtos if you prefer.
    public class UpdateDeviceRequest
    {
        public UpdateDeviceDto Device { get; set; } = new UpdateDeviceDto();
        public DeviceConfigurationDto? Configuration { get; set; }
    }
}
