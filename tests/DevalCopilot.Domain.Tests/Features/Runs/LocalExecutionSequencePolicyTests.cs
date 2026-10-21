using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>ADR-0060: the one Domain-owned definition of the bounds, deadlines and state classification of the local execution
/// progression.</summary>
public sealed class LocalExecutionSequencePolicyTests
{
    private static readonly DateTimeOffset Claimed = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_bounds_are_the_accepted_constants()
    {
        Assert.Equal(2, LocalExecutionSequencePolicy.MaximumCorrections);
        Assert.Equal(3, LocalExecutionSequencePolicy.MaximumPasses);
        Assert.Equal(6, LocalExecutionSequencePolicy.MaximumAgentClaims);
        Assert.Equal(1, LocalExecutionSequencePolicy.MinimumRecipes);
        Assert.Equal(32, LocalExecutionSequencePolicy.MaximumRecipes);
        Assert.Equal(TimeSpan.FromHours(2), LocalExecutionSequencePolicy.AdvancementWindow);
        Assert.Equal(TimeSpan.FromMinutes(2), LocalExecutionSequencePolicy.PreparationDeadline);
        Assert.Equal(TimeSpan.FromMinutes(2), LocalExecutionSequencePolicy.ResultGrace);
    }

    [Theory]
    [InlineData(1, 9)]
    [InlineData(2, 12)]
    [InlineData(32, 102)]
    public void The_maximum_step_count_is_three_times_the_recipe_count_plus_six(int recipeCount, int expected)
    {
        Assert.Equal(expected, LocalExecutionSequencePolicy.MaximumSteps(recipeCount));
    }

    [Fact]
    public void The_maximum_step_count_uses_checked_arithmetic()
    {
        Assert.Throws<OverflowException>(() => LocalExecutionSequencePolicy.MaximumSteps(int.MaxValue));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(32, true)]
    [InlineData(33, false)]
    [InlineData(-1, false)]
    public void The_recipe_count_is_valid_only_from_one_to_thirty_two(int count, bool expected)
    {
        Assert.Equal(expected, LocalExecutionSequencePolicy.IsRecipeCountValid(count));
    }

    [Theory]
    [InlineData(LocalExecutionStepKind.Implementation, true, false)]
    [InlineData(LocalExecutionStepKind.Verification, false, false)]
    [InlineData(LocalExecutionStepKind.CodeReview, true, false)]
    [InlineData(LocalExecutionStepKind.VerificationDiagnosis, true, false)]
    [InlineData(LocalExecutionStepKind.ReviewCorrection, true, true)]
    [InlineData(LocalExecutionStepKind.DiagnosisCorrection, true, true)]
    public void Every_kind_but_verification_is_an_Agent_kind_and_both_corrections_are_correction_kinds(
        LocalExecutionStepKind kind, bool isAgent, bool isCorrection)
    {
        Assert.Equal(isAgent, LocalExecutionSequencePolicy.IsAgentKind(kind));
        Assert.Equal(isCorrection, LocalExecutionSequencePolicy.IsCorrectionKind(kind));
    }

    [Theory]
    [InlineData(LocalExecutionStepKind.Implementation, AgentResponseContract.ImplementationReport)]
    [InlineData(LocalExecutionStepKind.CodeReview, AgentResponseContract.ImplementationReview)]
    [InlineData(LocalExecutionStepKind.VerificationDiagnosis, AgentResponseContract.VerificationDiagnosis)]
    [InlineData(LocalExecutionStepKind.ReviewCorrection, AgentResponseContract.ReviewCorrection)]
    [InlineData(LocalExecutionStepKind.DiagnosisCorrection, AgentResponseContract.ReviewCorrection)]
    public void Each_Agent_kind_is_claimed_under_its_existing_contract_and_both_corrections_share_one(
        LocalExecutionStepKind kind, AgentResponseContract contract)
    {
        Assert.Equal(contract, LocalExecutionSequencePolicy.ContractOf(kind));
    }

    [Fact]
    public void A_verification_step_has_no_Agent_contract_and_an_undefined_kind_has_none()
    {
        Assert.Null(LocalExecutionSequencePolicy.ContractOf(LocalExecutionStepKind.Verification));
        Assert.Null(LocalExecutionSequencePolicy.ContractOf((LocalExecutionStepKind)99));
    }

    [Theory]
    [InlineData(LocalExecutionSequenceState.Armed, true, true)]
    [InlineData(LocalExecutionSequenceState.PreparingStep, true, true)]
    [InlineData(LocalExecutionSequenceState.AwaitingStepResult, true, true)]
    [InlineData(LocalExecutionSequenceState.Stopping, false, true)]
    [InlineData(LocalExecutionSequenceState.ReadyForHumanApproval, false, false)]
    [InlineData(LocalExecutionSequenceState.HumanEscalation, false, false)]
    [InlineData(LocalExecutionSequenceState.Stopped, false, false)]
    [InlineData(LocalExecutionSequenceState.NeedsAttention, false, false)]
    [InlineData(LocalExecutionSequenceState.Interrupted, false, false)]
    public void The_state_classification_separates_advancing_live_and_ended(
        LocalExecutionSequenceState state, bool advancing, bool live)
    {
        Assert.Equal(advancing, LocalExecutionSequencePolicy.IsAdvancing(state));
        Assert.Equal(live, LocalExecutionSequencePolicy.IsAuthorityLive(state));
        Assert.Equal(!live, LocalExecutionSequencePolicy.IsEnded(state));
    }

    [Fact]
    public void The_preparation_and_advancement_deadlines_are_measured_from_their_origin()
    {
        Assert.Equal(Claimed.AddMinutes(2), LocalExecutionSequencePolicy.PreparationDeadlineOf(Claimed));
        Assert.Equal(Claimed.AddHours(2), LocalExecutionSequencePolicy.AdvancementDeadlineOf(Claimed));
    }

    [Fact]
    public void The_result_deadline_is_claim_plus_timeout_plus_two_minutes()
    {
        var deadline = LocalExecutionSequencePolicy.ResultDeadlineOf(Claimed, TimeSpan.FromMinutes(10), Claimed.AddHours(2));

        Assert.Equal(Claimed.AddMinutes(12), deadline);
    }

    [Fact]
    public void The_result_deadline_is_capped_by_the_advancement_deadline()
    {
        var cap = Claimed.AddMinutes(5);

        Assert.Equal(cap, LocalExecutionSequencePolicy.ResultDeadlineOf(Claimed, TimeSpan.FromMinutes(10), cap));
        Assert.Equal(cap, LocalExecutionSequencePolicy.ResultDeadlineOf(Claimed, TimeSpan.FromMinutes(3), cap));
        Assert.Equal(Claimed.AddMinutes(4), LocalExecutionSequencePolicy.ResultDeadlineOf(Claimed, TimeSpan.FromMinutes(2), cap));
    }

    [Fact]
    public void The_result_deadline_is_null_for_a_missing_zero_or_negative_timeout()
    {
        var cap = Claimed.AddHours(2);

        Assert.Null(LocalExecutionSequencePolicy.ResultDeadlineOf(Claimed, null, cap));
        Assert.Null(LocalExecutionSequencePolicy.ResultDeadlineOf(Claimed, TimeSpan.Zero, cap));
        Assert.Null(LocalExecutionSequencePolicy.ResultDeadlineOf(Claimed, TimeSpan.FromSeconds(-1), cap));
    }

    [Fact]
    public void The_result_deadline_is_null_when_the_arithmetic_overflows()
    {
        var cap = Claimed.AddHours(2);

        Assert.Null(LocalExecutionSequencePolicy.ResultDeadlineOf(Claimed, TimeSpan.MaxValue, cap));
        Assert.Null(LocalExecutionSequencePolicy.ResultDeadlineOf(DateTimeOffset.MaxValue.AddMinutes(-1), TimeSpan.FromMinutes(10), cap));
    }

    [Fact]
    public void A_result_is_late_at_or_after_the_deadline_and_never_before()
    {
        var timeout = TimeSpan.FromMinutes(10);
        var cap = Claimed.AddHours(2);
        var deadline = Claimed.AddMinutes(12);

        Assert.False(LocalExecutionSequencePolicy.IsResultLate(Claimed, timeout, cap, deadline.AddTicks(-1)));
        Assert.True(LocalExecutionSequencePolicy.IsResultLate(Claimed, timeout, cap, deadline));
        Assert.True(LocalExecutionSequencePolicy.IsResultLate(Claimed, timeout, cap, deadline.AddSeconds(1)));
    }

    [Fact]
    public void An_unrepresentable_deadline_is_never_trusted_so_the_result_counts_as_late()
    {
        var cap = Claimed.AddHours(2);

        Assert.True(LocalExecutionSequencePolicy.IsResultLate(Claimed, null, cap, Claimed));
        Assert.True(LocalExecutionSequencePolicy.IsResultLate(Claimed, TimeSpan.Zero, cap, Claimed.AddTicks(-1)));
        Assert.True(LocalExecutionSequencePolicy.IsResultLate(Claimed, TimeSpan.MaxValue, cap, Claimed));
    }

    [Fact]
    public void A_late_result_honors_the_advancement_cap()
    {
        var cap = Claimed.AddMinutes(5);

        Assert.True(LocalExecutionSequencePolicy.IsResultLate(Claimed, TimeSpan.FromMinutes(30), cap, cap));
        Assert.False(LocalExecutionSequencePolicy.IsResultLate(Claimed, TimeSpan.FromMinutes(30), cap, cap.AddTicks(-1)));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true)]
    [InlineData("0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef", false)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde", false)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_64_character_lowercase_hexadecimal_value_is_a_sha256(string? value, bool expected)
    {
        Assert.Equal(expected, LocalExecutionSequencePolicy.IsLowercaseSha256(value));
    }
}
