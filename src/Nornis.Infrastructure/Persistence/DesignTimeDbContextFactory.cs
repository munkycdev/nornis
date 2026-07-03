using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Nornis.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for generating and applying EF Core migrations.
/// Reads the connection string from the API project's appsettings.json.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NornisDbContext>
{
    public NornisDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "..", "Nornis.Api"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Server=.;Database=Nornis;Trusted_Connection=True;TrustServerCertificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<NornisDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new NornisDbContext(optionsBuilder.Options);
    }
}
