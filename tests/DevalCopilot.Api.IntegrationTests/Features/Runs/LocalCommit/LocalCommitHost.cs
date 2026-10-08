using DevalCopilot.Api.HostedServices;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.EnvironmentReadiness.Ports;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.Features.Runs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// The real Api host over a file-backed SQLite database and a real Git scene. Production composition, authentication, mediator,
/// transactions, real Git adapters and the real startup recovery are all in use; only unrelated hosted services are removed, and
/// the real <see cref="LocalCommitSupervisor"/> is kept when the test asks for hosted execution. Two hosts over the same database
/// file model a restart.
/// </summary>
internal sealed class LocalCommitHost(
    LocalCommitScene scene,
    string databasePath,
    bool runSupervisor,
    Func<ILocalCommitRepository, ILocalCommitRepository>? decorateRepository = null,
    Func<ILocalCommitPreparer, ILocalCommitPreparer>? decoratePreparer = null,
    Action<IServiceCollection>? configure = null)
    : WebApplicationFactory<Program>
{
    public const string Secret = ApiWebApplicationFactory.ValidSecret;

    /// <summary>The server errors this host logged, bounded and sanitized, for failure messages of unexpected statuses.</summary>
    public CapturedServerErrors ServerErrors { get; } = new(
        [Secret],
        [(scene.Root, "<scene>"), (databasePath, "<db>"), (Path.GetTempPath(), "<tmp>\\")]);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("LaunchSession:Secret", Secret),
                new KeyValuePair<string, string?>("ConnectionStrings:DevalCopilot", $"Data Source={databasePath}"),
                new KeyValuePair<string, string?>("Logging:EventLog:LogLevel:Default", "None"),
            ]);
        });

        builder.ConfigureTestServices(services =>
        {
            foreach (var hostedService in services
                         .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                             && descriptor.ImplementationType?.Namespace?.StartsWith(
                                 "DevalCopilot.Api.HostedServices", StringComparison.Ordinal) == true
                             && !(runSupervisor && descriptor.ImplementationType == typeof(LocalCommitSupervisor)))
                         .ToArray())
            {
                services.Remove(hostedService);
            }

            services.AddSingleton<ILoggerProvider>(ServerErrors);
            services.RemoveAll<IToolDiscoveryAdapter>();
            services.AddSingleton<IToolDiscoveryAdapter>(new ApiWebApplicationFactory.ThrowOnUseToolDiscoveryAdapter());
            services.RemoveAll<IArtifactStore>();
            services.RemoveAll<IVerificationOutputArtifactStore>();
            services.RemoveAll<IWorkspaceRootPathProvider>();
            var artifactStore = new FilesystemArtifactStore(Path.Combine(scene.Root, "artifacts"));
            services.AddSingleton<IArtifactStore>(artifactStore);
            services.AddSingleton<IVerificationOutputArtifactStore>(artifactStore);
            services.AddSingleton<IWorkspaceRootPathProvider>(new WorkspaceRootPathProvider(Path.Combine(scene.Root, "workspaces")));

            configure?.Invoke(services);

            if (decoratePreparer is not null)
            {
                services.RemoveAll<ILocalCommitPreparer>();
                services.AddSingleton<ILocalCommitPreparer>(
                    provider => decoratePreparer(provider.GetRequiredService<LocalCommitGit>()));
            }

            if (decorateRepository is not null)
            {
                services.RemoveAll<ILocalCommitRepository>();
                services.AddSingleton<ILocalCommitRepository>(
                    provider => decorateRepository(provider.GetRequiredService<LocalCommitGit>()));
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={databasePath}"));
    }
}
