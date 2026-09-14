using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevalCopilot.Api.Features.Projects.GetProjectRunSummaries;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects;

/// <summary>
/// A truthful product must start with zero registered projects — never a hardcoded fake one.
/// Uses its own dedicated factory instance (not the class-shared one other Projects tests use)
/// so nothing else's registration can have made this assertion accidentally pass.
/// </summary>
public sealed class NoProductionFixtureSeedingTests
{
    [Fact]
    public async Task A_freshly_started_host_registers_no_projects_on_its_own()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);

        var summaries = await client.GetFromJsonAsync<List<ProjectRunSummaryResponse>>("/api/projects/run-summaries");

        Assert.Empty(summaries!);
    }
}
