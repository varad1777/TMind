using Microsoft.EntityFrameworkCore;
using MyApp.Api.Extension;
using MyApp.Application.Interfaces;
using MyApp.Infrastructure.Data;
using MyApp.Infrastructure.SignalRHub;
using MyApp.Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;


var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();                       // <-- important
builder.Services.AddEndpointsApiExplorer();              // discovers endpoints for swagger
builder.Services.AddSwaggerGen();                        // <-- registers ISwaggerProvider

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IDeviceManager, DeviceManager>();
builder.Services.AddHostedService<ModbusPollerHostedService>();
builder.Services.AddCustomAuthentication(builder.Configuration);
builder.Services.AddSignalR();

// Allow your frontend to call the API
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000") // frontend URL
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});


var app = builder.Build();

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();            // uses ISwaggerProvider registered by AddSwaggerGen
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API v1");
        // c.RoutePrefix = "";   // uncomment to serve swagger at root
    });
}
app.UseCors("AllowFrontend");
app.MapHub<ModbusHub>("/hubs/modbus");

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers(); // <-- map controller routes

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}


