using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.Infrastructure.Data;

namespace MyApp.Api.Controllers
{
    [ApiController]
    [Route("api/stats")]
    public class ResponseStatsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public ResponseStatsController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet("avg-response-time")]
        public async Task<IActionResult> GetAverage()
        {
            var logs = await _db.ApiLogs
                .OrderByDescending(x => x.Id)
                .Take(1000)
                .ToListAsync();

            var avg = logs.Count == 0 ? 0 : logs.Average(x => x.Duration);

            return Ok(new { avgResponseTime = avg });
        }
    }
}