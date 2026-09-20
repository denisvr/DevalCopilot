using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Commands.RecordReviewCorrectionResult;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class ReviewCorrectionResponseParserTests
{
    private static readonly Guid FirstFindingId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondFindingId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void TryParse_accepts_one_revision_response_per_finding_in_durable_order()
    {
        var response = BuildResponse(FirstFindingId, SecondFindingId);

        var parsed = ReviewCorrectionResponseParser.TryParse(response, [FirstFindingId, SecondFindingId]);

        Assert.NotNull(parsed);
        Assert.Equal([FirstFindingId, SecondFindingId], parsed.RevisionResponses.Select(item => item.FindingMessageId));
        Assert.Equal(["src/one.cs"], parsed.ExecutionReport.ChangedRelativePaths);
    }

    [Fact]
    public void TryParse_rejects_findings_in_a_different_order()
    {
        var response = BuildResponse(FirstFindingId, SecondFindingId);

        Assert.Null(ReviewCorrectionResponseParser.TryParse(response, [SecondFindingId, FirstFindingId]));
    }

    [Fact]
    public void TryParse_rejects_a_missing_revision_response()
    {
        var response = BuildResponse(FirstFindingId);

        Assert.Null(ReviewCorrectionResponseParser.TryParse(response, [FirstFindingId, SecondFindingId]));
    }

    [Fact]
    public void TryParse_rejects_a_foreign_finding_id()
    {
        var response = BuildResponse(FirstFindingId, Guid.Parse("33333333-3333-3333-3333-333333333333"));

        Assert.Null(ReviewCorrectionResponseParser.TryParse(response, [FirstFindingId, SecondFindingId]));
    }

    [Fact]
    public void TryParse_rejects_extra_properties()
    {
        var response = JsonSerializer.Serialize(new
        {
            revisionResponses = new[]
            {
                new
                {
                    findingMessageId = FirstFindingId,
                    disposition = "Applied",
                    evidence = "The source was corrected.",
                    resultingSourceChanges = "Updated the implementation.",
                    unexpected = "must fail",
                },
            },
            executionReport = BuildReport(),
        });

        Assert.Null(ReviewCorrectionResponseParser.TryParse(response, [FirstFindingId]));
    }

    [Fact]
    public void TryParse_rejects_unsafe_changed_paths()
    {
        var response = JsonSerializer.Serialize(new
        {
            revisionResponses = new[]
            {
                new
                {
                    findingMessageId = FirstFindingId,
                    disposition = "Applied",
                    evidence = "The source was corrected.",
                    resultingSourceChanges = "Updated the implementation.",
                },
            },
            executionReport = new
            {
                summary = "Correction completed.",
                changedRelativePaths = new[] { "../outside.cs" },
                implementationNotes = "Updated the implementation.",
                unexpectedDiscoveries = "",
                remainingRisks = "",
                recommendedVerification = "Run the focused tests.",
            },
        });

        Assert.Null(ReviewCorrectionResponseParser.TryParse(response, [FirstFindingId]));
    }

    private static string BuildResponse(params Guid[] findingIds) => JsonSerializer.Serialize(new
    {
        revisionResponses = findingIds.Select(findingId => new
        {
            findingMessageId = findingId,
            disposition = "Applied",
            evidence = "The source was corrected.",
            resultingSourceChanges = "Updated the implementation.",
        }),
        executionReport = BuildReport(),
    });

    private static object BuildReport() => new
    {
        summary = "Correction completed.",
        changedRelativePaths = new[] { "src/one.cs" },
        implementationNotes = "Updated the implementation.",
        unexpectedDiscoveries = "",
        remainingRisks = "",
        recommendedVerification = "Run the focused tests.",
    };
}
