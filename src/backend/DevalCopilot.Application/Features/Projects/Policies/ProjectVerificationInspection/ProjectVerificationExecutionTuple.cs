using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

/// <summary>
/// The recorded status, outcome, exit code, time and fingerprint shapes the Domain transitions can produce (ADR-0069); any other
/// combination, an unknown enum or a rejected stored number is contradictory. Running may or may not be dispatched; a terminal process
/// result (Passed, Failed, TimedOut, Cancelled) needs a dispatch, a completion and a completion fingerprint equal to the claim; a
/// SourceChanged record is either invalidated before dispatch (no process outcome) or completed against a different source; Interrupted
/// and the dispatch refusal carry no outcome, exit code or completion fingerprint, and the refusal is never dispatched. Nothing is
/// promoted to a clean exit or to a successful verification.
/// </summary>
internal static class ProjectVerificationExecutionTuple
{
    public static bool TryClassify(
        ProjectVerificationExecutionRow row,
        DateTimeOffset claimedAtUtc,
        DateTimeOffset? dispatchedAtUtc,
        DateTimeOffset? completedAtUtc,
        out VerificationExecutionStatus status,
        out VerificationExecutionOutcome? outcome)
    {
        status = default;
        outcome = null;
        if (row.StatusCode is < 1 or > 8
            || row.OutcomeCode is < 0 or > 4
            || row.ClaimFingerprintOversized
            || row.CompletionFingerprintOversized
            || !ProjectVerificationInspectionStorage.IsFingerprint(row.ClaimFingerprint)
            || row.CompletionFingerprintPresent != (row.CompletionFingerprint is not null)
            || (row.CompletionFingerprintPresent && !ProjectVerificationInspectionStorage.IsFingerprint(row.CompletionFingerprint))
            || row.ExitCodePresent != (row.ExitCode is not null)
            || row.DispatchedPresent != (dispatchedAtUtc is not null)
            || row.CompletedPresent != (completedAtUtc is not null)
            || (dispatchedAtUtc is { } dispatched && dispatched < claimedAtUtc)
            || (completedAtUtc is { } completed && completed < (dispatchedAtUtc ?? claimedAtUtc)))
        {
            return false;
        }

        status = (VerificationExecutionStatus)(row.StatusCode - 1);
        outcome = row.OutcomeCode == 0 ? null : (VerificationExecutionOutcome)(row.OutcomeCode - 1);
        var dispatchedAt = dispatchedAtUtc is not null;
        var completedAt = completedAtUtc is not null;
        var sameFingerprint = row.CompletionFingerprintPresent
            && string.Equals(row.CompletionFingerprint, row.ClaimFingerprint, StringComparison.Ordinal);
        var exitCode = row.ExitCode;

        return status switch
        {
            VerificationExecutionStatus.Running =>
                !completedAt && outcome is null && exitCode is null && !row.CompletionFingerprintPresent,
            VerificationExecutionStatus.Passed =>
                dispatchedAt && completedAt && sameFingerprint && outcome == VerificationExecutionOutcome.Exited && exitCode == 0,
            VerificationExecutionStatus.Failed =>
                dispatchedAt && completedAt && sameFingerprint
                && ((outcome == VerificationExecutionOutcome.Exited && exitCode is not null && exitCode != 0)
                    || (outcome == VerificationExecutionOutcome.Failed && exitCode is null)),
            VerificationExecutionStatus.TimedOut =>
                dispatchedAt && completedAt && sameFingerprint && outcome == VerificationExecutionOutcome.TimedOut && exitCode is null,
            VerificationExecutionStatus.Cancelled =>
                dispatchedAt && completedAt && sameFingerprint && outcome == VerificationExecutionOutcome.Cancelled && exitCode is null,
            VerificationExecutionStatus.Interrupted =>
                completedAt && outcome is null && exitCode is null && !row.CompletionFingerprintPresent,
            VerificationExecutionStatus.SourceChanged =>
                completedAt
                && row.CompletionFingerprintPresent
                && !sameFingerprint
                && (dispatchedAt
                    ? outcome is not null && (outcome == VerificationExecutionOutcome.Exited) == (exitCode is not null)
                    : outcome is null && exitCode is null),
            _ => !dispatchedAt && completedAt && outcome is null && exitCode is null && !row.CompletionFingerprintPresent,
        };
    }
}
