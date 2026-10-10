using System.Net;
using System.Net.Sockets;
using DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The production Program composition on real Kestrel, started in this process: normal authentication, migrations, startup
/// reconciliation, capability discovery, every supervisor, and the real process executor and provider adapters. Only the storage
/// registrations (the owned workspace and artifact roots), the SQLite file and the launch secret differ, exactly as in the browser
/// journey host. No provider, discovery, eligibility, process or supervisor service is replaced, and nothing here touches a
/// provider executable.
/// </summary>
public sealed class ProductionHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _database;
    private readonly string? _previousUrls;
    private bool _disposed;

    public ProductionHost(OwnedRootGuard root, Uri api, string launchSecret)
    {
        _database = root.Database;
        _previousUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        // Program reads the listen address while it builds, before a factory configuration override is merged.
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", api.GetLeftPart(UriPartial.Authority));
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("ManualQualification");
            builder.UseContentRoot(AppContext.BaseDirectory);
            // The host would print its content root and request paths: nothing it logs may reach this launcher output.
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("ConnectionStrings:DevalCopilot", $"Data Source={root.Database}"),
                new KeyValuePair<string, string?>("LaunchSession:Secret", launchSecret),
                new KeyValuePair<string, string?>("Urls", api.GetLeftPart(UriPartial.Authority)),
            ]));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IWorkspaceRootPathProvider>();
                services.AddSingleton<IWorkspaceRootPathProvider>(new WorkspaceRootPathProvider(root.Workspaces));
                var artifacts = new FilesystemArtifactStore(root.Artifacts);
                services.RemoveAll<IArtifactStore>();
                services.RemoveAll<IVerificationOutputArtifactStore>();
                services.AddSingleton<IArtifactStore>(artifacts);
                services.AddSingleton<IVerificationOutputArtifactStore>(artifacts);
            });
        });
        _factory.UseKestrel(api.Port);
    }

    public IServiceProvider Services => _factory.Services;

    public static Uri ReserveLoopbackAddress()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
    }

    public void Start()
    {
        _factory.StartServer();
        if (Services.GetServices<ILoggerProvider>().Any())
        {
            throw new InvalidOperationException("The host must run without a log provider.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await _factory.DisposeAsync();
        }
        finally
        {
            SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_database}"));
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", _previousUrls);
        }
    }
}
