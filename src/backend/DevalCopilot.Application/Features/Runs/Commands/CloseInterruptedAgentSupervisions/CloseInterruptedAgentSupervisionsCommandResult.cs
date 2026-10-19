namespace DevalCopilot.Application.Features.Runs.Commands.CloseInterruptedAgentSupervisions;

/// <summary>How many previous-host observations were closed, and how many could not be (their recorded times do not cohere with the
/// clock, so they stay open and read as unavailable rather than fresh).</summary>
public sealed record CloseInterruptedAgentSupervisionsCommandResult(int ClosedCount, int UnclosedCount);
