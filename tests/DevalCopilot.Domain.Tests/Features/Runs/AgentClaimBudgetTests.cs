using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class AgentClaimBudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Run_defaults_to_sixteen_positive_maximum_agent_attempts()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Bound Agent claims", Now);

        Assert.Equal(16, run.MaximumAgentAttempts);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Invalid", Now, maximumAgentAttempts: 0));
    }

    [Fact]
    public void Run_accepts_an_explicit_maximum_independent_of_the_review_correction_budget()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Custom budgets", Now, maximumReviewCorrectionAttempts: 3, maximumAgentAttempts: 20);

        Assert.Equal(3, run.MaximumReviewCorrectionAttempts);
        Assert.Equal(20, run.MaximumAgentAttempts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Every_agent_claim_factory_rejects_a_non_positive_budget_slot(int invalidSlot)
    {
        var runId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
        var manifestId = Guid.NewGuid();
        const string fingerprint = "fingerprint";

        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgent(
            Guid.NewGuid(), runId, 1, workspaceId, checkpointId, fingerprint, manifestId,
            TimeSpan.FromMinutes(10), 1024, 2048, Now, invalidSlot));

        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), runId, 1, workspaceId, checkpointId, fingerprint, manifestId,
            TimeSpan.FromMinutes(10), 1024, 2048, Now, invalidSlot));

        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), runId, 1, workspaceId, checkpointId, fingerprint, manifestId,
            TimeSpan.FromMinutes(10), 1024, 2048, Now, invalidSlot));

        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), runId, 1, workspaceId, checkpointId, fingerprint, manifestId,
            TimeSpan.FromMinutes(10), 1024, 2048, Now, invalidSlot));

        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), runId, 1, workspaceId, checkpointId, fingerprint, manifestId,
            TimeSpan.FromMinutes(10), 1024, 2048, Now, invalidSlot));

        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentImplementationWithAssignment(
            Guid.NewGuid(), runId, 1, workspaceId, checkpointId, fingerprint, manifestId,
            TimeSpan.FromMinutes(10), 1024, 2048, Now, requestedModel: null, requestedEffort: null,
            AgentPermissionProfile.WorkspaceEditOnly, "claude-implementation-v1", invalidSlot));

        Assert.Throws<ArgumentOutOfRangeException>(() => Attempt.ClaimAgentReviewCorrection(
            Guid.NewGuid(), runId, 1, workspaceId, checkpointId, fingerprint, manifestId,
            TimeSpan.FromMinutes(10), 1024, 2048, Now, invalidSlot));
    }

    [Fact]
    public void A_claimed_agent_attempt_records_its_own_permanent_budget_slot()
    {
        var attempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "fingerprint", Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 1024, 2048, Now, agentBudgetSlot: 7);

        Assert.Equal(7, attempt.AgentBudgetSlot);
    }

    [Fact]
    public void A_simulated_or_process_attempt_never_carries_a_budget_slot()
    {
        var simulated = Attempt.Claim(Guid.NewGuid(), Guid.NewGuid(), 1, Now);
        var process = Attempt.ClaimProcess(
            Guid.NewGuid(), Guid.NewGuid(), 1,
            new ProcessExecutionIntent(@"C:\tools\build.exe", ["--verify"], @"C:\repos\devalcopilot", @"C:\repos", TimeSpan.FromMinutes(5), 65536, 131072),
            Now);

        Assert.Null(simulated.AgentBudgetSlot);
        Assert.Null(process.AgentBudgetSlot);
    }
}
