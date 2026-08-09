using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Nornis.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for generating and applying EF Core migrations.
///
/// Resolves the connection string the same way the API host does, and in the same order, so
/// <c>dotnet ef database update</c> targets whatever the app would target. That matters
/// because the tracked appsettings ship an <em>empty</em> DefaultConnection on purpose — the
/// real one lives in the API project's user-secrets store — and a factory reading only those
/// files finds "" and hands EF an uninitialized connection.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NornisDbContext>
{
    /// <summary>
    /// The API project's user-secrets id. Duplicated from <c>Nornis.Api.csproj</c> because
    /// this assembly is not the one that owns the store and no compiler spans the two; the
    /// secret must be the API's, so that migrations and the running app read one value.
    /// </summary>
    private const string ApiUserSecretsId = "4ebe1683-4c09-44dd-a16c-d5e94091179d";

    private const string LocalFallback =
        "Server=.;Database=Nornis;Trusted_Connection=True;TrustServerCertificate=True";

    public NornisDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "..", "Nornis.Api"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets(ApiUserSecretsId)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // Empty and absent both mean "nothing configured here". Treating only null that way
        // is what made the fallback below unreachable once the tracked appsettings started
        // shipping "" — and an empty string reaches EF as an uninitialized connection rather
        // than as a missing one, so the error names the wrong problem.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = LocalFallback;
        }

        var optionsBuilder = new DbContextOptionsBuilder<NornisDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new NornisDbContext(optionsBuilder.Options);
    }
}
