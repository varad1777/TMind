using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using MyApp.Infrastructure.Data;
using MyApp.Domain.Entities;

namespace MyApp.Api.Middleware
{
    public class ApiLoggingMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiLoggingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, AppDbContext db)
        {
            var sw = Stopwatch.StartNew();

            await _next(context);

            sw.Stop();

            await db.ApiLogs.AddAsync(new ApiLog
            {
                Api = context.Request.Path,
                Duration = sw.ElapsedMilliseconds,
                Timestamp = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }
    }
}
