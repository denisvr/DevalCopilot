using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace DevalCopilot.Api.IntegrationTests.Fixtures;

/// <summary>
/// Boots the real Api host against a disposable file-backed SQLite database and a
/// known launch-session secret supplied entirely through in-memory configuration —
/// never through argv, an environment variable, or a file the production bootstrap
/// would use.
/// </summary>
public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ValidSecret = "test-harness-launch-session-secret-0123456789abcdef";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-api-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("LaunchSession:Secret", ValidSecret),
                new KeyValuePair<string, string?>("ConnectionStrings:DevalCopilot", $"Data Source={_databasePath}"),
            ]);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
