using System.Text.Json;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationReviewResult;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Exercises <see cref="ImplementationReviewResponseParser.TryParse"/> in isolation — the pure,
/// dependency-free protocol/schema gate a Codex implementation-review final response must pass
/// before <c>RecordImplementationReviewResultCommandHandler</c> ever considers appending a
/// ReviewApproval or ReviewFinding set. Mirrors <c>ChallengeResolutionResponseParserTests</c> in
/// structure and style.
/// </summary>
public sealed class ImplementationReviewResponseParserTests
{
    private static object BuildFinding(string? affectedRelativePath = "src/Foo.cs", int index = 0) => new
    {
        severity = ImplementationReviewOutputSchema.Severities[0],
        category = ImplementationReviewOutputSchema.Categories[0],
        summary = $"Finding summary {index}",
        evidence = $"Evidence text {index}",
        requiredChange = $"Required change text {index}",
        affectedRelativePath,
    };

    private static string BuildApprovedJson() => JsonSerializer.Serialize(new
    {
        outcome = ImplementationReviewOutputSchema.ApprovedOutcome,
        summary = "Overall approval summary",
        rationale = "Approval rationale",
        residualRisks = "None material",
        findings = Array.Empty<object>(),
    });

    private static string BuildChangesRequestedJson(int findingCount = 1) => JsonSerializer.Serialize(new
    {
        outcome = ImplementationReviewOutputSchema.ChangesRequestedOutcome,
        summary = "Overall changes-requested summary",
        rationale = (string?)null,
        residualRisks = (string?)null,
        findings = Enumerable.Range(0, findingCount)
            .Select(index => BuildFinding($"src/Foo{index}.cs", index))
            .ToArray(),
    });

    [Fact]
    public void TryParse_returns_a_validated_approval_for_a_well_formed_approved_response()
    {
        var review = ImplementationReviewResponseParser.TryParse(BuildApprovedJson());

        Assert.NotNull(review);
        Assert.True(review.IsApproved);
        Assert.Equal("Approval rationale", review.Rationale);
        Assert.Equal("None material", review.ResidualRisks);
        Assert.Empty(review.Findings);
    }

    [Fact]
    public void TryParse_returns_a_validated_changes_requested_review_for_a_well_formed_response()
    {
        var review = ImplementationReviewResponseParser.TryParse(BuildChangesRequestedJson(2));

        Assert.NotNull(review);
        Assert.False(review.IsApproved);
        Assert.Null(review.Rationale);
        Assert.Null(review.ResidualRisks);
        Assert.Equal(2, review.Findings.Count);
        Assert.All(review.Findings, finding => Assert.NotNull(finding.AffectedRelativePath));
    }

    [Fact]
    public void TryParse_rejects_an_approved_response_that_also_carries_findings()
    {
        var json = JsonSerializer.Serialize(new
        {
            outcome = ImplementationReviewOutputSchema.ApprovedOutcome,
            summary = "Approval summary",
            rationale = "Rationale",
            residualRisks = "Risks",
            findings = new[] { BuildFinding() },
        });

        Assert.Null(ImplementationReviewResponseParser.TryParse(json));
    }

    [Fact]
    public void TryParse_rejects_a_changes_requested_response_that_also_carries_a_rationale()
    {
        var json = JsonSerializer.Serialize(new
        {
            outcome = ImplementationReviewOutputSchema.ChangesRequestedOutcome,
            summary = "Changes summary",
            rationale = "Should not be present",
            residualRisks = (string?)null,
            findings = new[] { BuildFinding() },
        });

        Assert.Null(ImplementationReviewResponseParser.TryParse(json));
    }

    [Fact]
    public void TryParse_rejects_zero_findings_for_changes_requested()
    {
        Assert.Null(ImplementationReviewResponseParser.TryParse(BuildChangesRequestedJson(0)));
    }

    [Fact]
    public void TryParse_rejects_more_than_ten_findings()
    {
        Assert.Null(ImplementationReviewResponseParser.TryParse(BuildChangesRequestedJson(11)));
    }

    [Fact]
    public void TryParse_rejects_a_duplicate_finding()
    {
        var json = JsonSerializer.Serialize(new
        {
            outcome = ImplementationReviewOutputSchema.ChangesRequestedOutcome,
            summary = "Changes summary",
            rationale = (string?)null,
            residualRisks = (string?)null,
            findings = new[] { BuildFinding(), BuildFinding() },
        });

        Assert.Null(ImplementationReviewResponseParser.TryParse(json));
    }

    [Theory]
    [InlineData(@"C:\repos\project\src\Foo.cs")]
    [InlineData("/etc/passwd")]
    [InlineData("../secrets.txt")]
    [InlineData("src/../../../etc/passwd")]
    public void TryParse_rejects_an_unsafe_affected_relative_path(string unsafePath)
    {
        var json = JsonSerializer.Serialize(new
        {
            outcome = ImplementationReviewOutputSchema.ChangesRequestedOutcome,
            summary = "Changes summary",
            rationale = (string?)null,
            residualRisks = (string?)null,
            findings = new[] { BuildFinding(unsafePath) },
        });

        Assert.Null(ImplementationReviewResponseParser.TryParse(json));
    }

    [Fact]
    public void TryParse_accepts_a_finding_with_no_affected_path()
    {
        var review = ImplementationReviewResponseParser.TryParse(BuildChangesRequestedJson(1).Replace("\"src/Foo0.cs\"", "null"));

        Assert.NotNull(review);
        Assert.Null(Assert.Single(review.Findings).AffectedRelativePath);
    }

    [Fact]
    public void TryParse_rejects_an_unrecognized_severity()
    {
        var json = JsonSerializer.Serialize(new
        {
            outcome = ImplementationReviewOutputSchema.ChangesRequestedOutcome,
            summary = "Changes summary",
            rationale = (string?)null,
            residualRisks = (string?)null,
            findings = new[]
            {
                new
                {
                    severity = "catastrophic",
                    category = ImplementationReviewOutputSchema.Categories[0],
                    summary = "Finding summary",
                    evidence = "Evidence",
                    requiredChange = "Change",
                    affectedRelativePath = (string?)null,
                },
            },
        });

        Assert.Null(ImplementationReviewResponseParser.TryParse(json));
    }

    [Fact]
    public void TryParse_rejects_an_unrecognized_outcome()
    {
        var json = JsonSerializer.Serialize(new
        {
            outcome = "maybe",
            summary = "Summary",
            rationale = (string?)null,
            residualRisks = (string?)null,
            findings = Array.Empty<object>(),
        });

        Assert.Null(ImplementationReviewResponseParser.TryParse(json));
    }

    [Fact]
    public void TryParse_rejects_an_unknown_top_level_field()
    {
        var json = """
            {"outcome":"approved","summary":"s","rationale":"r","residualRisks":"rr","findings":[],"extra":"nope"}
            """;

        Assert.Null(ImplementationReviewResponseParser.TryParse(json));
    }

    [Fact]
    public void TryParse_rejects_malformed_json()
    {
        Assert.Null(ImplementationReviewResponseParser.TryParse("{not json"));
    }

    [Fact]
    public void TryParse_rejects_a_non_object_root()
    {
        Assert.Null(ImplementationReviewResponseParser.TryParse("[]"));
    }
}
