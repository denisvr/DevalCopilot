using System.Text.Json.Nodes;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationReviewResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordReviewCorrectionResult;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.BrowserJourney;

// The local execution cycle journey (ADR-0060) drives the same doubles through two distinct corrections and a review that asks for
// a change. The new behavior is selected only by the conventions marker of that journey's own repository in the sealed manifest, so the
// pins here cover both sides: the cycle behavior with the marker and the unchanged behavior of every other journey without it.
public sealed partial class ProviderFixtureContractTests
{
    private const string CycleMarker = "CONVENTIONS-OF-local-execution-source";
    private const string SecondNote = "Removed the leftover review marker comment.";
    private const string CycleNit = " // REVIEW-NIT";

    private static string CycleManifest(string contract, bool cycle, string? reportNotes = null, Guid[]? findings = null)
    {
        var root = new JsonObject { ["expectedResponseContract"] = contract };
        root["implementedPlan"] = new JsonObject
        {
            ["messageId"] = Guid.NewGuid().ToString(),
            ["structuredContent"] = new JsonObject { ["implementationSteps"] = RevisedSteps },
        };
        root["executionReport"] = new JsonObject
        {
            ["messageId"] = Guid.NewGuid().ToString(),
            ["structuredContent"] = new JsonObject { ["implementationNotes"] = reportNotes ?? "Restored addition in Total." },
        };
        root["evidence"] = "verification Passed for the checkpoint";
        if (cycle)
        {
            root["projectInstructions"] = $"# local-execution-source conventions\n{CycleMarker}\n";
        }

        if (findings is not null)
        {
            root["orderedFindings"] = new JsonArray(findings.Select(id => (JsonNode)new JsonObject { ["messageId"] = id.ToString() }).ToArray());
        }

        return root.ToJsonString();
    }

    private string CycleReviewAnswer(string manifest)
    {
        Assert.Equal(0, Run("codex", CodexExec("ImplementationReview"), standardInput: manifest).ExitCode);
        return File.ReadAllText(ResultPath());
    }

    [Fact]
    public void A_cycle_review_asks_for_one_change_until_the_second_correction_report_and_then_approves()
    {
        var first = CycleReviewAnswer(CycleManifest("ImplementationReview", cycle: true));
        var parsedFirst = ImplementationReviewResponseParser.TryParse(first);
        Assert.NotNull(parsedFirst);
        Assert.False(parsedFirst.IsApproved);
        Assert.Single(parsedFirst.Findings);

        File.Delete(ResultPath());
        var second = CycleReviewAnswer(CycleManifest("ImplementationReview", cycle: true, reportNotes: SecondNote));
        var parsedSecond = ImplementationReviewResponseParser.TryParse(second);
        Assert.NotNull(parsedSecond);
        Assert.True(parsedSecond.IsApproved);

        var outcomes = ReadLog().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .Where(entry => entry["contract"]?.GetValue<string>() == "ImplementationReview")
            .Select(entry => entry["outcome"]!.GetValue<string>())
            .ToArray();
        Assert.Equal(["changesRequested", "approved"], outcomes);
    }

    [Fact]
    public void A_review_without_the_cycle_marker_still_approves_whatever_the_report_says_and_logs_no_outcome()
    {
        var answer = CycleReviewAnswer(CycleManifest("ImplementationReview", cycle: false));
        var parsed = ImplementationReviewResponseParser.TryParse(answer);
        Assert.NotNull(parsed);
        Assert.True(parsed.IsApproved);
        Assert.DoesNotContain("\"outcome\"", ReadLog(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_two_cycle_corrections_apply_in_order_each_with_its_own_report_and_verification_passes_after_both()
    {
        PrepareDefectedCandidate();
        Assert.Equal(1, Run("verify", ["check"]).ExitCode);
        var findingIds = new[] { Guid.NewGuid() };
        var expected = Guid.NewGuid();

        var first = Run("claude", MutatingClaude("ReviewCorrection"), standardInput: CycleManifest("ReviewCorrection", cycle: true, findings: [expected]));
        Assert.Equal(0, first.ExitCode);
        var firstContent = File.ReadAllText(_candidate);
        Assert.Contains("return left + right;" + CycleNit, firstContent, StringComparison.Ordinal);
        Assert.DoesNotContain(CandidateDefect, firstContent, StringComparison.Ordinal);
        Assert.Equal(0, Run("verify", ["check"]).ExitCode);
        var firstCorrection = ParseCorrection(first, expected);
        Assert.Equal("Restored addition in Total.", firstCorrection.ExecutionReport.ImplementationNotes);

        var second = Run("claude", MutatingClaude("ReviewCorrection"), standardInput: CycleManifest("ReviewCorrection", cycle: true, findings: findingIds));
        Assert.Equal(0, second.ExitCode);
        var secondContent = File.ReadAllText(_candidate);
        Assert.Contains("return left + right;", secondContent, StringComparison.Ordinal);
        Assert.DoesNotContain("REVIEW-NIT", secondContent, StringComparison.Ordinal);
        Assert.Equal(0, Run("verify", ["check"]).ExitCode);
        var secondCorrection = ParseCorrection(second, findingIds[0]);
        Assert.Equal(SecondNote, secondCorrection.ExecutionReport.ImplementationNotes);

        // A third correction is not served: the file is in neither state a cycle correction expects.
        Assert.Equal(Unsupported, Run("claude", MutatingClaude("ReviewCorrection"), standardInput: CycleManifest("ReviewCorrection", cycle: true, findings: [Guid.NewGuid()])).ExitCode);
        Assert.Equal(secondContent, File.ReadAllText(_candidate));
    }

    [Fact]
    public void A_correction_without_the_cycle_marker_keeps_the_single_ordinary_edit()
    {
        PrepareDefectedCandidate();

        var first = Run("claude", MutatingClaude("ReviewCorrection"), standardInput: CycleManifest("ReviewCorrection", cycle: false, findings: [Guid.NewGuid()]));

        Assert.Equal(0, first.ExitCode);
        Assert.DoesNotContain("REVIEW-NIT", File.ReadAllText(_candidate), StringComparison.Ordinal);
        Assert.Equal(Unsupported, Run("claude", MutatingClaude("ReviewCorrection"), standardInput: CycleManifest("ReviewCorrection", cycle: false, findings: [Guid.NewGuid()])).ExitCode);
    }

    [Fact]
    public void A_first_pass_failure_without_a_correction_is_the_diagnosis_origin_of_the_cycle()
    {
        PrepareDefectedCandidate();

        var failed = Run("verify", ["check"]);
        Assert.Equal(1, failed.ExitCode);
        Assert.Contains("TOTAL-NOT-SUM", failed.StandardError, StringComparison.Ordinal);
        Assert.Equal(0, Run("codex", CodexExec("VerificationDiagnosis"), standardInput: CycleDiagnosisManifest()).ExitCode);
        Assert.Contains("findings", File.ReadAllText(ResultPath()), StringComparison.Ordinal);
    }

    [Fact]
    public void The_root_is_accepted_and_implementable_only_for_the_cycle_repository()
    {
        string RootReview(bool cycle) => new JsonObject
        {
            ["expectedResponseContract"] = "CriticalReview",
            ["reviewedProposal"] = ProposalUnder(RootSteps),
            ["projectInstructions"] = cycle ? CycleMarker : "other conventions",
        }.ToJsonString();

        var accepted = Run("claude", ReadOnlyClaude("CriticalReview"), standardInput: RootReview(cycle: true));
        Assert.Equal(0, accepted.ExitCode);
        Assert.Equal("accept", ResultOf(accepted)["decision"]!.GetValue<string>());
        var challenged = Run("claude", ReadOnlyClaude("CriticalReview"), standardInput: RootReview(cycle: false));
        Assert.Equal(0, challenged.ExitCode);
        Assert.Equal("challenge", ResultOf(challenged)["decision"]!.GetValue<string>());

        string RootImplementation(bool cycle) => new JsonObject
        {
            ["expectedResponseContract"] = "ImplementationReport",
            ["resolvedPlan"] = ProposalUnder(RootSteps),
            ["projectInstructions"] = cycle ? CycleMarker : "other conventions",
        }.ToJsonString();

        Assert.Equal(PlanRefused, Run("claude", MutatingClaude("ImplementationReport"), standardInput: RootImplementation(cycle: false)).ExitCode);
        Assert.Contains(Stub, File.ReadAllText(_candidate), StringComparison.Ordinal);
        Assert.Equal(0, Run("claude", MutatingClaude("ImplementationReport"), standardInput: RootImplementation(cycle: true)).ExitCode);
        Assert.Contains(CandidateDefect, File.ReadAllText(_candidate), StringComparison.Ordinal);

        string RootReviewOfPlan(bool cycle) => new JsonObject
        {
            ["expectedResponseContract"] = "ImplementationReview",
            ["resolvedPlan"] = ProposalUnder(RootSteps),
            ["evidence"] = "verification Passed for the checkpoint",
            ["projectInstructions"] = cycle ? CycleMarker : "other conventions",
        }.ToJsonString();

        Assert.Equal(PlanRefused, Run("codex", CodexExec("ImplementationReview"), standardInput: RootReviewOfPlan(cycle: false)).ExitCode);
        Assert.Equal(0, Run("codex", CodexExec("ImplementationReview"), standardInput: RootReviewOfPlan(cycle: true)).ExitCode);
    }

    private static string CycleDiagnosisManifest() => new JsonObject
    {
        ["expectedResponseContract"] = "VerificationDiagnosis",
        ["implementedPlan"] = new JsonObject
        {
            ["messageId"] = Guid.NewGuid().ToString(),
            ["structuredContent"] = new JsonObject { ["implementationSteps"] = RevisedSteps },
        },
        ["evidence"] = "the failed output carries TOTAL-NOT-SUM",
        ["projectInstructions"] = CycleMarker,
    }.ToJsonString();

    private static JsonObject ResultOf(Result result) =>
        JsonNode.Parse(JsonNode.Parse(result.StandardOutput)!.AsObject()["result"]!.GetValue<string>())!.AsObject();

    private static ValidatedReviewCorrection ParseCorrection(Result result, Guid expectedFinding)
    {
        var envelope = JsonNode.Parse(result.StandardOutput)!.AsObject();
        var parsed = ReviewCorrectionResponseParser.TryParse(envelope["result"]!.GetValue<string>(), [expectedFinding]);
        Assert.NotNull(parsed);
        return parsed;
    }
}
