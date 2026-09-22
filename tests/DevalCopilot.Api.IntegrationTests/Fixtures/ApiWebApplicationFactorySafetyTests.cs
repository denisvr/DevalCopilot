using DevalCopilot.Api.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Fixtures;

public sealed class ApiWebApplicationFactorySafetyTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public void Ordinary_api_factory_removes_product_hosted_services_and_never_probes_provider_executables()
    {
        using var client = factory.CreateClient();

        var hostedServiceTypes = factory.Services.GetServices<IHostedService>()
            .Select(service => service.GetType())
            .ToArray();

        Assert.DoesNotContain(hostedServiceTypes, type =>
            type.Namespace?.StartsWith("DevalCopilot.Api.HostedServices", StringComparison.Ordinal) == true);
        Assert.Equal(0, factory.ToolDiscoveryAdapter.CallCount);
    }
}
