using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationEvidence;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

/// <summary>
/// Proves that a step's recorded link names exactly its Agent attempt (ADR-0068), from bounded scalar reads of that one attempt: the
/// exact attempt of this run with the step's reserved identity, an Agent kind, the coherent role, provider and response contract the
/// existing selected-attempt readers admit and ADR-0055's fixed table assigns to the step's ordinal, and the workspace, checkpoint and
/// fingerprint recorded at claim equal to the sequence's pin. The attempt's pin and its three time columns are read first as raw bytes and
/// admitted by the one storage parser, and the attempt number is admitted in the same statement as an exact stored INTEGER from 1 to
/// int.MaxValue, so the shared identity projection (which converts those columns) never meets unsupported storage. Only the
/// existing immutable header is returned. A Running, failed, refused, interrupted or no-output attempt is as inspectable as any other when
/// this association is coherent: no status, outcome, message or dispatch marker is required or reported. Anything else is null: never a
/// different attempt, a later manual attempt or a partial answer.
/// </summary>
internal static class PlanningSequenceInspectionAgentProof
{
    public static async Task<AgentAttemptVerificationEvidenceHeader?> ProveAsync(
        IDevalCopilotDbContext dbContext,
        PlanningSequenceInspectionStructure structure,
        PlanningSequenceInspectionStep step,
        CancellationToken cancellationToken)
    {
        if (step.AttemptId is not { } attemptId)
        {
            return null;
        }

        var runId = structure.RunId;
        var header = structure.Header;
        var pin = await dbContext.Attempts.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.Id == attemptId)
            .ProjectAttemptPin()
            .FirstOrDefaultAsync(cancellationToken);
        if (pin is null
            || pin.FingerprintOversized
            || !pin.WorkspacePresent
            || !pin.CheckpointPresent
            || PlanningSequenceInspectionStorage.ParseIdentifier(pin.WorkspaceId) != header.WorkspaceId
            || PlanningSequenceInspectionStorage.ParseIdentifier(pin.CheckpointId) != header.CheckpointId
            || !string.Equals(pin.Fingerprint, header.FingerprintSha256, StringComparison.Ordinal)
            || PlanningSequenceInspectionStorage.ParseUtc(pin.ClaimedAtUtc) is null
            || (pin.CompletedPresent && PlanningSequenceInspectionStorage.ParseUtc(pin.CompletedAtUtc) is null)
            || (pin.DispatchedPresent && PlanningSequenceInspectionStorage.ParseUtc(pin.DispatchedAtUtc) is null))
        {
            return null;
        }

        var attempt = await dbContext.Attempts.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.Id == attemptId)
            .WhereAttemptNumberSupported()
            .ProjectScalarIdentity()
            .FirstOrDefaultAsync(cancellationToken);
        if (attempt is null || !attempt.IsAgent)
        {
            return null;
        }

        var identity = ScalarAgentAttemptIdentity.Evaluate(attempt);
        if (identity is null
            || identity.Role != step.Role
            || identity.Provider != step.Provider
            || identity.ResponseContract != step.ResponseContract)
        {
            return null;
        }

        return new AgentAttemptVerificationEvidenceHeader(
            attempt.Id, attempt.AttemptNumber, identity.Role, identity.Provider, identity.ResponseContract);
    }
}
