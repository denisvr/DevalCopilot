namespace DevalCopilot.Application.Features.Runs.Commands.SetCodexAssignmentPreference;

public sealed record SetCodexAssignmentPreferenceCommandResult(string? RequestedModel, string? RequestedEffort);
