using DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;

/// <summary>
/// The browser journey's executable test host: the production Program composition on real Kestrel (normal authentication,
/// CORS, SignalR, migrations, reconciliation, capability discovery, and every supervisor), differing only in disposable
/// SQLite storage, an ephemeral launch secret, and three storage registrations that point at one verified disposable root.
/// Provider and verification executables are the owned fixture doubles found through this process's PATH.
/// </summary>
public static class JourneyHost
{
    public static async Task<int> Main()
    {
        var root = OwnedRootGuard.Verify(
            Environment.GetEnvironmentVariable("DEVALCOPILOT_E2E_ROOT"), Environment.GetEnvironmentVariable("DEVALCOPILOT_E2E_OWNER_TOKEN"));
        var secret = Environment.GetEnvironmentVariable("DEVALCOPILOT_TEST_LAUNCH_SECRET");
        var apiUrl = Environment.GetEnvironmentVariable("DEVALCOPILOT_JOURNEY_API_URL");
        var origin = Environment.GetEnvironmentVariable("DEVALCOPILOT_JOURNEY_ORIGIN");
        if (string.IsNullOrEmpty(secret) || !Uri.TryCreate(apiUrl, UriKind.Absolute, out var api) || string.IsNullOrEmpty(origin))
        {
            await Console.Error.WriteLineAsync("The journey host requires its launch secret, API address, and frontend origin.");
            return 2;
        }

        // The first PATH entry must be the owned bin directory, so discovery resolves the doubles and never an installed provider.
        var firstPathEntry = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator)[0];
        if (!string.Equals(Path.TrimEndingDirectorySeparator(firstPathEntry), Path.Combine(root.Root, "bin"), StringComparison.OrdinalIgnoreCase))
        {
            await Console.Error.WriteLineAsync("The owned bin directory is not first on the host PATH.");
            return 2;
        }

        root.CreateLayout();
        FixtureInstaller.Install(root, AppContext.BaseDirectory);

        // Program reads the CORS origin and the listen address while it builds, before a factory configuration override is merged, so
        // these two reach it as process environment values (the launch secret is read later and uses the factory configuration).
        Environment.SetEnvironmentVariable("Cors__AllowedOrigin", origin);
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", api.GetLeftPart(UriPartial.Authority));

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("BrowserJourney");
            builder.UseContentRoot(AppContext.BaseDirectory);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("ConnectionStrings:DevalCopilot", $"Data Source={root.Database}"),
                new KeyValuePair<string, string?>("LaunchSession:Secret", secret),
                new KeyValuePair<string, string?>("Cors:AllowedOrigin", origin),
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
        factory.UseKestrel(api.Port);
        factory.StartServer();

        using var shutdown = new CancellationTokenSource();
        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
        lifetime.ApplicationStopping.Register(shutdown.Cancel);

        _ = LaunchTargetVerifier.WriteVerdictAsync(factory.Services, root, shutdown.Token);
        Console.WriteLine("journey host listening");
        try
        {
            await Task.Delay(Timeout.Infinite, shutdown.Token);
        }
        catch (OperationCanceledException)
        {
            // Orderly shutdown requested.
        }

        return 0;
    }
}
