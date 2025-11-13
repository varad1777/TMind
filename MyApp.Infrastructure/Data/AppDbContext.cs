using Microsoft.EntityFrameworkCore;
using MyApp.Domain.Entities;
using System;

namespace MyApp.Infrastructure.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> opts) : base(opts) { }

        public DbSet<Device> Devices => Set<Device>();
        public DbSet<DeviceConfiguration> DeviceConfigurations => Set<DeviceConfiguration>();
        public DbSet<DevicePortSet> DevicePortSets => Set<DevicePortSet>();
        public DbSet<DevicePort> DevicePorts => Set<DevicePort>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<Device>().HasKey(d => d.DeviceId);

            
            mb.Entity<Device>()
                .HasIndex(d => d.Name)
                .IsUnique();

           

            mb.Entity<DevicePort>()
                .HasIndex(p => new { p.DeviceId, p.PortIndex })
                .IsUnique(false); 

            base.OnModelCreating(mb);
        }
    }
}
