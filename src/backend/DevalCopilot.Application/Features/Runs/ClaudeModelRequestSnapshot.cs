namespace DevalCopilot.Application.Features.Runs;

/// <summary>The Run's current Claude model/effort request as read at claim time: an immutable pair,
/// never an observed or effective value.</summary>
public readonly record struct ClaudeModelRequestSnapshot(string? Model, string? Effort);
