using Microsoft.EntityFrameworkCore;
using MyApp.Domain.Entities;


namespace MyApp.Infrastructure.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> opts) : base(opts) { }

        public DbSet<Device> Devices => Set<Device>();
        public DbSet<DeviceConfiguration> DeviceConfigurations => Set<DeviceConfiguration>();
        public DbSet<DeviceSlave> DeviceSlaves => Set<DeviceSlave>();
        public DbSet<Register> Registers => Set<Register>();

        public DbSet<ApiLog> ApiLogs => Set<ApiLog>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            // DEVICE
            mb.Entity<Device>()
              .HasKey(d => d.DeviceId);

            // Device → DeviceSlaveSets (keep for other functionality)
          

            // Device → DeviceSlaves
            mb.Entity<Device>()
              .HasMany(d => d.DeviceSlave)
              .WithOne(dp => dp.Device)
              .HasForeignKey(dp => dp.DeviceId)
              .OnDelete(DeleteBehavior.Cascade);

            // DEVICE PORT
            mb.Entity<DeviceSlave>()
                .HasKey(dp => dp.deviceSlaveId);

            // slaveIndex must be unique per device
            mb.Entity<DeviceSlave>()
                .HasIndex(dp => new { dp.DeviceId, dp.slaveIndex })
                .IsUnique();

            // DeviceSlave → Registers
            mb.Entity<DeviceSlave>()
                .HasMany(dp => dp.Registers)
                .WithOne(r => r.DeviceSlave)
                .HasForeignKey(r => r.deviceSlaveId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            // REGISTER
            mb.Entity<Register>()
                .HasKey(r => r.RegisterId);

            // RegisterAddress must be unique per DeviceSlave
            mb.Entity<Register>()
                .HasIndex(r => new { r.deviceSlaveId, r.RegisterAddress })
                .IsUnique();

            base.OnModelCreating(mb);
        }
    }
}
