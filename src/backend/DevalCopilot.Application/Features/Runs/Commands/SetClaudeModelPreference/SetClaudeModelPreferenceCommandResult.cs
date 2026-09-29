namespace DevalCopilot.Application.Features.Runs.Commands.SetClaudeModelPreference;

public sealed record SetClaudeModelPreferenceCommandResult(string? RequestedModel, string? RequestedEffort);
