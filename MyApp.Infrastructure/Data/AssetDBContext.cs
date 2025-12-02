using Microsoft.EntityFrameworkCore;
using MyApp.Domain.Entities;

namespace MyApp.Infrastructure.Data
{


    public class AssetDbContextForDevice : DbContext
    {
        public AssetDbContextForDevice(DbContextOptions<AssetDbContextForDevice> options) : base(options) { }

        public DbSet<AssetSignalDeviceMapping> MappingTable { get; set; }
    }


}
