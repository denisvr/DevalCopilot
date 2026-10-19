namespace DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;

/// <summary>How a composition's adapter double holds its call until released (ADR-0056 blocked-adapter scenario): completes
/// <see cref="Entered"/> when the call is reached, lets it go on <see cref="Release"/>, and is optionally armed before the supervisor
/// starts.</summary>
internal sealed record AdapterHold(Func<object, Task> Entered, Action<object> Release, Action<object>? Arm = null);
