namespace DevalCopilot.Application.Features.Runs.Commands.ObserveAgentSupervision;

/// <summary>Whether this observation changed the receipt, whether it is now closed, and its retained first attention (null when none).</summary>
public sealed record ObserveAgentSupervisionCommandResult(bool Changed, bool Closed, string? AttentionReason);
