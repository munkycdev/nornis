using Microsoft.Data.SqlClient;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// Production signs in to SQL as the process's own identity: its connection string says
/// <c>Authentication=Active Directory Default</c> (scripts/provision-azure.ps1). Local runs and
/// every other test use a SQL login, so nothing else notices when that sign-in is missing.
///
/// It went missing once (3.34). EF Core 11 brought Microsoft.Data.SqlClient 7, which moved
/// Entra sign-in out of the driver and into Microsoft.Data.SqlClient.Extensions.Azure. Without
/// that package every connection in production failed, /health answered 503, and the first
/// .NET 11 revision of the API never became ready (3.38).
/// </summary>
[TestFixture]
public class SqlEntraSignInTests
{
    [Test]
    public void AProcessBuiltOnInfrastructure_CanSignInToSqlTheWayProductionDoes()
    {
        var provider = SqlAuthenticationProvider.GetProvider(SqlAuthenticationMethod.ActiveDirectoryDefault);

        Assert.That(provider, Is.Not.Null,
            "No provider for 'Active Directory Default'. Nornis.Infrastructure must reference "
            + "Microsoft.Data.SqlClient.Extensions.Azure, which is where SqlClient 7 keeps Entra sign-in.");
    }
}
