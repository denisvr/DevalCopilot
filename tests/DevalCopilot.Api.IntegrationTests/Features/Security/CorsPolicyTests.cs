using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.Security.Cors;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Security;

/// <summary>
/// Exercises the real CORS policy through actual spawned processes and real HTTP requests
/// — the same failure this proves fixed (the packaged Tauri WebView's cross-origin request
/// to the loopback API) only ever showed up against a real process, never through
/// <c>WebApplicationFactory</c>'s in-process test server.
/// </summary>
public sealed class CorsPolicyTests : IDisposable
{
    private static readonly string ValidSecret = string.Concat(Enumerable.Repeat("0123456789abcdef", 4));
    private static readonly string ApiDllPath = typeof(Program).Assembly.Location;
    private const string UnknownOrigin = "http://evil.example";

    private readonly List<Process> _processes = [];
    private readonly List<string> _databasePaths = [];

    [Fact]
    public async Task Packaged_tauri_origin_receives_the_expected_cors_headers_with_credentials()
    {
        var (client, port) = await StartBootstrapSidecarAsync();

        var (status, headers) = await SendPreflightAsync(client, port, TrustedFrontendOrigins.PackagedWindowsWebView);

        Assert.Equal(HttpStatusCode.NoContent, status);
        Assert.Equal(TrustedFrontendOrigins.PackagedWindowsWebView, headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", headers.GetValues("Access-Control-Allow-Credentials").Single());
    }

    [Fact]
    public async Task Tauri_dev_origin_is_allowed_in_the_bootstrap_stdin_composition()
    {
        var (client, port) = await StartBootstrapSidecarAsync();

        var (status, headers) = await SendPreflightAsync(client, port, TrustedFrontendOrigins.TauriDevelopmentWebView);

        Assert.Equal(HttpStatusCode.NoContent, status);
        Assert.Equal(TrustedFrontendOrigins.TauriDevelopmentWebView, headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Tauri_origins_are_not_allowed_in_the_browser_hosted_playwright_composition()
    {
        var (client, port) = await StartBrowserHostedSidecarAsync("http://127.0.0.1:5173");

        var response = await SendGetWithOriginAsync(client, port, "/health", TrustedFrontendOrigins.PackagedWindowsWebView);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task The_configured_browser_hosted_playwright_origin_remains_supported()
    {
        const string playwrightOrigin = "http://127.0.0.1:5173";
        var (client, port) = await StartBrowserHostedSidecarAsync(playwrightOrigin);

        var response = await SendGetWithOriginAsync(client, port, "/health", playwrightOrigin);

        Assert.Equal(playwrightOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task An_unknown_origin_receives_no_permissive_cors_response_in_either_composition()
    {
        var (bootstrapClient, bootstrapPort) = await StartBootstrapSidecarAsync();
        var bootstrapResponse = await SendGetWithOriginAsync(bootstrapClient, bootstrapPort, "/health", UnknownOrigin);
        Assert.False(bootstrapResponse.Headers.Contains("Access-Control-Allow-Origin"));

        var (browserClient, browserPort) = await StartBrowserHostedSidecarAsync("http://127.0.0.1:5173");
        var browserResponse = await SendGetWithOriginAsync(browserClient, browserPort, "/health", UnknownOrigin);
        Assert.False(browserResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Allowed_origin_without_a_credential_still_returns_401()
    {
        var (client, port) = await StartBootstrapSidecarAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/projects/run-summaries");
        request.Headers.Add("Origin", TrustedFrontendOrigins.PackagedWindowsWebView);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Allowed_origin_with_an_incorrect_credential_still_returns_401()
    {
        var (client, port) = await StartBootstrapSidecarAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/projects/run-summaries");
        request.Headers.Add("Origin", TrustedFrontendOrigins.PackagedWindowsWebView);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-right-secret");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Allowed_origin_with_the_correct_credential_succeeds()
    {
        var (client, port) = await StartBootstrapSidecarAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/projects/run-summaries");
        request.Headers.Add("Origin", TrustedFrontendOrigins.PackagedWindowsWebView);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ValidSecret);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TrustedFrontendOrigins.PackagedWindowsWebView, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Access_token_query_string_authentication_remains_rejected_from_the_packaged_origin()
    {
        var (client, port) = await StartBootstrapSidecarAsync();

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"http://127.0.0.1:{port}/api/projects/run-summaries?access_token={ValidSecret}");
        request.Headers.Add("Origin", TrustedFrontendOrigins.PackagedWindowsWebView);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SignalR_negotiate_succeeds_from_the_packaged_origin_with_credentials()
    {
        var (client, port) = await StartBootstrapSidecarAsync();

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"http://127.0.0.1:{port}/hubs/run/negotiate?negotiateVersion=1");
        request.Headers.Add("Origin", TrustedFrontendOrigins.PackagedWindowsWebView);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ValidSecret);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TrustedFrontendOrigins.PackagedWindowsWebView, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    private async Task<(HttpClient Client, int Port)> StartBootstrapSidecarAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-cors-{Guid.NewGuid():N}.db");
        _databasePaths.Add(databasePath);
        await MigrateDisposableDatabaseAsync(databasePath);

        var startInfo = new ProcessStartInfo("dotnet", $"\"{ApiDllPath}\" --bootstrap-stdin")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "SideEffectFreeIntegrationTest";
        startInfo.Environment["ConnectionStrings__DevalCopilot"] = $"Data Source={databasePath}";

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the sidecar.");
        _processes.Add(process);

        await process.StandardInput.WriteAsync($$"""{"version":1,"secret":"{{ValidSecret}}"}""" + "\n");
        await process.StandardInput.FlushAsync();

        var readyLine = await CaptureUntilReadyAsync(process).WaitAsync(TimeSpan.FromSeconds(30));
        return (new HttpClient(), ParsePort(readyLine));
    }

    private async Task<(HttpClient Client, int Port)> StartBrowserHostedSidecarAsync(string allowedOrigin)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-cors-browser-{Guid.NewGuid():N}.db");
        _databasePaths.Add(databasePath);
        await MigrateDisposableDatabaseAsync(databasePath);

        var startInfo = new ProcessStartInfo("dotnet", $"\"{ApiDllPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "SideEffectFreeIntegrationTest";
        startInfo.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        startInfo.Environment["ConnectionStrings__DevalCopilot"] = $"Data Source={databasePath}";
        startInfo.Environment["Cors__AllowedOrigin"] = allowedOrigin;
        startInfo.Environment["LaunchSession__Secret"] = ValidSecret;

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the sidecar.");
        _processes.Add(process);

        var port = await CaptureListeningPortAsync(process).WaitAsync(TimeSpan.FromSeconds(30));
        return (new HttpClient(), port);
    }

    private static async Task MigrateDisposableDatabaseAsync(string databasePath)
    {
        var options = new DbContextOptionsBuilder<DevalCopilotDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        await using var dbContext = new DevalCopilotDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    private static async Task<(HttpStatusCode Status, HttpResponseHeaders Headers)> SendPreflightAsync(
        HttpClient client, int port, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, $"http://127.0.0.1:{port}/api/projects/run-summaries");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await client.SendAsync(request);
        return (response.StatusCode, response.Headers);
    }

    private static async Task<HttpResponseMessage> SendGetWithOriginAsync(HttpClient client, int port, string path, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}{path}");
        request.Headers.Add("Origin", origin);
        return await client.SendAsync(request);
    }

    private static async Task<string> CaptureUntilReadyAsync(Process process)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line is null)
            {
                throw new InvalidOperationException("The sidecar's stdout ended before a readiness line was seen.");
            }

            if (line.StartsWith("DEVALCOPILOT_SIDECAR_READY ", StringComparison.Ordinal))
            {
                return line;
            }
        }
    }

    private static async Task<int> CaptureListeningPortAsync(Process process)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line is null)
            {
                throw new InvalidOperationException("The host's stdout ended before it reported a listening address.");
            }

            // "Now listening on: http://127.0.0.1:12345"
            var marker = "Now listening on: ";
            var index = line.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var url = line[(index + marker.Length)..].Trim();
            return new Uri(url).Port;
        }
    }

    private static int ParsePort(string readyLine)
    {
        var json = readyLine["DEVALCOPILOT_SIDECAR_READY ".Length..];
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("port").GetInt32();
    }

    public void Dispose()
    {
        foreach (var process in _processes)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(TimeSpan.FromSeconds(5));
            }

            process.Dispose();
        }

        foreach (var path in _databasePaths)
        {
            // Scoped to this fixture's own connection strings only — SqliteConnection.ClearAllPools()
            // is a process-wide operation that can invalidate another, unrelated test class's
            // still-in-flight connection under xUnit's default cross-class parallelism.
            SqliteConnection.ClearPool(new SqliteConnection($"Data Source={path}"));
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup: a momentarily lingering OS-level file handle after
                // process exit must not fail the test that already passed its assertions.
            }
        }
    }
}
