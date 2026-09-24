using System.Collections.Frozen;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The single Domain rule relating host-measured <see cref="AgentProcessExecutionEvidence"/> to an
/// Agent attempt's dispatch state and requested terminal <see cref="AgentOutcome"/>. Enforced by
/// every Agent completion transition on <see cref="Attempt"/> and evaluated in advance by each
/// result-recording Application handler so a violation fails closed without mutation.
/// </summary>
public static class AgentProcessEvidencePolicy
{
    /// <summary>Every outcome that can only be reached after a provider process ran to completion
    /// and exited zero: each contract's semantic success outcome, <see cref="AgentOutcome.InvalidStructuredOutput"/>,
    /// and the "process succeeded but nothing to record" and "process succeeded but HEAD moved
    /// unexpectedly" classifications for the two WorkspaceMutating contracts
    /// (<see cref="AgentOutcome.NoChangesProduced"/>, <see cref="AgentOutcome.ImplementationHeadChanged"/>,
    /// <see cref="AgentOutcome.CorrectionNoChangesProduced"/>, <see cref="AgentOutcome.CorrectionHeadChanged"/>).
    /// Every one of these is only ever classified by its recording handler when the reported process
    /// outcome already asserted a clean exit — see <c>RecordImplementationResultCommandHandler.Classify</c>
    /// and <c>RecordReviewCorrectionResultCommandHandler.Classify</c> — so each requires evidence
    /// proving exactly that.</summary>
    private static readonly FrozenSet<AgentOutcome> CleanExitRequiredOutcomes = BuildCleanExitRequiredOutcomes();

    /// <summary>Every outcome that is, by its own Domain meaning, always detected immediately
    /// before the provider is ever invoked — see each value's own XML doc on <see cref="AgentOutcome"/>.
    /// No child process can exist for any of these, so none may ever carry process evidence,
    /// regardless of the attempt's dispatch state. Deliberately excludes
    /// <see cref="AgentOutcome.SourceChanged"/>, which — unlike this set — can be detected either
    /// before or after a real provider invocation.</summary>
    private static readonly FrozenSet<AgentOutcome> PreInvocationOutcomes = new HashSet<AgentOutcome>
    {
        AgentOutcome.WorkspaceNoLongerEligible,
        AgentOutcome.InputAlreadyReviewed,
        AgentOutcome.InputAlreadyResolved,
        AgentOutcome.InputAlreadyImplemented,
        AgentOutcome.InputAlreadyCodeReviewed,
        AgentOutcome.InputAlreadyCorrected,
    }.ToFrozenSet();

    public static bool RequiresCleanExit(AgentOutcome outcome) => CleanExitRequiredOutcomes.Contains(outcome);

    public static bool IsPreInvocationOutcome(AgentOutcome outcome) => PreInvocationOutcomes.Contains(outcome);

    /// <summary>Returns the first violation, or <see langword="null"/> when the combination is
    /// valid. A pre-invocation outcome may never carry evidence at all, checked before every other
    /// rule since it applies independently of dispatch state. Otherwise, evidence may accompany
    /// any outcome of a dispatched attempt (a timeout, cancellation, or non-zero exit is truthful
    /// evidence for a failure outcome); it may never accompany an undispatched attempt, and a
    /// clean-exit-required outcome may never be recorded without clean exit evidence.</summary>
    public static AgentProcessEvidenceViolation? Evaluate(
        AgentOutcome requestedOutcome, bool dispatched, AgentProcessExecutionEvidence? evidence)
    {
        if (evidence is not null && IsPreInvocationOutcome(requestedOutcome))
        {
            return AgentProcessEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence;
        }

        if (evidence is not null && !dispatched)
        {
            return AgentProcessEvidenceViolation.NotDispatched;
        }

        if (RequiresCleanExit(requestedOutcome) && evidence is not { IsCleanExit: true })
        {
            return AgentProcessEvidenceViolation.CleanExitRequired;
        }

        return null;
    }

    private static FrozenSet<AgentOutcome> BuildCleanExitRequiredOutcomes()
    {
        var outcomes = new HashSet<AgentOutcome>
        {
            AgentOutcome.InvalidStructuredOutput,
            AgentOutcome.NoChangesProduced,
            AgentOutcome.ImplementationHeadChanged,
            AgentOutcome.CorrectionNoChangesProduced,
            AgentOutcome.CorrectionHeadChanged,
        };
        outcomes.UnionWith(AgentAttemptContract.CompletedOutcomesForEffect(AgentEffectKind.ReadOnly));
        outcomes.UnionWith(AgentAttemptContract.CompletedOutcomesForEffect(AgentEffectKind.WorkspaceMutating));
        return outcomes.ToFrozenSet();
    }
}
