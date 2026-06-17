using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WorldMonitor.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WorldMonitorDbContext>
{
    public WorldMonitorDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("WORLDMONITOR_DB")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=WorldMonitorDesign;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";
        var options = new DbContextOptionsBuilder<WorldMonitorDbContext>().UseSqlServer(cs).Options;
        return new WorldMonitorDbContext(options);
    }
}
