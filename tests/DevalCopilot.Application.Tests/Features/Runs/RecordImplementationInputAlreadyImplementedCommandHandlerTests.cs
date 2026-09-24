using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationInputAlreadyImplemented;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// No dedicated test file existed for this handler before Slice B.1's fail-closed correction.
/// Covers the routing guard's own contract-coherence check — a genuinely malformed persisted
/// attempt (correct AgentRole, incoherent AgentResponseContract) must be rejected exactly like any
/// other "not this attempt shape" case, never treated as a valid implementation attempt just
/// because its role happens to match.
/// </summary>
public sealed class RecordImplementationInputAlreadyImplementedCommandHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private static (Project Project, Run Run, Attempt Attempt) CreateClaimedImplementationAttempt()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Implement the resolved plan", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);
        return (project, run, attempt);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_attempt_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();

        var handler = new RecordImplementationInputAlreadyImplementedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordImplementationInputAlreadyImplementedCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_a_codex_planning_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan the next increment", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, 1);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordImplementationInputAlreadyImplementedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordImplementationInputAlreadyImplementedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_implementation", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    // Fail-closed regression: a genuinely malformed persisted attempt — the correct AgentRole but
    // a response contract that does not cohere with AgentAttemptContract.For(role) — must be
    // rejected by the same safe "not this attempt shape" failure, never treated as a valid
    // implementation attempt just because its role happens to match.
    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_an_attempt_with_a_mismatched_response_contract()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedImplementationAttempt();
        var responseContractProperty = typeof(Attempt).GetProperty(nameof(Attempt.AgentResponseContract))!;
        responseContractProperty.GetSetMethod(nonPublic: true)!.Invoke(attempt, [AgentResponseContract.Proposal]);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordImplementationInputAlreadyImplementedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordImplementationInputAlreadyImplementedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_implementation", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Empty(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_when_no_competing_implementation_exists_at_all()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedImplementationAttempt();
        var inputMessage = AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), sequence: 0);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(inputMessage);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordImplementationInputAlreadyImplementedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new RecordImplementationInputAlreadyImplementedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.no_competing_implementation_found", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Empty(dbContext.Events.Where(e => e.AttemptId == attempt.Id));
    }
}
