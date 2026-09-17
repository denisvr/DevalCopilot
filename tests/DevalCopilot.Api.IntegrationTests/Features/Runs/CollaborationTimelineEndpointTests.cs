using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevalCopilot.Api.Features.Runs.GetCollaborationTimeline;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

public sealed class CollaborationTimelineEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task GetCollaborationTimeline_requires_authentication()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/runs/{Guid.NewGuid()}/collaboration-timeline");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCollaborationTimeline_returns_a_safe_not_found_response_for_an_unknown_run()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);

        var response = await client.GetAsync($"/api/runs/{Guid.NewGuid()}/collaboration-timeline");
        var rawPayload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("runs.not_found", rawPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("protocolVersion", rawPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("structuredContent", rawPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", rawPayload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCollaborationTimeline_returns_only_bounded_safe_protocol_envelopes()
    {
        var runId = await SeedMessageAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);

        var response = await client.GetAsync($"/api/runs/{runId}/collaboration-timeline");
        response.EnsureSuccessStatusCode();
        var rawPayload = await response.Content.ReadAsStringAsync();
        var payload = JsonSerializer.Deserialize<List<CollaborationMessageTimelineResponse>>(
            rawPayload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var message = Assert.Single(payload!);
        Assert.Equal("Codex", message.Actor);
        Assert.Equal("Claude", message.Recipient);
        Assert.Equal("Proposal", message.Type);
        Assert.Equal("Simulated", message.Provenance);
        Assert.DoesNotContain("resolvedExecutablePath", rawPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawOutput", rawPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", rawPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("environment", rawPayload, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> SeedMessageAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Ledger API", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Read timeline", now);
        dbContext.AddRange(project, run);
        dbContext.CollaborationMessages.Add(CollaborationMessage.Record(
            Guid.NewGuid(),
            run.Id,
            null,
            CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Codex,
            ParticipantKind.Claude,
            CollaborationMessageType.Proposal,
            null,
            "Expose a bounded protocol card.",
            "{\"scope\":\"Timeline\",\"assumptions\":\"Authenticated host\",\"verification\":\"MVC test\",\"risks\":\"Sensitive fields\"}",
            CollaborationMessageProvenance.Simulated,
            now));
        await dbContext.SaveChangesAsync();
        return run.Id;
    }
}
