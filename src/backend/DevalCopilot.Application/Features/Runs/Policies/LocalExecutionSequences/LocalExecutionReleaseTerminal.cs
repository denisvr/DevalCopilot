using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// ADR-0065: which terminal results of a dispatched local execution operation can be retained quarantines that a Human may release,
/// derived from the existing Domain result policies and never from membership in a terminal-status vocabulary. An operation whose
/// ordinary host Exited proof is absent ended with a timeout or cancellation of its owned process (a clean exit is exactly the proof that
/// already releases it), so its recorded result must be a failure that the existing policies allow to carry that process result: an Agent
/// attempt is Failed with an outcome that does not require a clean exit and is not a pre-invocation classification
/// (<see cref="AgentProcessEvidencePolicy"/>), and a verification execution's status follows its outcome and completion fingerprint exactly
/// as <c>VerificationExecution.Complete</c> derives them. The shared ownership definition, the release reading, the locked command and
/// the database fence triggers (whose vocabulary is the frozen copy in the migration) all decide by this one meaning.
/// </summary>
internal static class LocalExecutionReleaseTerminal
{
    /// <summary>The Agent outcomes a Failed dispatched attempt can carry together with a timed-out or cancelled process: every defined
    /// outcome except the clean-exit-required ones and the pre-invocation classifications.</summary>
    public static IReadOnlyList<AgentOutcome> AgentOutcomes { get; } = Enum.GetValues<AgentOutcome>()
        .Where(outcome => !AgentProcessEvidencePolicy.RequiresCleanExit(outcome) && !AgentProcessEvidencePolicy.IsPreInvocationOutcome(outcome))
        .ToArray();

    /// <summary>Every defined Agent outcome; a stored value outside it is unrecognized.</summary>
    public static IReadOnlyList<AgentOutcome> DefinedAgentOutcomes { get; } = Enum.GetValues<AgentOutcome>();
}
