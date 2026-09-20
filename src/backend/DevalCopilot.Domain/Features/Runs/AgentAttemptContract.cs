using System.Collections.Frozen;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed, Domain-owned semantic contract table for an Agent attempt, per ADR-0009 and ADR-0010:
/// response contract, workflow role, execution effect, and the outcomes that produce
/// <see cref="AttemptStatus.Completed"/>. Provider identity is deliberately absent — it is
/// execution provenance, never part of this semantic contract. Keyed by <see cref="AgentResponseContract"/>,
/// resolved by <see cref="For"/>; never caller-constructed. AgentRole remains the sole collaboration-authority
/// dimension; multiple response contracts (e.g. ImplementationReport and ReviewCorrection) may belong
/// to AgentRole.Implementer.
/// </summary>
public sealed class AgentAttemptContract
{
    private static readonly FrozenDictionary<AgentResponseContract, AgentAttemptContract> ByResponseContract = BuildContracts();

    private AgentAttemptContract(
        AgentResponseContract responseContract,
        AgentRole role,
        AgentEffectKind effect,
        IReadOnlySet<AgentOutcome> completedOutcomes)
    {
        ResponseContract = responseContract;
        Role = role;
        Effect = effect;
        CompletedOutcomes = completedOutcomes;
    }

    public AgentResponseContract ResponseContract { get; }

    public AgentRole Role { get; }

    public AgentEffectKind Effect { get; }

    /// <summary>The closed set of <see cref="AgentOutcome"/> values that produce
    /// <see cref="AttemptStatus.Completed"/> for this contract — immutable and caller-safe. An
    /// outcome's absence from this set means only that it does not produce Completed status for
    /// this contract; it does not, by itself, authorize that outcome for this contract at all.
    /// Shared failure classifications (SourceChanged, InvalidStructuredOutput,
    /// ProviderInvocationFailed, CheckpointEvidenceUnavailable, WorkspaceNoLongerEligible),
    /// role-specific failures, and each role's own dedicated pre-dispatch race classification
    /// (e.g. an "input already handled" outcome) remain validated by their owning Application
    /// handlers and Domain transitions — <see cref="AgentAttemptContract"/> does not yet
    /// centralize the complete allowed-failure matrix.</summary>
    public IReadOnlySet<AgentOutcome> CompletedOutcomes { get; }

    /// <summary>Resolves the one fixed contract for a response contract. Fails closed — throws rather than
    /// returning a default — for any <see cref="AgentResponseContract"/> this contract table does not
    /// recognize.</summary>
    public static AgentAttemptContract For(AgentResponseContract responseContract) =>
        ByResponseContract.TryGetValue(responseContract, out var contract)
            ? contract
            : throw new ArgumentOutOfRangeException(nameof(responseContract), responseContract, "No agent attempt contract is defined for this response contract.");

    /// <summary>Every defined <see cref="AgentRole"/> whose contract has the given effect —
    /// immutable and caller-safe. Fails closed for an undefined <see cref="AgentEffectKind"/>
    /// rather than silently returning an empty or partial set.</summary>
    public static IReadOnlySet<AgentRole> RolesForEffect(AgentEffectKind effect)
    {
        if (!Enum.IsDefined(effect))
        {
            throw new ArgumentOutOfRangeException(nameof(effect), effect, "Not a defined agent effect kind.");
        }

        return ByResponseContract.Values
            .Where(contract => contract.Effect == effect)
            .Select(contract => contract.Role)
            .ToFrozenSet();
    }

    /// <summary>The union of every completed-outcome value across every contract sharing the given
    /// effect — immutable and caller-safe. Fails closed for an undefined <see cref="AgentEffectKind"/>.
    /// Used by <see cref="Attempt.CompleteAgent"/> to recognize a caller-claimed success outcome
    /// before checking whether it also matches this specific attempt's own contract.</summary>
    public static IReadOnlySet<AgentOutcome> CompletedOutcomesForEffect(AgentEffectKind effect)
    {
        if (!Enum.IsDefined(effect))
        {
            throw new ArgumentOutOfRangeException(nameof(effect), effect, "Not a defined agent effect kind.");
        }

        return ByResponseContract.Values
            .Where(contract => contract.Effect == effect)
            .SelectMany(contract => contract.CompletedOutcomes)
            .ToFrozenSet();
    }

    private static FrozenDictionary<AgentResponseContract, AgentAttemptContract> BuildContracts()
    {
        AgentAttemptContract[] contracts =
        [
            new(AgentResponseContract.Proposal, AgentRole.Planner, AgentEffectKind.ReadOnly,
                new HashSet<AgentOutcome> { AgentOutcome.Proposed }.ToFrozenSet()),
            new(AgentResponseContract.CriticalReview, AgentRole.CriticalReviewer, AgentEffectKind.ReadOnly,
                new HashSet<AgentOutcome> { AgentOutcome.Accepted, AgentOutcome.Challenged }.ToFrozenSet()),
            new(AgentResponseContract.ChallengeResolution, AgentRole.Resolver, AgentEffectKind.ReadOnly,
                new HashSet<AgentOutcome> { AgentOutcome.Resolved }.ToFrozenSet()),
            new(AgentResponseContract.ImplementationReport, AgentRole.Implementer, AgentEffectKind.WorkspaceMutating,
                new HashSet<AgentOutcome> { AgentOutcome.Implemented }.ToFrozenSet()),
            new(AgentResponseContract.ImplementationReview, AgentRole.CodeReviewer, AgentEffectKind.ReadOnly,
                new HashSet<AgentOutcome> { AgentOutcome.ReviewApproved, AgentOutcome.ReviewChangesRequested }.ToFrozenSet()),
            new(AgentResponseContract.ReviewCorrection, AgentRole.Implementer, AgentEffectKind.WorkspaceMutating,
                new HashSet<AgentOutcome> { AgentOutcome.CorrectionApplied }.ToFrozenSet()),
        ];

        return contracts.ToFrozenDictionary(contract => contract.ResponseContract);
    }
}
