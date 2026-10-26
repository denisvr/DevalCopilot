using System.Net.Http.Headers;
using DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionInspection;
using DevalCopilot.Application.Features.Processes.Ports;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.PlanningSequenceInspection;

/// <summary>The real protected API host (production authentication, routing, filters, mediator and response mapping) over an existing
/// database file, with no hosted service started and the artifact store wrapped by a recorder that proves the inspection reads no sealed
/// artifact at all.</summary>
internal sealed class PlanningSequenceInspectionHost : IDisposable
{
    public const string Secret = "test-harness-launch-session-secret-0123456789abcdef";

    private readonly WebApplicationFactory<Program> factory;

    public PlanningSequenceInspectionHost(string databasePath, IArtifactStore artifactStore)
    {
        Store = new CountingArtifactStore(artifactStore);
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(candidate => candidate.ServiceType == typeof(IHostedService)
                             && candidate.ImplementationType?.Namespace?.StartsWith("DevalCopilot.Api.HostedServices", StringComparison.Ordinal) == true).ToArray())
                {
                    services.Remove(descriptor);
                }

                services.RemoveAll<IArtifactStore>();
                services.AddSingleton<IArtifactStore>(Store);
            });
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("LaunchSession:Secret", Secret),
                new KeyValuePair<string, string?>("ConnectionStrings:DevalCopilot", $"Data Source={databasePath}"),
            ]));
        });
    }

    public CountingArtifactStore Store { get; }

    public IServiceProvider Services => factory.Services;

    public HttpClient CreateClient(bool authenticated = true)
    {
        var client = factory.CreateClient();
        if (authenticated)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Secret);
        }

        return client;
    }

    public void Dispose() => factory.Dispose();
}
