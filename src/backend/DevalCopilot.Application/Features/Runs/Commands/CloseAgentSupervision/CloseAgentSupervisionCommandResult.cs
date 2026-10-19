using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.CloseAgentSupervision;

/// <summary>The closure reason and the receipt's retained first attention (null when none), as committed.</summary>
public sealed record CloseAgentSupervisionCommandResult(string ClosureReason, string? AttentionReason, long Revision);
