using Microsoft.Extensions.Hosting;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>What one Codex supervisor's hosted tests supply so the same account-usage scenarios (ADR-0025) run against all four real
/// supervisors with their real mediator, handlers and SQLite database: how to claim an attempt (with the stop configured at 80 percent,
/// or without it), how to start a fresh supervisor, how to read the attempt, and how many times the provider adapter was invoked.</summary>
public sealed record AccountUsageHostedHarness(
    ScriptedAccountUsageAdapter Adapter,
    Func<Task<(Guid RunId, Guid AttemptId)>> ClaimAsync,
    Func<BackgroundService> NewSupervisor,
    Func<Guid, Task<HostedAttemptView>> ReadAsync,
    Func<int> AgentInvocations,
    Func<string, Task> ExecuteSqlAsync);
