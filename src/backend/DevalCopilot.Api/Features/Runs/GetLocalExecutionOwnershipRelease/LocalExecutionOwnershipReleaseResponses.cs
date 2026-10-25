using System.Globalization;
using DevalCopilot.Application.Features.Runs.Commands.ReleaseLocalExecutionOwnership;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionOwnershipRelease;

/// <summary>The one safe mapping of a release reading or receipt, shared by the GET and the POST (ADR-0065). Times are canonical UTC
/// round-trip strings and counters canonical Int64 strings.</summary>
internal static class LocalExecutionOwnershipReleaseResponses
{
    public static LocalExecutionOwnershipReleaseResponse Map(GetLocalExecutionOwnershipReleaseQueryResult value) => new(
        value.RunId,
        value.StepId,
        value.Status.ToString(),
        value.ReasonCode,
        value.Candidate is { } candidate ? Map(candidate) : null,
        value.Release is { } release ? Map(release.Facts, release.ReleaseId, release.ReleasedAtUtc, release.EventSequence) : null);

    public static LocalExecutionOwnershipReleaseResponse Map(ReleaseLocalExecutionOwnershipCommandResult value) => new(
        value.RunId,
        value.StepId,
        nameof(LocalExecutionOwnershipReleaseStatus.Released),
        null,
        null,
        Map(value.Facts, value.ReleaseId, value.ReleasedAtUtc, value.EventSequence));

    private static LocalExecutionOwnershipReleaseCandidateResponse Map(LocalExecutionOwnershipReleaseCandidate candidate)
    {
        var facts = candidate.Facts;
        return new LocalExecutionOwnershipReleaseCandidateResponse(
            facts.SequenceId,
            facts.SequenceState.ToString(),
            facts.AdmissionMode.ToString(),
            facts.SourceCheckpointId,
            facts.SourceFingerprintSha256,
            facts.Kind.ToString(),
            facts.Ordinal,
            facts.PassNumber,
            facts.Family.ToString(),
            facts.OperationId,
            facts.OperationStatus,
            facts.ProcessOutcome.ToString(),
            facts.SemanticExitCode,
            facts.ObservationId,
            facts.PhysicalExitCode,
            facts.ObservedElapsedTicks.ToString(CultureInfo.InvariantCulture),
            facts.ProcessDurationTicks.ToString(CultureInfo.InvariantCulture),
            LocalExecutionOwnershipReleaseConfirmation.Time(facts.DispatchedAtUtc),
            LocalExecutionOwnershipReleaseConfirmation.Time(facts.RecordedAtUtc),
            candidate.ConfirmationVersion.ToString(CultureInfo.InvariantCulture),
            candidate.ConfirmationSha256,
            LocalExecutionOwnershipReleaseConfirmation.Time(candidate.ConfirmationExpiresAtUtc));
    }

    private static LocalExecutionOwnershipReleaseReceiptResponse Map(
        LocalExecutionOwnershipReleaseFacts facts, Guid releaseId, DateTimeOffset releasedAtUtc, long eventSequence) => new(
        releaseId,
        facts.Family.ToString(),
        facts.OperationId,
        facts.Kind.ToString(),
        LocalExecutionOwnershipReleaseConfirmation.Time(releasedAtUtc),
        eventSequence.ToString(CultureInfo.InvariantCulture));
}
