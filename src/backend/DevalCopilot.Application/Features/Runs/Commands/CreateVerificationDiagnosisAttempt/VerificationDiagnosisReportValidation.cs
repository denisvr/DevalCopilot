using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;

/// <summary>
/// Validates every fact a verification diagnosis requires about the named ExecutionReport before it is claimed: it belongs
/// to the run, is a provider-observed Implementer ExecutionReport, its owning attempt's real result checkpoint is exactly the
/// workspace's current checkpoint, and its whole Implementer chain (initial or corrected, through the validated lineage with
/// the true ImplementedPlan) is valid. Mirrors the ordinary code review's validation without sharing a private method, so
/// the ordinary claim path stays byte-for-byte unchanged; reads are untracked when asked, so a claim's authority re-read
/// reflects the database now.
/// </summary>
internal static class VerificationDiagnosisReportValidation
{
    internal sealed record Outcome(ImplementerExecutionReportEligibility.Result? Value, Error? Error)
    {
        public static Outcome Succeeded(ImplementerExecutionReportEligibility.Result value) => new(value, null);

        public static Outcome Failed(Error error) => new(null, error);
    }

    public static async Task<Outcome> ValidateAsync(
        IDevalCopilotDbContext dbContext,
        Guid runId,
        Guid executionReportMessageId,
        Guid workspaceId,
        GitCheckpoint resultCheckpoint,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        var messages = asNoTracking ? dbContext.CollaborationMessages.AsNoTracking() : dbContext.CollaborationMessages;
        var message = await messages.SingleOrDefaultAsync(candidate => candidate.Id == executionReportMessageId, cancellationToken);
        if (message is null || message.RunId != runId)
        {
            return Outcome.Failed(Error.NotFound(
                "agent_attempts.execution_report_not_found", "The requested execution report was not found for this run."));
        }

        if (message.Type != CollaborationMessageType.ExecutionReport)
        {
            return Outcome.Failed(Error.Conflict(
                "agent_attempts.not_provider_observed_execution_report",
                "Only a provider-observed Implementer execution report can be requested for a verification diagnosis."));
        }

        var owningAttempt = await AgentAuthoredMessageEligibility.ResolveOwningAttemptAsync(
            dbContext, message, runId, AgentRole.Implementer, cancellationToken, asNoTracking);
        if (owningAttempt is not null
            && (owningAttempt.AgentGitWorkspaceId != workspaceId || owningAttempt.AgentResultGitCheckpointId != resultCheckpoint.Id))
        {
            return Outcome.Failed(Error.Conflict(
                "agent_attempts.result_checkpoint_mismatch",
                "The execution report's result checkpoint is not the workspace's exact current checkpoint."));
        }

        var validation = await ImplementerExecutionReportEligibility.ResolveAsync(
            dbContext, message, runId, workspaceId, resultCheckpoint.Id, cancellationToken);
        return validation is null
            ? Outcome.Failed(Error.Conflict(
                "agent_attempts.implementer_attempt_not_valid",
                "The execution report's Implementer result chain is not valid for a diagnosis."))
            : Outcome.Succeeded(validation);
    }
}
