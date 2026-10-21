namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The one Domain-owned definition of the fixed local execution progression, its bounds, deadlines and state classification
/// (ADR-0060): initial implementation, whole verification passes, ordinary review or eligible failure diagnosis, and at most two
/// shared corrections.
/// </summary>
public static class LocalExecutionSequencePolicy
{
    public const int MaximumCorrections = 2;
    public const int MaximumPasses = 3;
    public const int MaximumAgentClaims = 6;
    public const int MinimumRecipes = 1;
    public const int MaximumRecipes = 32;

    /// <summary>The absolute two-hour advancement deadline, measured from arming. It neither extends nor replaces any operation's own
    /// timeout or budget.</summary>
    public static readonly TimeSpan AdvancementWindow = TimeSpan.FromHours(2);

    /// <summary>The preparation deadline, measured from the reservation of the step's one preparation intent.</summary>
    public static readonly TimeSpan PreparationDeadline = TimeSpan.FromMinutes(2);

    /// <summary>The coordination grace added to the claim time and the actual invocation or recipe timeout. It is an observation
    /// deadline, never measured invocation time, an account budget or proof of process death.</summary>
    public static readonly TimeSpan ResultGrace = TimeSpan.FromMinutes(2);

    /// <summary>At most this many operation steps: every pass runs every recipe once, plus implementation, three reviews or
    /// diagnoses and two corrections.</summary>
    public static int MaximumSteps(int recipeCount) => checked(3 * recipeCount + 6);

    public static bool IsRecipeCountValid(int recipeCount) => recipeCount is >= MinimumRecipes and <= MaximumRecipes;

    public static bool IsAgentKind(LocalExecutionStepKind kind) => kind != LocalExecutionStepKind.Verification;

    public static bool IsCorrectionKind(LocalExecutionStepKind kind) =>
        kind is LocalExecutionStepKind.ReviewCorrection or LocalExecutionStepKind.DiagnosisCorrection;

    /// <summary>The existing response contract an Agent step is claimed under. Both correction origins share one contract.</summary>
    public static AgentResponseContract? ContractOf(LocalExecutionStepKind kind) => kind switch
    {
        LocalExecutionStepKind.Implementation => AgentResponseContract.ImplementationReport,
        LocalExecutionStepKind.CodeReview => AgentResponseContract.ImplementationReview,
        LocalExecutionStepKind.VerificationDiagnosis => AgentResponseContract.VerificationDiagnosis,
        LocalExecutionStepKind.ReviewCorrection or LocalExecutionStepKind.DiagnosisCorrection => AgentResponseContract.ReviewCorrection,
        _ => null,
    };

    /// <summary>The coordinator may reserve or observe a step: the sequence has not ended and has not been told to stop.</summary>
    public static bool IsAdvancing(LocalExecutionSequenceState state) =>
        state is LocalExecutionSequenceState.Armed or LocalExecutionSequenceState.PreparingStep
            or LocalExecutionSequenceState.AwaitingStepResult;

    /// <summary>Automatic authority is still live: it can advance or it drains an already claimed step.</summary>
    public static bool IsAuthorityLive(LocalExecutionSequenceState state) =>
        IsAdvancing(state) || state == LocalExecutionSequenceState.Stopping;

    public static bool IsEnded(LocalExecutionSequenceState state) => !IsAuthorityLive(state);

    public static DateTimeOffset PreparationDeadlineOf(DateTimeOffset reservedAtUtc) => reservedAtUtc + PreparationDeadline;

    public static DateTimeOffset AdvancementDeadlineOf(DateTimeOffset armedAtUtc) => armedAtUtc + AdvancementWindow;

    /// <summary>
    /// Claim time plus the operation's actual invocation or recipe timeout plus the grace, in checked arithmetic and capped by the
    /// sequence's advancement deadline. Null when the timeout is not a positive bounded value or the sum is not representable: such a
    /// deadline is never fabricated.
    /// </summary>
    public static DateTimeOffset? ResultDeadlineOf(
        DateTimeOffset claimedAtUtc, TimeSpan? timeout, DateTimeOffset advancementDeadlineUtc)
    {
        if (timeout is not { } value || value <= TimeSpan.Zero)
        {
            return null;
        }

        try
        {
            var deadline = claimedAtUtc.AddTicks(checked(value.Ticks + ResultGrace.Ticks));
            return deadline < advancementDeadlineUtc ? deadline : advancementDeadlineUtc;
        }
        catch (Exception exception) when (exception is OverflowException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Whether a result observed at <paramref name="observedAtUtc"/> is at or past the result deadline. An unrepresentable
    /// deadline is never trusted, so it counts as elapsed.</summary>
    public static bool IsResultLate(
        DateTimeOffset claimedAtUtc, TimeSpan? timeout, DateTimeOffset advancementDeadlineUtc, DateTimeOffset observedAtUtc) =>
        ResultDeadlineOf(claimedAtUtc, timeout, advancementDeadlineUtc) is not { } deadline || observedAtUtc >= deadline;

    public static bool IsLowercaseSha256(string? value)
    {
        if (value is not { Length: 64 })
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!(character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }
}
