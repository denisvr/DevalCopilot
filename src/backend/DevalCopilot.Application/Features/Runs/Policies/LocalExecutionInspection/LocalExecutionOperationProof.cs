using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationEvidence;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>
/// Proves that a step's recorded link names exactly its operation (ADR-0061), from bounded scalar reads of that one operation. An Agent
/// link must be the exact Agent attempt of this run, recorded against the pinned workspace, the step's source checkpoint and fingerprint
/// and the contract of the step's kind, with the coherent role, provider and contract identity the existing selected-attempt readers
/// admit; only their immutable header is returned. A verification link must be the exact execution of this project and pinned workspace
/// against the step's source, equal to the step's pinned recipe in snapshot name, executable, exact ordered arguments and timeout, with a
/// status, process outcome, time and fingerprint shape the Domain can produce. Anything else is null: never a different operation or a
/// partial answer. The pinned recipe's arguments and path are read only for the selected step and only within their internal byte guards.
/// </summary>
internal static class LocalExecutionOperationProof
{
    private const int MaximumNameLength = 200;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async Task<AgentAttemptVerificationEvidenceHeader?> ProveAgentAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionInspectionStructure structure,
        LocalExecutionInspectionStep step,
        CancellationToken cancellationToken)
    {
        if (!LocalExecutionSequencePolicy.IsAgentKind(step.Kind)
            || step.OperationId is not { } attemptId
            || LocalExecutionSequencePolicy.ContractOf(step.Kind) is not { } contract)
        {
            return null;
        }

        var runId = structure.RunId;
        var attempt = await dbContext.Attempts.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.Id == attemptId)
            .ProjectScalarIdentity()
            .FirstOrDefaultAsync(cancellationToken);
        if (attempt is null || !attempt.IsAgent)
        {
            return null;
        }

        var identity = ScalarAgentAttemptIdentity.Evaluate(attempt);
        if (identity is null || identity.ResponseContract != contract)
        {
            return null;
        }

        var ownership = await dbContext.Attempts.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.Id == attemptId)
            .ProjectVerificationOwnership(dbContext.Runs.AsNoTracking())
            .FirstOrDefaultAsync(cancellationToken);
        if (ownership is not { FingerprintOversized: false }
            || ownership.ProjectId != structure.ProjectId
            || ownership.WorkspaceId != structure.Header.WorkspaceId
            || ownership.CheckpointId != step.SourceCheckpointId
            || !string.Equals(ownership.Fingerprint, step.SourceFingerprintSha256, StringComparison.Ordinal))
        {
            return null;
        }

        return new AgentAttemptVerificationEvidenceHeader(
            attempt.Id, attempt.AttemptNumber, identity.Role, identity.Provider, identity.ResponseContract);
    }

    public static async Task<LocalExecutionVerificationFacts?> ProveVerificationAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionInspectionStructure structure,
        LocalExecutionInspectionStep step,
        CancellationToken cancellationToken)
    {
        if (step.Kind != LocalExecutionStepKind.Verification
            || step.OperationId is not { } executionId
            || step.RecipeOrdinal is not { } recipeOrdinal)
        {
            return null;
        }

        var executions = await dbContext.VerificationExecutions.AsNoTracking()
            .Where(candidate => candidate.Id == executionId)
            .Take(2)
            .ProjectExecution()
            .ToArrayAsync(cancellationToken);
        var sequenceId = structure.Header.SequenceId;
        var recipes = await dbContext.LocalExecutionSequenceRecipes.AsNoTracking()
            .Where(recipe => recipe.SequenceId == sequenceId && recipe.Ordinal == recipeOrdinal)
            .Take(2)
            .ProjectSelectedRecipe()
            .ToArrayAsync(cancellationToken);
        if (executions.Length != 1 || recipes.Length != 1)
        {
            return null;
        }

        var execution = executions[0];
        var recipe = recipes[0];
        if (execution.Id != executionId
            || execution.ProjectId != structure.ProjectId
            || execution.WorkspaceId != structure.Header.WorkspaceId
            || execution.CheckpointId != step.SourceCheckpointId
            || execution.ClaimFingerprintOversized
            || !string.Equals(execution.ClaimFingerprint, step.SourceFingerprintSha256, StringComparison.Ordinal)
            || execution.Number is not { } number
            || !IsName(execution.CommandName, execution.CommandNameOversized)
            || !IsName(recipe.Name, recipe.NameOversized)
            || !string.Equals(execution.CommandName, recipe.Name, StringComparison.Ordinal)
            || execution.CommandId != recipe.VerificationCommandId
            || execution.TimeoutSeconds is not { } timeout
            || recipe.TimeoutSeconds != timeout
            || execution.ExecutablePathOversized
            || recipe.ExecutablePathOversized
            || execution.ExecutablePath is null
            || !string.Equals(execution.ExecutablePath, recipe.ExecutablePath, StringComparison.Ordinal)
            || !ArgumentsMatch(execution, recipe)
            || !TryClassifyTerminal(execution, out var status, out var outcome))
        {
            return null;
        }

        return new LocalExecutionVerificationFacts(
            executionId,
            number,
            execution.CommandName!,
            status,
            outcome,
            execution.ExitCodePresent ? execution.ExitCode : null,
            execution.ClaimedAtUtc,
            execution.DispatchedAtUtc,
            execution.CompletedAtUtc);
    }

    private static bool IsName(string? value, bool oversized) =>
        !oversized && !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumNameLength;

    /// <summary>Exact, literal and ordered: the execution's snapshot arguments (stored as an empty text when there are none) and the pinned
    /// recipe's JSON array parse to the same sequence of values, each within the recipe bounds of 64 members and 4096 UTF-8 bytes.</summary>
    private static bool ArgumentsMatch(LocalExecutionInspectionExecutionRow execution, LocalExecutionInspectionSelectedRecipeRow recipe)
    {
        if (execution.ArgumentsOversized || execution.ArgumentsUtf8 is null || recipe.ArgumentsJsonOversized || recipe.ArgumentsJson is null)
        {
            return false;
        }

        string executionJson;
        try
        {
            executionJson = StrictUtf8.GetString(execution.ArgumentsUtf8);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        var snapshot = executionJson.Length == 0 ? [] : ParseArguments(executionJson);
        var pinned = ParseArguments(recipe.ArgumentsJson);
        return snapshot is not null && pinned is not null && snapshot.SequenceEqual(pinned, StringComparer.Ordinal);
    }

    private static string[]? ParseArguments(string json)
    {
        string[]? arguments;
        try
        {
            arguments = JsonSerializer.Deserialize<string[]>(json);
        }
        catch (JsonException)
        {
            return null;
        }

        return arguments is { Length: <= VerificationCommand.MaximumArgumentCount }
            && arguments.All(argument => argument is not null
                && Encoding.UTF8.GetByteCount(argument) <= VerificationCommand.MaximumArgumentUtf8Bytes)
            ? arguments
            : null;
    }

    /// <summary>The recorded status, outcome, exit code, time and fingerprint shapes the Domain transitions can produce; any other
    /// combination, an unknown enum or a rejected stored number is contradictory. Nothing is promoted to a clean exit.</summary>
    private static bool TryClassifyTerminal(
        LocalExecutionInspectionExecutionRow execution, out VerificationExecutionStatus status, out VerificationExecutionOutcome? outcome)
    {
        status = default;
        outcome = null;
        if (execution.StatusCode is < 1 or > 8
            || execution.OutcomeCode is < 0 or > 4
            || execution.CompletionFingerprintOversized
            || execution.ExitCodePresent != (execution.ExitCode is not null)
            || !IsUtc(execution.ClaimedAtUtc)
            || (execution.DispatchedAtUtc is { } dispatched && (!IsUtc(dispatched) || dispatched < execution.ClaimedAtUtc))
            || (execution.CompletedAtUtc is { } completed
                && (!IsUtc(completed) || completed < (execution.DispatchedAtUtc ?? execution.ClaimedAtUtc))))
        {
            return false;
        }

        status = (VerificationExecutionStatus)(execution.StatusCode - 1);
        outcome = execution.OutcomeCode == 0 ? null : (VerificationExecutionOutcome)(execution.OutcomeCode - 1);
        var dispatchedAt = execution.DispatchedAtUtc is not null;
        var completedAt = execution.CompletedAtUtc is not null;
        var sameFingerprint = execution.CompletionFingerprintPresent
            && string.Equals(execution.CompletionFingerprint, execution.ClaimFingerprint, StringComparison.Ordinal);
        var exitCode = execution.ExitCode;

        switch (status)
        {
            case VerificationExecutionStatus.Running:
                return !completedAt && outcome is null && exitCode is null && !execution.CompletionFingerprintPresent;
            case VerificationExecutionStatus.Passed:
                return dispatchedAt && completedAt && sameFingerprint && outcome == VerificationExecutionOutcome.Exited && exitCode == 0;
            case VerificationExecutionStatus.Failed:
                return dispatchedAt && completedAt && sameFingerprint
                    && ((outcome == VerificationExecutionOutcome.Exited && exitCode is not null && exitCode != 0)
                        || (outcome == VerificationExecutionOutcome.Failed && exitCode is null));
            case VerificationExecutionStatus.TimedOut:
                return dispatchedAt && completedAt && sameFingerprint && outcome == VerificationExecutionOutcome.TimedOut && exitCode is null;
            case VerificationExecutionStatus.Cancelled:
                return dispatchedAt && completedAt && sameFingerprint && outcome == VerificationExecutionOutcome.Cancelled && exitCode is null;
            case VerificationExecutionStatus.Interrupted:
                return completedAt && outcome is null && exitCode is null && !execution.CompletionFingerprintPresent;
            case VerificationExecutionStatus.SourceChanged:
                // Either invalidated before dispatch (no process outcome) or the dispatched process completed against a different source.
                return completedAt
                    && execution.CompletionFingerprintPresent
                    && LocalExecutionSequencePolicy.IsLowercaseSha256(execution.CompletionFingerprint)
                    && !sameFingerprint
                    && (dispatchedAt
                        ? outcome is not null && (outcome == VerificationExecutionOutcome.Exited) == (exitCode is not null)
                        : outcome is null && exitCode is null);
            default:
                // The dispatch refusal: terminal before any dispatch, with no process outcome, exit code or completion fingerprint.
                return !dispatchedAt && completedAt && outcome is null && exitCode is null && !execution.CompletionFingerprintPresent;
        }
    }

    private static bool IsUtc(DateTimeOffset value) => value != default && value.Offset == TimeSpan.Zero;
}
