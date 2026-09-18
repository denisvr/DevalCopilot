using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Exercises <see cref="ClaudeCriticalReviewResponseParser.TryParse"/> in isolation — the pure,
/// dependency-free protocol/schema gate a Claude critical-review final response must pass before
/// <c>RecordClaudeCriticalReviewResultCommandHandler</c> ever considers appending an Acceptance or
/// Challenge set. Mirrors <c>CodexFinalResponseParserTests</c> in structure and style.
/// </summary>
public sealed class ClaudeCriticalReviewResponseParserTests
{
    private const string ValidAcceptanceJson =
        """
        {
            "decision": "accept",
            "summary": "The proposal correctly scopes the ledger change.",
            "rationale": "The implementation steps match the stated risks and verification plan."
        }
        """;

    private static string BuildChallengeJson(int challengeCount)
    {
        var challenges = Enumerable.Range(1, challengeCount).Select(index => new
        {
            summary = $"Challenge {index} summary",
            disputedItem = $"Disputed item {index}",
            materialImpact = $"Material impact {index}",
            reasoning = $"Reasoning {index}",
            alternativeOrQuestion = $"Alternative or question {index}",
        });

        return JsonSerializer.Serialize(new
        {
            decision = "challenge",
            summary = "The proposal has material gaps.",
            challenges,
        });
    }

    [Fact]
    public void TryParse_returns_a_validated_acceptance_for_a_well_formed_accept_response()
    {
        var review = ClaudeCriticalReviewResponseParser.TryParse(ValidAcceptanceJson);

        Assert.NotNull(review);
        Assert.True(review.IsAcceptance);
        Assert.NotNull(review.Acceptance);
        Assert.Equal("The proposal correctly scopes the ledger change.", review.Acceptance.Summary);
        Assert.Empty(review.Challenges);
        Assert.Null(review.ChallengeSetSummary);

        using var structuredContent = JsonDocument.Parse(review.Acceptance.StructuredContentJson);
        var root = structuredContent.RootElement;
        Assert.Equal(
            "The implementation steps match the stated risks and verification plan.",
            root.GetProperty("rationale").GetString());
        // The top-level summary is never duplicated into the structured content payload.
        Assert.False(root.TryGetProperty("summary", out _));
    }

    [Fact]
    public void TryParse_returns_null_when_the_accept_response_is_missing_a_required_field()
    {
        var missingRationale =
            """
            {
                "decision": "accept",
                "summary": "The proposal correctly scopes the ledger change."
            }
            """;

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(missingRationale));
    }

    [Fact]
    public void TryParse_returns_null_when_the_accept_response_has_an_unexpected_extra_field()
    {
        var withExtraField =
            """
            {
                "decision": "accept",
                "summary": "The proposal correctly scopes the ledger change.",
                "rationale": "The implementation steps match the stated risks and verification plan.",
                "unexpectedField": "should never be accepted"
            }
            """;

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(withExtraField));
    }

    [Fact]
    public void TryParse_returns_null_when_an_accept_response_carries_a_challenges_field()
    {
        var withChallenges =
            """
            {
                "decision": "accept",
                "summary": "The proposal correctly scopes the ledger change.",
                "rationale": "Fine.",
                "challenges": []
            }
            """;

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(withChallenges));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void TryParse_returns_a_validated_challenge_set_at_the_boundary_counts(int challengeCount)
    {
        var review = ClaudeCriticalReviewResponseParser.TryParse(BuildChallengeJson(challengeCount));

        Assert.NotNull(review);
        Assert.False(review.IsAcceptance);
        Assert.Null(review.Acceptance);
        Assert.Equal(challengeCount, review.Challenges.Count);
        Assert.Equal("The proposal has material gaps.", review.ChallengeSetSummary);

        for (var index = 0; index < challengeCount; index++)
        {
            var challenge = review.Challenges[index];
            Assert.Equal($"Challenge {index + 1} summary", challenge.Summary);

            using var structuredContent = JsonDocument.Parse(challenge.StructuredContentJson);
            var root = structuredContent.RootElement;
            Assert.Equal($"Disputed item {index + 1}", root.GetProperty("disputedItem").GetString());
            Assert.Equal($"Material impact {index + 1}", root.GetProperty("materialImpact").GetString());
            Assert.Equal($"Reasoning {index + 1}", root.GetProperty("reasoning").GetString());
            Assert.Equal($"Alternative or question {index + 1}", root.GetProperty("alternativeOrQuestion").GetString());
            Assert.False(root.TryGetProperty("summary", out _));
        }
    }

    [Fact]
    public void TryParse_returns_null_when_the_challenge_count_is_zero()
    {
        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(BuildChallengeJson(0)));
    }

    [Fact]
    public void TryParse_returns_null_when_the_challenge_count_exceeds_the_maximum_of_five()
    {
        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(BuildChallengeJson(6)));
    }

    [Fact]
    public void TryParse_returns_null_when_a_challenge_item_is_missing_a_required_field()
    {
        var withMissingField =
            """
            {
                "decision": "challenge",
                "summary": "The proposal has material gaps.",
                "challenges": [
                    {
                        "summary": "Challenge 1 summary",
                        "disputedItem": "Disputed item 1",
                        "materialImpact": "Material impact 1",
                        "reasoning": "Reasoning 1"
                    }
                ]
            }
            """;

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(withMissingField));
    }

    [Fact]
    public void TryParse_returns_null_when_a_challenge_item_has_an_unexpected_extra_field()
    {
        var withExtraField =
            """
            {
                "decision": "challenge",
                "summary": "The proposal has material gaps.",
                "challenges": [
                    {
                        "summary": "Challenge 1 summary",
                        "disputedItem": "Disputed item 1",
                        "materialImpact": "Material impact 1",
                        "reasoning": "Reasoning 1",
                        "alternativeOrQuestion": "Alternative or question 1",
                        "unexpectedField": "should never be accepted"
                    }
                ]
            }
            """;

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(withExtraField));
    }

    [Fact]
    public void TryParse_returns_null_when_the_challenge_set_summary_is_missing()
    {
        var withoutSummary =
            """
            {
                "decision": "challenge",
                "challenges": [
                    {
                        "summary": "Challenge 1 summary",
                        "disputedItem": "Disputed item 1",
                        "materialImpact": "Material impact 1",
                        "reasoning": "Reasoning 1",
                        "alternativeOrQuestion": "Alternative or question 1"
                    }
                ]
            }
            """;

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(withoutSummary));
    }

    [Fact]
    public void TryParse_returns_null_when_challenges_is_not_an_array()
    {
        var withObjectChallenges =
            """
            {
                "decision": "challenge",
                "summary": "The proposal has material gaps.",
                "challenges": {}
            }
            """;

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(withObjectChallenges));
    }

    [Fact]
    public void TryParse_returns_null_for_malformed_json()
    {
        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse("{ not json"));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"a plain string\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void TryParse_returns_null_for_a_non_object_root(string json)
    {
        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
    }

    [Fact]
    public void TryParse_returns_null_when_decision_is_missing()
    {
        var withoutDecision =
            """
            {
                "summary": "The proposal correctly scopes the ledger change.",
                "rationale": "Fine."
            }
            """;

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(withoutDecision));
    }

    [Theory]
    [InlineData("Accept")]
    [InlineData("ACCEPT")]
    [InlineData("accepted")]
    [InlineData("reject")]
    [InlineData("")]
    public void TryParse_returns_null_for_an_unrecognized_decision_value(string decision)
    {
        var json = JsonSerializer.Serialize(new
        {
            decision,
            summary = "The proposal correctly scopes the ledger change.",
            rationale = "Fine.",
        });

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
    }

    [Fact]
    public void TryParse_returns_null_when_decision_is_not_a_string()
    {
        var withNumericDecision =
            """
            {
                "decision": 1,
                "summary": "The proposal correctly scopes the ledger change.",
                "rationale": "Fine."
            }
            """;

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(withNumericDecision));
    }

    [Fact]
    public void TryParse_returns_null_when_an_accept_field_value_is_not_a_string()
    {
        var withNumericField =
            """
            {
                "decision": "accept",
                "summary": "The proposal correctly scopes the ledger change.",
                "rationale": 12345
            }
            """;

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(withNumericField));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_returns_null_for_a_blank_accept_field_value(string blankValue)
    {
        var json = JsonSerializer.Serialize(new
        {
            decision = "accept",
            summary = "The proposal correctly scopes the ledger change.",
            rationale = blankValue,
        });

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
    }

    [Fact]
    public void TryParse_returns_null_for_a_duplicated_field()
    {
        var withDuplicateField =
            """
            {
                "decision": "accept",
                "summary": "The proposal correctly scopes the ledger change.",
                "summary": "A second, conflicting summary.",
                "rationale": "Fine."
            }
            """;

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(withDuplicateField));
    }

    [Fact]
    public void TryParse_fails_closed_for_an_overlong_accept_summary()
    {
        var overlongSummary = new string('x', 601);
        var json = JsonSerializer.Serialize(new
        {
            decision = "accept",
            summary = overlongSummary,
            rationale = "Fine.",
        });

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
    }

    [Fact]
    public void TryParse_fails_closed_for_an_overlong_accept_rationale()
    {
        var overlongField = new string('x', 901);
        var json = JsonSerializer.Serialize(new
        {
            decision = "accept",
            summary = "The proposal correctly scopes the ledger change.",
            rationale = overlongField,
        });

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
    }

    [Fact]
    public void TryParse_fails_closed_for_an_overlong_challenge_set_summary()
    {
        var overlongSummary = new string('x', 601);
        var json = JsonSerializer.Serialize(new
        {
            decision = "challenge",
            summary = overlongSummary,
            challenges = new[]
            {
                new
                {
                    summary = "Challenge 1 summary",
                    disputedItem = "Disputed item 1",
                    materialImpact = "Material impact 1",
                    reasoning = "Reasoning 1",
                    alternativeOrQuestion = "Alternative or question 1",
                },
            },
        });

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
    }

    [Fact]
    public void TryParse_fails_closed_for_an_overlong_challenge_item_field()
    {
        var overlongField = new string('x', 901);
        var json = JsonSerializer.Serialize(new
        {
            decision = "challenge",
            summary = "The proposal has material gaps.",
            challenges = new[]
            {
                new
                {
                    summary = "Challenge 1 summary",
                    disputedItem = overlongField,
                    materialImpact = "Material impact 1",
                    reasoning = "Reasoning 1",
                    alternativeOrQuestion = "Alternative or question 1",
                },
            },
        });

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
    }

    [Theory]
    [InlineData("summary")]
    [InlineData("rationale")]
    public void TryParse_fails_closed_for_accept_content_that_looks_unsafe(string fieldName)
    {
        var values = new Dictionary<string, string>
        {
            ["decision"] = "accept",
            ["summary"] = "The proposal correctly scopes the ledger change.",
            ["rationale"] = "Fine.",
        };
        values[fieldName] = "Uses the API key from the environment";

        var json = JsonSerializer.Serialize(values);

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
    }

    [Fact]
    public void TryParse_fails_closed_for_challenge_content_that_looks_unsafe()
    {
        var json = JsonSerializer.Serialize(new
        {
            decision = "challenge",
            summary = "The proposal has material gaps.",
            challenges = new[]
            {
                new
                {
                    summary = "Challenge 1 summary",
                    disputedItem = "Uses the API key from the environment",
                    materialImpact = "Material impact 1",
                    reasoning = "Reasoning 1",
                    alternativeOrQuestion = "Alternative or question 1",
                },
            },
        });

        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
    }
}
