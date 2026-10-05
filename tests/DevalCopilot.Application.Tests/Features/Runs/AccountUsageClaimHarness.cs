using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>What one Codex claim handler's tests must supply so the same account-usage contract (ADR-0025) is proved against all four of
/// them and their repair paths without copying every case.</summary>
public sealed class AccountUsageClaimHarness
{
    public required SqliteDatabaseFixture Fixture { get; init; }

    public required DateTimeOffset Now { get; init; }

    /// <summary>Seeds one eligible run whose Codex runtime is observed at <see cref="LaunchExecutable"/> and returns its id.</summary>
    public required Func<Task<Guid>> SeedRunAsync { get; init; }

    /// <summary>Claims for the run on a fresh context with the given adapter and clock; the hook runs inside the Git capture.</summary>
    public required Func<Guid, IAccountUsageObserver?, TimeProvider, Func<CancellationToken, Task>?, Task<AccountUsageClaimOutcome>> ClaimOn { get; init; }

    public const string LaunchExecutable = @"C:\safe\codex.exe";

    public async Task<int> AttemptCountAsync(Guid runId)
    {
        await using var context = Fixture.CreateContext();
        return await context.Attempts.CountAsync(candidate => candidate.RunId == runId);
    }

    public Task<AccountUsageClaimOutcome> ClaimAsync(Guid runId, IAccountUsageObserver? adapter, Func<CancellationToken, Task>? hook = null) =>
        ClaimOn(runId, adapter, new AdjustableTimeProvider(Now), hook);

    public Task<AccountUsageClaimOutcome> ClaimAsync(
        Guid runId, IAccountUsageObserver? adapter, TimeProvider clock, Func<CancellationToken, Task>? hook) =>
        ClaimOn(runId, adapter, clock, hook);
}
