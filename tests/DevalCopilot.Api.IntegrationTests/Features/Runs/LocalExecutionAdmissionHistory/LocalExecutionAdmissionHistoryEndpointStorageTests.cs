using System.Net;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionAdmissionHistory;

/// <summary>The Unavailable wire state of ADR-0063 over a queued cycle whose storage this class damages (so it owns its factory and
/// database): an explicit 200 with a null header and an empty list, never a partial ledger, a corrupt value echoed or a refusal status.</summary>
public sealed class LocalExecutionAdmissionHistoryEndpointStorageTests(ApiWebApplicationFactory baseFactory)
    : LocalExecutionAdmissionHistoryApiTestBase(baseFactory), IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task A_damaged_ledger_is_an_explicit_unavailable_with_nothing_disclosed()
    {
        var (scene, _) = await QueuedAsync();
        await CorruptAsync(
            "DROP TRIGGER IF EXISTS trg_local_execution_admission_immutable; "
            + "UPDATE local_execution_admissions SET Kind = 'Bogus';");

        var response = await GetAsync(Admissions(scene.RunId));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(["admissions", "runId", "sequence", "status"], Names(response.Body));
        Assert.Equal("Unavailable", response.Body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("sequence").ValueKind);
        Assert.Empty(response.Body.GetProperty("admissions").EnumerateArray());
        Assert.DoesNotContain("Bogus", response.Text, StringComparison.Ordinal);
    }
}
