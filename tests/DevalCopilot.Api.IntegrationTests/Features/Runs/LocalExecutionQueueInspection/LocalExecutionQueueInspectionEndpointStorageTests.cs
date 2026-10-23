using System.Net;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionAdmissionHistory;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionQueueInspection;

/// <summary>The Unavailable wire state of ADR-0064 over queued cycles whose storage this class damages (so every test owns its factory
/// and database): an explicit 200 with an empty entry list, never a partial list, a corrupt value echoed or a refusal status, and the
/// healthy cycle beside the damaged one is not shown either.</summary>
public sealed class LocalExecutionQueueInspectionEndpointStorageTests : LocalExecutionAdmissionHistoryApiTestBase, IAsyncLifetime
{
    private const string Route = "/api/local-execution-queue/inspection";

    private readonly ApiWebApplicationFactory own;

    public LocalExecutionQueueInspectionEndpointStorageTests() : this(new ApiWebApplicationFactory())
    {
    }

    private LocalExecutionQueueInspectionEndpointStorageTests(ApiWebApplicationFactory own) : base(own)
    {
        this.own = own;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await own.DisposeAsync();
    }

    private static void AssertUnavailable(HttpStatusCode status, JsonElement body, string text)
    {
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["entries", "status"], Names(body));
        Assert.Equal("Unavailable", body.GetProperty("status").GetString());
        Assert.Empty(body.GetProperty("entries").EnumerateArray());
        Assert.DoesNotContain("Bogus", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_damaged_admission_state_is_an_explicit_unavailable_that_also_hides_the_healthy_cycle()
    {
        await QueuedAsync();
        await QueuedAsync();
        Assert.Equal("Recorded", (await GetAsync(Route)).Body.GetProperty("status").GetString());
        await CorruptAsync(
            "DROP TRIGGER IF EXISTS trg_local_execution_admission_immutable; "
            + "UPDATE local_execution_admissions SET State = 'Bogus' WHERE AdmissionNumber = (SELECT MIN(AdmissionNumber) FROM local_execution_admissions);");

        var response = await GetAsync(Route);

        AssertUnavailable(response.Status, response.Body, response.Text);
    }

    [Fact]
    public async Task A_damaged_project_label_is_an_explicit_unavailable_with_nothing_disclosed()
    {
        await QueuedAsync();
        await CorruptAsync("UPDATE projects SET Name = '   ';");

        var response = await GetAsync(Route);

        AssertUnavailable(response.Status, response.Body, response.Text);
    }
}
