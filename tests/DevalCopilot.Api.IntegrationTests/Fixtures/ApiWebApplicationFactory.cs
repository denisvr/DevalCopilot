using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.EnvironmentReadiness.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DevalCopilot.Api.IntegrationTests.Fixtures;

/// <summary>
/// Boots the real Api host against a disposable file-backed SQLite database and a
/// known launch-session secret supplied entirely through in-memory configuration —
/// never through argv, an environment variable, or a file the production bootstrap
/// would use.
/// </summary>
public class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ValidSecret = "test-harness-launch-session-secret-0123456789abcdef";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-api-tests-{Guid.NewGuid():N}.db");
    private readonly string _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-api-tests-artifacts-{Guid.NewGuid():N}");
    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-api-tests-workspaces-{Guid.NewGuid():N}");
    public ThrowOnUseToolDiscoveryAdapter ToolDiscoveryAdapter { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("LaunchSession:Secret", ValidSecret),
                new KeyValuePair<string, string?>("ConnectionStrings:DevalCopilot", $"Data Source={_databasePath}"),
                new KeyValuePair<string, string?>("Logging:EventLog:LogLevel:Default", "None"),
            ]);
        });

        builder.ConfigureTestServices(services =>
        {
            foreach (var hostedService in services
                         .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                             && descriptor.ImplementationType?.Namespace?.StartsWith(
                                 "DevalCopilot.Api.HostedServices", StringComparison.Ordinal) == true)
                         .ToArray())
            {
                services.Remove(hostedService);
            }

            services.RemoveAll<IToolDiscoveryAdapter>();
            services.AddSingleton<IToolDiscoveryAdapter>(ToolDiscoveryAdapter);

            services.RemoveAll<IArtifactStore>();
            services.RemoveAll<IVerificationOutputArtifactStore>();
            services.RemoveAll<IWorkspaceRootPathProvider>();
            var artifactStore = new FilesystemArtifactStore(_artifactRoot);
            services.AddSingleton<IArtifactStore>(artifactStore);
            services.AddSingleton<IVerificationOutputArtifactStore>(artifactStore);
            services.AddSingleton<IWorkspaceRootPathProvider>(new WorkspaceRootPathProvider(_workspaceRoot));
        });
    }

    public sealed class ThrowOnUseToolDiscoveryAdapter : IToolDiscoveryAdapter
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<ToolDiscoveryResult> DiscoverAsync(Capability capability, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            throw new InvalidOperationException("API integration-test composition must not probe provider executables.");
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        if (Directory.Exists(_artifactRoot))
        {
            Directory.Delete(_artifactRoot, recursive: true);
        }

        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }
}
