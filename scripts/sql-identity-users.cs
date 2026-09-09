#:package Microsoft.Data.SqlClient@6.1.6

// Creates the contained database users through which the container apps reach nornis-db as
// their managed identities, and grants them read and write. The one provisioning step
// scripts/provision-azure.ps1 cannot do itself: it needs the SQL server's Entra admin.
//
// Run from the repo root, as the Entra admin's az login:
//
//   dotnet run scripts/sql-identity-users.cs -- ca-nornis-api=<appId> ca-nornis-worker=<appId>
//
// where <appId> is the identity's application (client) id — `az ad sp show --id <principalId>
// --query appId -o tsv`. The server and database default to the live ones; override with
// SQL_SERVER and SQL_DATABASE. The token comes from `az account get-access-token --resource
// https://database.windows.net/`, minted here, so nothing is typed and nothing is stored.
//
// Idempotent: existing users are left alone and role membership is re-asserted. The SID form
// (WITH SID = <appId as binary>, TYPE = E) is used rather than FROM EXTERNAL PROVIDER because
// the latter needs the SQL server's own identity to read the directory, and this server has
// none; SQL compares that SID against the identity's token, so the application id is the
// whole of what it needs.

using System.Diagnostics;
using Microsoft.Data.SqlClient;

var server = Environment.GetEnvironmentVariable("SQL_SERVER") ?? "sql-chronicis-dev.database.windows.net";
var database = Environment.GetEnvironmentVariable("SQL_DATABASE") ?? "nornis-db";

var users = args
    .Select(a => a.Split('=', 2))
    .Where(p => p.Length == 2 && Guid.TryParse(p[1], out _))
    .Select(p => (Name: p[0], AppId: Guid.Parse(p[1])))
    .ToList();

if (users.Count == 0)
{
    Console.Error.WriteLine("usage: dotnet run scripts/sql-identity-users.cs -- <user-name>=<appId> [...]");
    return 2;
}

var token = await AzToken();

await using var connection = new SqlConnection(
    $"Server=tcp:{server},1433;Initial Catalog={database};Encrypt=True;Connect Timeout=60;")
{
    AccessToken = token
};
await connection.OpenAsync();

await using (var who = new SqlCommand("SELECT SUSER_SNAME(), DB_NAME()", connection))
await using (var reader = await who.ExecuteReaderAsync())
{
    await reader.ReadAsync();
    Console.WriteLine($"connected as {reader.GetString(0)} to {reader.GetString(1)}");
}

foreach (var (name, appId) in users)
{
    if (name.Contains(']') || name.Contains('\''))
    {
        Console.Error.WriteLine($"refusing user name '{name}'");
        return 2;
    }

    var sql = $"""
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'{name}')
        BEGIN
            DECLARE @sid varbinary(16) = CONVERT(varbinary(16), CAST('{appId}' AS uniqueidentifier));
            DECLARE @stmt nvarchar(max) = N'CREATE USER [{name}] WITH SID = ' + CONVERT(nvarchar(64), @sid, 1) + N', TYPE = E;';
            EXEC sp_executesql @stmt;
        END
        IF NOT EXISTS (SELECT 1 FROM sys.database_role_members rm
                       JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
                       JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
                       WHERE r.name = 'db_datareader' AND m.name = N'{name}')
            ALTER ROLE db_datareader ADD MEMBER [{name}];
        IF NOT EXISTS (SELECT 1 FROM sys.database_role_members rm
                       JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
                       JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
                       WHERE r.name = 'db_datawriter' AND m.name = N'{name}')
            ALTER ROLE db_datawriter ADD MEMBER [{name}];
        """;
    await using var command = new SqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
    Console.WriteLine($"ensured user {name} (db_datareader, db_datawriter)");
}

await using (var list = new SqlCommand(
    "SELECT name, type_desc FROM sys.database_principals WHERE type = 'E' ORDER BY name", connection))
await using (var reader = await list.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"  {reader.GetString(0),-20} {reader.GetString(1)}");
    }
}

return 0;

static async Task<string> AzToken()
{
    var psi = new ProcessStartInfo("az", "account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv")
    {
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };
    // `az` is a .cmd shim on Windows; ProcessStartInfo resolves it through PATHEXT only when
    // UseShellExecute is false and the name is bare, which this is.
    if (OperatingSystem.IsWindows())
    {
        psi.FileName = "cmd";
        psi.Arguments = "/c az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv";
    }

    using var process = Process.Start(psi) ?? throw new InvalidOperationException("could not start az");
    var output = (await process.StandardOutput.ReadToEndAsync()).Trim();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0 || output.Length == 0)
    {
        throw new InvalidOperationException("az account get-access-token failed — is az logged in as the SQL Entra admin?");
    }

    return output;
}
