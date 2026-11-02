using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.EnvironmentReadiness.Commands.EnsureHostCapabilityCatalogSeeded;
using DevalCopilot.Application.Features.EnvironmentReadiness.Commands.ReconcileInterruptedHostCapabilityProbes;
using DevalCopilot.Application.Features.Projects.Commands.ReconcileInterruptedVerificationExecutions;
using DevalCopilot.Application.Features.Projects.Commands.ReconcileWorkspaces;
using DevalCopilot.Application.Features.Projects.Commands.RecoverInterruptedVerificationOutputArtifacts;
using DevalCopilot.Application.Features.Runs.Commands.CloseInterruptedAgentSupervisions;
using DevalCopilot.Application.Features.Runs.Commands.InterruptLocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Commands.InterruptPlanningSequences;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedAgentAttempts;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedImplementationAttempts;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedProcessAttempts;
using DevalCopilot.Application.Features.Runs.Commands.RecoverLocalCommitOperations;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Api.Startup;

/// <summary>
/// ADR-0072: the host's startup recovery, in its existing order and with its existing idempotent steps, now checked. Every required
/// Result and every meaningful payload is inspected: a database, schema, query, transaction or commit fault, a failed or incoherent
/// safety-critical reconciliation (sequences, attempts, verification, workspaces, local commit, capability catalog and probes) or any
/// unclassified exception is fatal and stops at once, so no later step, API, Ready line or hosted dispatch follows. A coherent success
/// that records NeedsAttention, Interrupted or held ownership is not a failure: those holds are the existing state. Only per-item
/// optional historical output that could not be sealed or imported, deferred removal of a proved-redundant partial and supervision
/// observations a previous launch left unclosed are warnings, and only because every transaction and safety-critical step before and
/// after them was proven. Warnings create no evidence, readiness or release. Nothing is retried, repaired, adopted or released here.
/// </summary>
public static class StartupRecovery
{
    public static async Task<StartupRecoveryReport> RunAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        var warnings = new SortedSet<StartupWarningCategory>();
        var stage = StartupFailureStage.StorageSchema;
        try
        {
            using var startupScope = services.CreateScope();
            var dbContext = startupScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.MigrateAsync(cancellationToken);

            var mediator = startupScope.ServiceProvider.GetRequiredService<IApplicationMediator>();

            // ADR-0060: close every previous-launch local execution sequence before any other recovery.
            stage = StartupFailureStage.LocalExecutionSequences;
            var sequences = Require(await mediator.SendAsync(new InterruptLocalExecutionSequencesCommand(), cancellationToken));
            RequireCoherent(sequences.InterruptedCount >= 0 && sequences.RefusedUndispatchedCount >= 0);

            stage = StartupFailureStage.ProcessAttemptOutput;
            AddWarnings(warnings, await ProcessAttemptOutputRecovery.RunAsync(startupScope.ServiceProvider, logger, cancellationToken));

            stage = StartupFailureStage.ProcessAttempts;
            RequireCount(await mediator.SendAsync(new ReconcileInterruptedProcessAttemptsCommand(), cancellationToken));

            stage = StartupFailureStage.CapabilityCatalog;
            RequireCount(await mediator.SendAsync(new EnsureHostCapabilityCatalogSeededCommand(), cancellationToken));

            stage = StartupFailureStage.CapabilityProbes;
            RequireCount(await mediator.SendAsync(new ReconcileInterruptedHostCapabilityProbesCommand(), cancellationToken));

            stage = StartupFailureStage.LocalCommitOperations;
            RequireCount(await mediator.SendAsync(new RecoverLocalCommitOperationsCommand(), cancellationToken));

            stage = StartupFailureStage.Workspaces;
            RequireCount(await mediator.SendAsync(new ReconcileWorkspacesCommand(), cancellationToken));

            stage = StartupFailureStage.VerificationOutput;
            var verificationOutput = Require(await mediator.SendAsync(new RecoverInterruptedVerificationOutputArtifactsCommand(), cancellationToken));
            RequireCoherent(verificationOutput.ImportedCount >= 0);
            if (verificationOutput.OptionalOutputDeferred)
            {
                warnings.Add(StartupWarningCategory.OptionalOutputDeferred);
            }

            stage = StartupFailureStage.VerificationExecutions;
            RequireCount(await mediator.SendAsync(new ReconcileInterruptedVerificationExecutionsCommand(), cancellationToken));

            stage = StartupFailureStage.AgentAttemptOutput;
            AddWarnings(warnings, await AgentAttemptOutputRecovery.RunAsync(startupScope.ServiceProvider, logger, cancellationToken));

            stage = StartupFailureStage.AgentAttempts;
            RequireCount(await mediator.SendAsync(new ReconcileInterruptedAgentAttemptsCommand(), cancellationToken));

            stage = StartupFailureStage.ImplementationAttempts;
            RequireCount(await mediator.SendAsync(new ReconcileInterruptedImplementationAttemptsCommand(), cancellationToken));

            stage = StartupFailureStage.PlanningSequences;
            var planning = Require(await mediator.SendAsync(new InterruptPlanningSequencesCommand(), cancellationToken));
            RequireCoherent(planning.InterruptedCount >= 0);

            stage = StartupFailureStage.AgentSupervisions;
            var supervisions = Require(await mediator.SendAsync(new CloseInterruptedAgentSupervisionsCommand(), cancellationToken));
            RequireCoherent(supervisions.ClosedCount >= 0 && supervisions.UnclosedCount >= 0);
            if (supervisions.UnclosedCount > 0)
            {
                warnings.Add(StartupWarningCategory.SupervisionObservationsUnclosed);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var failure = new StartupFailure(stage, Classify(exception));

            // Only closed values: never the exception object, its message or any path.
            logger.LogCritical("startup_recovery_failed Diagnostic={Diagnostic}", failure.Code);
            return new StartupRecoveryReport(failure, []);
        }

        foreach (var warning in warnings)
        {
            logger.LogWarning("startup_recovery_warning Category={Category}", warning);
        }

        return new StartupRecoveryReport(null, warnings.ToArray());
    }

    private static StartupFailureReason Classify(Exception exception) => exception switch
    {
        StartupStateIncoherentException => StartupFailureReason.StateIncoherent,
        _ when StorageFailureClassifier.IsStorageUnavailable(exception) => StartupFailureReason.StorageUnavailable,
        _ when StorageFailureClassifier.IsDatabaseError(exception) => StartupFailureReason.StateIncoherent,
        _ => StartupFailureReason.Unclassified,
    };

    private static T Require<T>(Result<T> result) =>
        result.IsSuccess ? result.Value : throw new StartupStateIncoherentException();

    private static void RequireCount(Result<int> result) => RequireCoherent(Require(result) >= 0);

    private static void RequireCoherent(bool coherent)
    {
        if (!coherent)
        {
            throw new StartupStateIncoherentException();
        }
    }

    private static void AddWarnings(SortedSet<StartupWarningCategory> warnings, OutputRecoveryOutcome outcome)
    {
        if (outcome.OptionalOutputDeferred)
        {
            warnings.Add(StartupWarningCategory.OptionalOutputDeferred);
        }

        if (outcome.RedundantCleanupDeferred)
        {
            warnings.Add(StartupWarningCategory.RedundantCleanupDeferred);
        }
    }

    /// <summary>A failed or incoherent safety-critical result; carries no detail.</summary>
    private sealed class StartupStateIncoherentException : Exception;
}
