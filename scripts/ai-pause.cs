#:package Microsoft.Data.SqlClient@6.1.6

// Pause or resume every paid AI call across Nornis, by flipping the 'ai-paused' row in
// OperationalFlags. scripts/ai-pause.ps1 is the documented front door and forwards here;
// docs/runbooks/ai-paused.md is the companion runbook.
//
//   dotnet run scripts/ai-pause.cs -- status
//   dotnet run scripts/ai-pause.cs -- pause "Azure OpenAI incident, tracking DPS-1234"
//   dotnet run scripts/ai-pause.cs -- resume
//
// Reaches the database as you: the token comes from `az account get-access-token --resource
// https://database.windows.net/`, minted here, so nothing is typed and nothing is stored.
// Since O3 (2026-09-08) the apps reach SQL as their managed identities and this script does
// the same as yours — you need a contained user in nornis-db, or to be the server's Entra
// admin (David is). Server and database default to the live ones; override with SQL_SERVER
// and SQL_DATABASE, the same knobs sql-identity-users.cs honours.
//
// A reason is required to pause. It is shown to users when an interactive path refuses, and
// a pause nobody can explain is indistinguishable from a fault.

using System.Diagnostics;
using Microsoft.Data.SqlClient;

var server = Environment.GetEnvironmentVariable("SQL_SERVER") ?? "sql-chronicis-dev.database.windows.net";
var database = Environment.GetEnvironmentVariable("SQL_DATABASE") ?? "nornis-db";

var action = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
var reason = args.Length > 1 ? args[1] : null;

if (action is not ("status" or "pause" or "resume"))
{
    Console.Error.WriteLine("usage: dotnet run scripts/ai-pause.cs -- status | pause <reason> | resume");
    return 2;
}

if (action == "pause" && string.IsNullOrWhiteSpace(reason))
{
    Console.Error.WriteLine("Pausing needs a reason. It is shown to users, and a pause nobody can explain is indistinguishable from a fault.");
    return 2;
}

var token = await AzToken();

await using var connection = new SqlConnection(
    $"Server=tcp:{server},1433;Initial Catalog={database};Encrypt=True;Connect Timeout=60;")
{
    AccessToken = token
};
await connection.OpenAsync();

// The row is upserted rather than inserted: one row per flag is the invariant, and a second
// row for 'ai-paused' would be a bug rather than a record.
switch (action)
{
    case "pause":
    {
        await using var command = new SqlCommand("""
            MERGE OperationalFlags AS target
            USING (SELECT 'ai-paused' AS Name) AS source ON target.Name = source.Name
            WHEN MATCHED THEN UPDATE SET Enabled = 1, Reason = @reason, UpdatedAt = SYSDATETIMEOFFSET()
            WHEN NOT MATCHED THEN INSERT (Name, Enabled, Reason, UpdatedAt)
                VALUES ('ai-paused', 1, @reason, SYSDATETIMEOFFSET());
            """, connection);
        command.Parameters.AddWithValue("@reason", reason);
        await command.ExecuteNonQueryAsync();
        Console.WriteLine($"AI PAUSED: {reason}");
        Console.WriteLine("Effective within ~90s. Queued work waits in the queue; nothing is dead-lettered.");
        break;
    }
    case "resume":
    {
        await using var command = new SqlCommand("""
            UPDATE OperationalFlags SET Enabled = 0, Reason = NULL, UpdatedAt = SYSDATETIMEOFFSET()
            WHERE Name = 'ai-paused';
            """, connection);
        await command.ExecuteNonQueryAsync();
        Console.WriteLine("AI resumed. Workers restart consuming within ~90s.");
        break;
    }
    default:
    {
        await using var command = new SqlCommand("""
            SELECT CASE WHEN Enabled = 1 THEN 'PAUSED: ' + ISNULL(Reason, '(no reason recorded)')
                        ELSE 'running' END + '  (updated ' + CONVERT(varchar, UpdatedAt, 120) + ')'
            FROM OperationalFlags WHERE Name = 'ai-paused';
            """, connection);
        if (await command.ExecuteScalarAsync() is string line)
        {
            Console.WriteLine(line);
        }
        Console.WriteLine("(no row means running — the flag has never been set)");
        break;
    }
}

return 0;

// Mirrors AzToken in sql-identity-users.cs. File-based programs cannot share source, so the
// two copies are kept the same by hand.
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
        throw new InvalidOperationException("az account get-access-token failed — is az logged in as someone with a user in nornis-db?");
    }

    return output;
}
