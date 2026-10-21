using DevalCopilot.Api.HostedServices;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http.Headers;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>The real protected API host over an existing database file, with no hosted service started: only the status read is
/// exercised, through the production authentication, routing, mediator and response mapping.</summary>
internal sealed class LocalExecutionSequenceStatusHost : IDisposable
{
    private const string Secret = "test-harness-launch-session-secret-0123456789abcdef";

    private readonly WebApplicationFactory<Program> factory;

    public LocalExecutionSequenceStatusHost(string databasePath)
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(candidate => candidate.ServiceType == typeof(IHostedService)
                             && candidate.ImplementationType?.Namespace?.StartsWith("DevalCopilot.Api.HostedServices", StringComparison.Ordinal) == true).ToArray())
                {
                    services.Remove(descriptor);
                }
            });
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("LaunchSession:Secret", Secret),
                new KeyValuePair<string, string?>("ConnectionStrings:DevalCopilot", $"Data Source={databasePath}"),
            ]));
        });
    }

    public HttpClient CreateClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Secret);
        return client;
    }

    public void Dispose() => factory.Dispose();
}
