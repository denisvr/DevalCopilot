using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordReviewCorrectionResult;

/// <summary>A fully parsed correction response. The response order is the same as the exact
/// finding input order captured on the attempt.</summary>
public sealed class ValidatedReviewCorrection
{
    private ValidatedReviewCorrection(IReadOnlyList<ValidatedRevisionResponse> responses, ValidatedImplementationReport report)
    {
        RevisionResponses = responses;
        ExecutionReport = report;
    }

    public static ValidatedReviewCorrection Create(
        IReadOnlyList<ValidatedRevisionResponse> responses, ValidatedImplementationReport report) =>
        new(responses.ToArray(), report);

    public IReadOnlyList<ValidatedRevisionResponse> RevisionResponses { get; }

    public ValidatedImplementationReport ExecutionReport { get; }
}

public sealed record ValidatedRevisionResponse(
    Guid FindingMessageId,
    string Disposition,
    string Evidence,
    string ResultingSourceChanges);
