using System.Net;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// The representative production-lineage proof of ADR-0047 (not the ADR-0046 clone): a fresh real Git delivery, the production command
/// chain and supervisor-driven explicit local commit, the protected observation-target POST and the protected identity-binding POST, and
/// then the two-phase inspection through the same production host, with only the provider-neutral observer scripted. It proves the
/// cloned class exercises the same lineage the production host produces.
/// </summary>
public sealed class GitHubCheckRunOutputObservationProductionLineageTests : GitHubCheckRunOutputObservationTestBase
{
    [Fact]
    public async Task The_complete_two_phase_inspection_runs_on_a_freshly_delivered_production_lineage_and_writes_nothing()
    {
        var bound = await BoundAsync();
        var remote = new GitHubCheckRunOutputRef(
            Scene.BranchName, bound.Sha);
        bound.Output.Metadata = () => Probe(remote, MetadataNode(bound.Sha));
        bound.Output.Output = () => Probe(remote, OutputNode(bound.Sha, Detail("lineage", "COMPLETED", "FAILURE", "T", "S", "X")));
        var before = await DatabaseFingerprintAsync();

        var reading = await ObserveOutputAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal(OutputResponseKeys, KeysOf(reading.Root));
        Assert.True(StatusOf(reading) == "OutputReported", reading.Body);
        AssertFacts(reading, source: true, target: true, binding: true, observation: true);
        var check = reading.Root.GetProperty("observation").GetProperty("checkRun");
        Assert.Equal(CheckRunKeys, KeysOf(check));
        Assert.Equal("lineage", check.GetProperty("name").GetString());
        Assert.Equal(bound.Sha, check.GetProperty("commitSha").GetString());
        Assert.Equal("X", check.GetProperty("output").GetProperty("text").GetString());
        Assert.Equal(["metadata", "output"], bound.Output.Calls.Select(call => call.Phase).ToArray());
        Assert.Equal(before, await DatabaseFingerprintAsync());
        Assert.Empty(bound.IdentityObserver!.Calls);
        Assert.Equal((1, 1), await BindingCountsAsync(bound.Ids.RunId));
    }
}
