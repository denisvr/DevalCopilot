using System.Text.Json;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Exercises <see cref="ChallengeResolutionResponseParser.TryParse"/> in isolation — the pure,
/// dependency-free protocol/schema gate a Codex challenge-resolution final response must pass
/// before <c>RecordChallengeResolutionResultCommandHandler</c> ever considers appending a Decision
/// set or a revised Proposal. Mirrors <c>ClaudeCriticalReviewResponseParserTests</c> in structure
/// and style.
/// </summary>
public sealed class ChallengeResolutionResponseParserTests
{
    private static readonly Guid Challenge1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Challenge2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ForeignChallenge = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static object BuildDecision(Guid challengeMessageId, string resolution = ChallengeResolutionOutputSchema.AcceptedResolution) => new
    {
        challengeMessageId = challengeMessageId.ToString(),
        summary = "Decision summary",
        resolution,
        rationale = "Rationale text",
        resultingPlanChanges = "Plan changes",
        nextAction = "Next action",
    };

    private static object BuildRevisedProposal() => new
    {
        summary = "Revised proposal summary",
        scope = "Revised scope",
        implementationSteps = "Revised steps",
        risks = "Revised risks",
        verificationPlan = "Revised verification",
        escalationPoints = "Revised escalation",
    };

    private static string BuildValidJson(IReadOnlyList<Guid> challengeMessageIds) => JsonSerializer.Serialize(new
    {
        summary = "Overall resolution summary",
        decisions = challengeMessageIds.Select(id => BuildDecision(id)).ToArray(),
        revisedProposal = BuildRevisedProposal(),
    });

    [Fact]
    public void TryParse_returns_a_validated_resolution_for_a_well_formed_response()
    {
        var expected = new HashSet<Guid> { Challenge1, Challenge2 };
        var json = BuildValidJson([Challenge1, Challenge2]);

        var resolution = ChallengeResolutionResponseParser.TryParse(json, expected);

        Assert.NotNull(resolution);
        Assert.Equal("Overall resolution summary", resolution.Summary);
        Assert.Equal(2, resolution.Decisions.Count);
        Assert.Equal(expected, resolution.Decisions.Select(decision => decision.ChallengeMessageId).ToHashSet());
        Assert.Equal("Revised proposal summary", resolution.RevisedProposal.Summary);

        using var revisedProposalContent = JsonDocument.Parse(resolution.RevisedProposal.StructuredContentJson);
        Assert.Equal("Revised scope", revisedProposalContent.RootElement.GetProperty("scope").GetString());

        var decision = resolution.Decisions.Single(candidate => candidate.ChallengeMessageId == Challenge1);
        using var decisionContent = JsonDocument.Parse(decision.StructuredContentJson);
        Assert.Equal(ChallengeResolutionOutputSchema.AcceptedResolution, decisionContent.RootElement.GetProperty("resolution").GetString());
        Assert.False(decisionContent.RootElement.TryGetProperty("challengeMessageId", out _));
        Assert.False(decisionContent.RootElement.TryGetProperty("summary", out _));
    }

    [Theory]
    [InlineData(ChallengeResolutionOutputSchema.AcceptedResolution)]
    [InlineData(ChallengeResolutionOutputSchema.PartiallyAcceptedResolution)]
    [InlineData(ChallengeResolutionOutputSchema.RejectedResolution)]
    public void TryParse_accepts_every_defined_resolution_value(string resolution)
    {
        var expected = new HashSet<Guid> { Challenge1 };
        var json = JsonSerializer.Serialize(new
        {
            summary = "Overall resolution summary",
            decisions = new[] { BuildDecision(Challenge1, resolution) },
            revisedProposal = BuildRevisedProposal(),
        });

        Assert.NotNull(ChallengeResolutionResponseParser.TryParse(json, expected));
    }

    [Fact]
    public void TryParse_returns_null_for_an_unrecognized_resolution_value()
    {
        var expected = new HashSet<Guid> { Challenge1 };
        var json = JsonSerializer.Serialize(new
        {
            summary = "Overall resolution summary",
            decisions = new[] { BuildDecision(Challenge1, "rejected_but_misspelled") },
            revisedProposal = BuildRevisedProposal(),
        });

        Assert.Null(ChallengeResolutionResponseParser.TryParse(json, expected));
    }

    [Fact]
    public void TryParse_returns_null_when_a_decision_is_missing_for_an_expected_challenge()
    {
        var expected = new HashSet<Guid> { Challenge1, Challenge2 };
        var json = BuildValidJson([Challenge1]);

        Assert.Null(ChallengeResolutionResponseParser.TryParse(json, expected));
    }

    [Fact]
    public void TryParse_returns_null_for_a_duplicated_challenge_message_id()
    {
        var expected = new HashSet<Guid> { Challenge1 };
        var json = JsonSerializer.Serialize(new
        {
            summary = "Overall resolution summary",
            decisions = new[] { BuildDecision(Challenge1), BuildDecision(Challenge1) },
            revisedProposal = BuildRevisedProposal(),
        });

        Assert.Null(ChallengeResolutionResponseParser.TryParse(json, expected));
    }

    [Fact]
    public void TryParse_returns_null_for_a_foreign_challenge_message_id_not_in_the_expected_set()
    {
        var expected = new HashSet<Guid> { Challenge1 };
        var json = BuildValidJson([Challenge1, ForeignChallenge]);

        Assert.Null(ChallengeResolutionResponseParser.TryParse(json, expected));
    }

    [Fact]
    public void TryParse_returns_null_for_an_invented_challenge_message_id_when_none_were_expected_to_match()
    {
        var expected = new HashSet<Guid> { Challenge1 };
        var json = BuildValidJson([ForeignChallenge]);

        Assert.Null(ChallengeResolutionResponseParser.TryParse(json, expected));
    }

    [Fact]
    public void TryParse_returns_null_for_a_malformed_challenge_message_id()
    {
        var expected = new HashSet<Guid> { Challenge1 };
        var json = JsonSerializer.Serialize(new
        {
            summary = "Overall resolution summary",
            decisions = new object[]
            {
                new
                {
                    challengeMessageId = "not-a-guid",
                    summary = "Decision summary",
                    resolution = ChallengeResolutionOutputSchema.AcceptedResolution,
                    rationale = "Rationale text",
                    resultingPlanChanges = "Plan changes",
                    nextAction = "Next action",
                },
            },
            revisedProposal = BuildRevisedProposal(),
        });

        Assert.Null(ChallengeResolutionResponseParser.TryParse(json, expected));
    }

    [Fact]
    public void TryParse_returns_null_when_the_decision_count_exceeds_the_maximum_of_five()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var expected = ids.ToHashSet();
        var json = BuildValidJson(ids);

        Assert.Null(ChallengeResolutionResponseParser.TryParse(json, expected));
    }

    [Fact]
    public void TryParse_returns_null_when_decisions_is_empty()
    {
        var expected = new HashSet<Guid>();
        var json = JsonSerializer.Serialize(new
        {
            summary = "Overall resolution summary",
            decisions = Array.Empty<object>(),
            revisedProposal = BuildRevisedProposal(),
        });

        Assert.Null(ChallengeResolutionResponseParser.TryParse(json, expected));
    }

    [Fact]
    public void TryParse_returns_null_when_a_decision_item_is_missing_a_required_field()
    {
        var expected = new HashSet<Guid> { Challenge1 };
        var json = JsonSerializer.Serialize(new
        {
            summary = "Overall resolution summary",
            decisions = new object[]
            {
                new
                {
                    challengeMessageId = Challenge1.ToString(),
                    summary = "Decision summary",
                    resolution = ChallengeResolutionOutputSchema.AcceptedResolution,
                    rationale = "Rationale text",
                    // resultingPlanChanges and nextAction omitted
                },
            },
            revisedProposal = BuildRevisedProposal(),
        });

        Assert.Null(ChallengeResolutionResponseParser.TryParse(json, expected));
    }

    [Fact]
    public void TryParse_returns_null_when_a_decision_item_has_an_unexpected_extra_field()
    {
        var expected = new HashSet<Guid> { Challenge1 };
        var json = JsonSerializer.Serialize(new
        {
            summary = "Overall resolution summary",
            decisions = new object[]
            {
                new
                {
                    challengeMessageId = Challenge1.ToString(),
                    summary = "Decision summary",
                    resolution = ChallengeResolutionOutputSchema.AcceptedResolution,
                    rationale = "Rationale text",
                    resultingPlanChanges = "Plan changes",
                    nextAction = "Next action",
                    unexpectedField = "should never be accepted",
                },
            },
            revisedProposal = BuildRevisedProposal(),
        });

        Assert.Null(ChallengeResolutionResponseParser.TryParse(json, expected));
    }

    [Fact]
    public void TryParse_returns_null_when_the_revised_proposal_is_missing_a_required_field()
    {
        var expected = new HashSet<Guid> { Challenge1 };
        var json = JsonSerializer.Serialize(new
        {
            summary = "Overall resolution summary",
            decisions = new[] { BuildDecision(Challenge1) },
            revisedProposal = new
            {
                summary = "Revised proposal summary",
                scope = "Revised scope",
                implementationSteps = "Revised steps",
                risks = "Revised risks",
                // verificationPlan and escalationPoints omitted
            },
        });

        Assert.Null(ChallengeResolutionResponseParser.TryParse(json, expected));
    }

    [Fact]
    public void TryParse_returns_null_when_an_unknown_top_level_field_is_present()
    {
        var expected = new HashSet<Guid> { Challenge1 };
        var json = JsonSerializer.Serialize(new
        {
            summary = "Overall resolution summary",
            decisions = new[] { BuildDecision(Challenge1) },
            revisedProposal = BuildRevisedProposal(),
            unexpectedField = "should never be accepted",
        });

        Assert.Null(ChallengeResolutionResponseParser.TryParse(json, expected));
    }

    [Fact]
    public void TryParse_returns_null_for_malformed_json()
    {
        Assert.Null(ChallengeResolutionResponseParser.TryParse("{ not json", new HashSet<Guid> { Challenge1 }));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"a plain string\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void TryParse_returns_null_for_a_non_object_root(string json)
    {
        Assert.Null(ChallengeResolutionResponseParser.TryParse(json, new HashSet<Guid> { Challenge1 }));
    }

    [Fact]
    public void TryParse_fails_closed_for_decision_content_that_looks_unsafe()
    {
        var expected = new HashSet<Guid> { Challenge1 };
        var json = JsonSerializer.Serialize(new
        {
            summary = "Overall resolution summary",
            decisions = new object[]
            {
                new
                {
                    challengeMessageId = Challenge1.ToString(),
                    summary = "Decision summary",
                    resolution = ChallengeResolutionOutputSchema.AcceptedResolution,
                    rationale = "Uses the API key from the environment",
                    resultingPlanChanges = "Plan changes",
                    nextAction = "Next action",
                },
            },
            revisedProposal = BuildRevisedProposal(),
        });

        Assert.Null(ChallengeResolutionResponseParser.TryParse(json, expected));
    }

    [Fact]
    public void TryParse_fails_closed_for_an_overlong_revised_proposal_field()
    {
        var expected = new HashSet<Guid> { Challenge1 };
        var overlongField = new string('x', 901);
        var json = JsonSerializer.Serialize(new
        {
            summary = "Overall resolution summary",
            decisions = new[] { BuildDecision(Challenge1) },
            revisedProposal = new
            {
                summary = "Revised proposal summary",
                scope = overlongField,
                implementationSteps = "Revised steps",
                risks = "Revised risks",
                verificationPlan = "Revised verification",
                escalationPoints = "Revised escalation",
            },
        });

        Assert.Null(ChallengeResolutionResponseParser.TryParse(json, expected));
    }
}
