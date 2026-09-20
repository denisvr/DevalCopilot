using System.Collections.Frozen;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed, Domain-owned semantic contract for an Agent attempt's workflow role, per ADR-0009:
/// workflow role, response contract, execution effect, and the outcomes that produce
/// <see cref="AttemptStatus.Completed"/>. Provider identity is deliberately absent — it is
/// execution provenance, never part of this semantic contract. Exactly one instance per
/// <see cref="AgentRole"/>, resolved by <see cref="For"/>; never caller-constructed. Adding a role
/// requires adding its contract to <see cref="BuildContracts"/> in the same change — <see cref="For"/>
/// throws for any <see cref="AgentRole"/> it does not recognize, so a new role can never silently
/// fall through with an assumed effect.
/// </summary>
public sealed class AgentAttemptContract
{
    private static readonly FrozenDictionary<AgentRole, AgentAttemptContract> ByRole = BuildContracts();

    private AgentAttemptContract(
        AgentRole role,
        AgentResponseContract responseContract,
        AgentEffectKind effect,
        IReadOnlySet<AgentOutcome> completedOutcomes)
    {
        Role = role;
        ResponseContract = responseContract;
        Effect = effect;
        CompletedOutcomes = completedOutcomes;
    }

    public AgentRole Role { get; }

    public AgentResponseContract ResponseContract { get; }

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

    /// <summary>Resolves the one fixed contract for a role. Fails closed — throws rather than
    /// returning a default — for any <see cref="AgentRole"/> this contract table does not
    /// recognize.</summary>
    public static AgentAttemptContract For(AgentRole role) =>
        ByRole.TryGetValue(role, out var contract)
            ? contract
            : throw new ArgumentOutOfRangeException(nameof(role), role, "No agent attempt contract is defined for this role.");

    /// <summary>Every defined <see cref="AgentRole"/> whose contract has the given effect —
    /// immutable and caller-safe. Fails closed for an undefined <see cref="AgentEffectKind"/>
    /// rather than silently returning an empty or partial set.</summary>
    public static IReadOnlySet<AgentRole> RolesForEffect(AgentEffectKind effect)
    {
        if (!Enum.IsDefined(effect))
        {
            throw new ArgumentOutOfRangeException(nameof(effect), effect, "Not a defined agent effect kind.");
        }

        return ByRole.Values.Where(contract => contract.Effect == effect).Select(contract => contract.Role).ToFrozenSet();
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

        return ByRole.Values
            .Where(contract => contract.Effect == effect)
            .SelectMany(contract => contract.CompletedOutcomes)
            .ToFrozenSet();
    }

    private static FrozenDictionary<AgentRole, AgentAttemptContract> BuildContracts()
    {
        AgentAttemptContract[] contracts =
        [
            new(AgentRole.Planner, AgentResponseContract.Proposal, AgentEffectKind.ReadOnly,
                new HashSet<AgentOutcome> { AgentOutcome.Proposed }.ToFrozenSet()),
            new(AgentRole.CriticalReviewer, AgentResponseContract.CriticalReview, AgentEffectKind.ReadOnly,
                new HashSet<AgentOutcome> { AgentOutcome.Accepted, AgentOutcome.Challenged }.ToFrozenSet()),
            new(AgentRole.Resolver, AgentResponseContract.ChallengeResolution, AgentEffectKind.ReadOnly,
                new HashSet<AgentOutcome> { AgentOutcome.Resolved }.ToFrozenSet()),
            new(AgentRole.Implementer, AgentResponseContract.ImplementationReport, AgentEffectKind.WorkspaceMutating,
                new HashSet<AgentOutcome> { AgentOutcome.Implemented }.ToFrozenSet()),
            new(AgentRole.CodeReviewer, AgentResponseContract.ImplementationReview, AgentEffectKind.ReadOnly,
                new HashSet<AgentOutcome> { AgentOutcome.ReviewApproved, AgentOutcome.ReviewChangesRequested }.ToFrozenSet()),
        ];

        return contracts.ToFrozenDictionary(contract => contract.Role);
    }
}
