using Microsoft.AspNetCore.Mvc;
using MyApp.Application.Dtos;
using MyApp.Application.Interfaces;

namespace MyApp.Api.Controllers
{
    [ApiController]
    [Route("api/devices")]
    public class DevicesController : Controller
    {
        private readonly IDeviceManager _mgr;
        private readonly ILogger<DevicesController> _log;
        public DevicesController(IDeviceManager mgr, ILogger<DevicesController> log) { _mgr = mgr; _log = log; }

        // POST /api/devices
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateDeviceDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    error = "Validation failed",
                    details = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)
                });
            }

            try
            {
                var id = await _mgr.CreateDeviceAsync(dto);
                return CreatedAtAction(nameof(Get), new { id }, new { deviceId = id });
            }
            catch (ArgumentException aex)
            {
                return BadRequest(new { error = aex.Message });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Create device failed");
                return StatusCode(500, new { error = "An unexpected error occurred." });
            }
        }

        // GET /api/devices
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var devices = await _mgr.GetAllDevicesAsync();
                return Ok(devices);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "GetAll devices failed");
                return StatusCode(500, new { error = "An unexpected error occurred." });
            }
        }

        // GET /api/devices/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var d = await _mgr.GetDeviceAsync(id);
            if (d == null) return NotFound();
            return Ok(d);
        }

        // PUT /api/devices/{id}
        // Accepts device updates + optional configuration in one request.
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDeviceRequest request)
        {
            if (request == null)
                return BadRequest(new { error = "Request body is required." });

            // Validate inner DTOs
            // Note: ApiController modelbinding would normally validate; we keep manual checks to match your style.
            if (!TryValidateModel(request.Device))
            {
                return BadRequest(new
                {
                    error = "Validation failed for device",
                    details = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)
                });
            }

            if (request.Configuration != null && !TryValidateModel(request.Configuration))
            {
                return BadRequest(new
                {
                    error = "Validation failed for configuration",
                    details = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)
                });
            }

            try
            {
                await _mgr.UpdateDeviceAsync(id, request.Device, request.Configuration);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { error = "Device not found." });
            }
            catch (ArgumentException aex)
            {
                return BadRequest(new { error = aex.Message });
            }
            catch (InvalidOperationException ioex)
            {
                // e.g., delete-protection or other policy errors surfaced from service
                return BadRequest(new { error = ioex.Message });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Update device failed");
                return StatusCode(500, new { error = "An unexpected error occurred." });
            }
        }

        // DELETE /api/devices/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                await _mgr.DeleteDeviceAsync(id);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { error = "Device not found." });
            }
            catch (InvalidOperationException ioex)
            {
                // For example: configuration is shared and cannot be deleted
                return BadRequest(new { error = ioex.Message });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Delete device failed");
                return StatusCode(500, new { error = "An unexpected error occurred." });
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
