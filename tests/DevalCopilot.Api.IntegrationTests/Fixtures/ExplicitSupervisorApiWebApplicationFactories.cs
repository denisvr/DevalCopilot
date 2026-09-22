using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Features.EnvironmentReadiness.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevalCopilot.Api.IntegrationTests.Fixtures;

/// <summary>Runs only the real host-capability supervisor, with deterministic discovery results.
/// No provider executable is started by this endpoint-test composition.</summary>
public sealed class HostCapabilityReadinessApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IToolDiscoveryAdapter>();
            services.AddSingleton<IToolDiscoveryAdapter, DeterministicToolDiscoveryAdapter>();
            services.AddHostedService<HostCapabilityReadinessSupervisor>();
        });
    }

    private sealed class DeterministicToolDiscoveryAdapter : IToolDiscoveryAdapter
    {
        public Task<ToolDiscoveryResult> DiscoverAsync(Capability capability, CancellationToken cancellationToken) =>
            Task.FromResult(capability == Capability.Git
                ? ToolDiscoveryResult.DirectExecutableSuccess(@"C:\test-tools\git.exe", "test-git-version")
                : ToolDiscoveryResult.Failed(CapabilityProbeReason.ExecutableNotFound));
    }
}

/// <summary>Runs only the deterministic simulated-run worker for API-flow tests.</summary>
public sealed class SimulatedRunApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddHostedService<SimulatedRunSupervisor>());
    }
}
