using Microsoft.EntityFrameworkCore;
using MyApp.Domain.Entities;

namespace MyApp.Infrastructure.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> opts) : base(opts) { }

        public DbSet<Device> Devices => Set<Device>();
        public DbSet<DeviceConfiguration> DeviceConfigurations => Set<DeviceConfiguration>();
        public DbSet<DevicePort> DevicePorts => Set<DevicePort>();
        public DbSet<Register> Registers => Set<Register>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            // DEVICE
            mb.Entity<Device>()
              .HasKey(d => d.DeviceId);

            // Device → DevicePortSets (keep for other functionality)
          

            // Device → DevicePorts
            mb.Entity<Device>()
              .HasMany(d => d.DevicePorts)
              .WithOne(dp => dp.Device)
              .HasForeignKey(dp => dp.DeviceId)
              .OnDelete(DeleteBehavior.Cascade);

            // DEVICE PORT
            mb.Entity<DevicePort>()
                .HasKey(dp => dp.DevicePortId);

            // PortIndex must be unique per device
            mb.Entity<DevicePort>()
                .HasIndex(dp => new { dp.DeviceId, dp.PortIndex })
                .IsUnique();

            // DevicePort → Registers
            mb.Entity<DevicePort>()
                .HasMany(dp => dp.Registers)
                .WithOne(r => r.DevicePort)
                .HasForeignKey(r => r.DevicePortId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            // REGISTER
            mb.Entity<Register>()
                .HasKey(r => r.RegisterId);

            // RegisterAddress must be unique per DevicePort
            mb.Entity<Register>()
                .HasIndex(r => new { r.DevicePortId, r.RegisterAddress })
                .IsUnique();

            base.OnModelCreating(mb);
        }
    }
}
