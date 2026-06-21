using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nornis.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for generating EF Core migrations.
/// Uses a dummy connection string — no real database connection is needed.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NornisDbContext>
{
    public NornisDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NornisDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=.;Database=Nornis;Trusted_Connection=True;TrustServerCertificate=True");

        return new NornisDbContext(optionsBuilder.Options);
    }
}
