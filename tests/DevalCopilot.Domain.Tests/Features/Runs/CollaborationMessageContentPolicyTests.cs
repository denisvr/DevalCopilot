using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>The blocked-literal vocabulary is one list that the filter reads, so the host-authored Proposal guidance that renders
/// it can never drift from what the filter refuses. Every example here is fabricated.</summary>
public sealed class CollaborationMessageContentPolicyTests
{
    private static readonly string[] ExpectedMarkers =
        [":\\", "bearer ", "credential", "password", "api_key", "api key", "secret", "environment"];

    [Fact]
    public void The_blocked_vocabulary_is_exactly_the_eight_reviewed_literals_in_order()
    {
        Assert.Equal(ExpectedMarkers, CollaborationMessageContentPolicy.BlockedMarkers);
    }

    [Theory]
    [MemberData(nameof(Markers))]
    public void Every_listed_marker_makes_a_summary_unsafe_in_any_letter_case(string marker)
    {
        Assert.False(CollaborationMessageContentPolicy.IsSafeSummary($"Plan the {marker}handling step."));
        Assert.False(CollaborationMessageContentPolicy.IsSafeSummary($"Plan the {marker.ToUpperInvariant()}handling step."));
    }

    [Theory]
    [MemberData(nameof(Markers))]
    public void Every_listed_marker_makes_a_proposal_field_invalid_in_each_field(string marker)
    {
        foreach (var field in new[] { "scope", "implementationSteps", "risks", "verificationPlan", "escalationPoints" })
        {
            Assert.Throws<ArgumentException>(() =>
                CollaborationMessageContentPolicy.Validate(CollaborationMessageType.Proposal, Proposal(field, $"Check {marker}later.")));
        }
    }

    [Fact]
    public void A_benign_paraphrase_of_each_concept_is_accepted()
    {
        foreach (var paraphrase in new[]
        {
            "Confirm the runtime context and target platform.",
            "Keep authentication details and access-token handling out of scope.",
            "Use repository-relative paths with forward slashes.",
            "Protected values stay outside every field.",
        })
        {
            Assert.True(CollaborationMessageContentPolicy.IsSafeSummary(paraphrase));
            CollaborationMessageContentPolicy.Validate(CollaborationMessageType.Proposal, Proposal("verificationPlan", paraphrase));
        }
    }

    public static IEnumerable<object[]> Markers() => ExpectedMarkers.Select(marker => new object[] { marker });

    private static string Proposal(string field, string value)
    {
        var fields = new Dictionary<string, string>
        {
            ["scope"] = "Ledger",
            ["implementationSteps"] = "Add the table then the query",
            ["risks"] = "Unbounded content",
            ["verificationPlan"] = "Tests",
            ["escalationPoints"] = "None expected",
        };
        fields[field] = value;
        return System.Text.Json.JsonSerializer.Serialize(fields);
    }
}
