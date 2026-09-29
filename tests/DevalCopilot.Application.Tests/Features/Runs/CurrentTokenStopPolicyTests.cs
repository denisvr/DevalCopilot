using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The commit-time stop-policy guard in isolation, against real SQLite.</summary>
public sealed class CurrentTokenStopPolicyTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Guid> SeedRunAsync(long? codex = null, long? claude = null)
    {
        await using var context = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Objective", Now);
        run.Claim(Now);
        if (codex is not null)
        {
            run.SetTokenStopThreshold(AgentProvider.Codex, codex);
        }

        if (claude is not null)
        {
            run.SetTokenStopThreshold(AgentProvider.ClaudeCode, claude);
        }

        context.Projects.Add(project);
        context.Runs.Add(run);
        await context.SaveChangesAsync();
        return run.Id;
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(10L, null)]
    [InlineData(null, 20L)]
    [InlineData(10L, 20L)]
    public async Task ConfirmUnchangedAsync_matches_exactly_the_loaded_policy(long? codex, long? claude)
    {
        var runId = await SeedRunAsync(codex, claude);
        await using var context = _fixture.CreateContext();
        var run = await context.Runs.SingleAsync(r => r.Id == runId);

        Assert.True(await CurrentTokenStopPolicy.ConfirmUnchangedAsync(context, run, CancellationToken.None));
    }

    [Theory]
    [InlineData(AgentProvider.Codex, 5L)]
    [InlineData(AgentProvider.Codex, null)]
    [InlineData(AgentProvider.ClaudeCode, 5L)]
    [InlineData(AgentProvider.ClaudeCode, null)]
    public async Task ConfirmUnchangedAsync_and_HasChangedAsync_detect_a_change_to_either_threshold_including_a_clear(
        AgentProvider changed, long? newValue)
    {
        var runId = await SeedRunAsync(codex: 10, claude: 20);
        await using var context = _fixture.CreateContext();
        var run = await context.Runs.SingleAsync(r => r.Id == runId);
        await TokenStopTestSupport.SetStopAsync(_fixture, runId, changed, newValue);

        Assert.True(await CurrentTokenStopPolicy.HasChangedAsync(context, run, CancellationToken.None));
        Assert.False(await CurrentTokenStopPolicy.ConfirmUnchangedAsync(context, run, CancellationToken.None));
    }

    [Fact]
    public async Task HasChangedAsync_is_false_for_an_unchanged_policy()
    {
        var runId = await SeedRunAsync(codex: 10);
        await using var context = _fixture.CreateContext();
        var run = await context.Runs.SingleAsync(r => r.Id == runId);

        Assert.False(await CurrentTokenStopPolicy.HasChangedAsync(context, run, CancellationToken.None));
    }

    [Fact]
    public async Task Guard_alone_makes_a_save_fail_when_the_policy_changed_after_load_and_writes_nothing()
    {
        var runId = await SeedRunAsync();
        await using var context = _fixture.CreateContext();
        var run = await context.Runs.SingleAsync(r => r.Id == runId);
        CurrentTokenStopPolicy.Guard(context, run);
        await TokenStopTestSupport.SetStopAsync(_fixture, runId, AgentProvider.ClaudeCode, 7);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());

        await using var verify = _fixture.CreateContext();
        var stored = await verify.Runs.AsNoTracking().SingleAsync(r => r.Id == runId);
        Assert.Equal(7, stored.ClaudeTokenStopThreshold);
        Assert.Null(stored.CodexTokenStopThreshold);
    }

    [Fact]
    public async Task Guard_alone_lets_a_save_succeed_when_the_policy_is_unchanged()
    {
        var runId = await SeedRunAsync(codex: 10);
        await using var context = _fixture.CreateContext();
        var run = await context.Runs.SingleAsync(r => r.Id == runId);
        CurrentTokenStopPolicy.Guard(context, run);

        await context.SaveChangesAsync();

        await using var verify = _fixture.CreateContext();
        Assert.Equal(10, (await verify.Runs.AsNoTracking().SingleAsync(r => r.Id == runId)).CodexTokenStopThreshold);
    }

    [Fact]
    public async Task An_advisory_warning_write_never_fails_a_guarded_save()
    {
        var runId = await SeedRunAsync();
        await using var context = _fixture.CreateContext();
        var run = await context.Runs.SingleAsync(r => r.Id == runId);
        CurrentTokenStopPolicy.Guard(context, run);
        await using (var competing = _fixture.CreateContext())
        {
            var other = await competing.Runs.SingleAsync(r => r.Id == runId);
            other.SetTokenWarningThreshold(AgentProvider.Codex, 3);
            await competing.SaveChangesAsync();
        }

        await context.SaveChangesAsync();

        await using var verify = _fixture.CreateContext();
        Assert.Equal(3, (await verify.Runs.AsNoTracking().SingleAsync(r => r.Id == runId)).CodexTokenWarningThreshold);
    }
}
