namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;

/// <summary>One field of a message's structured content, admitted only after the existing content policy validated its name, type
/// and bounded value.</summary>
public sealed record CollaborationHistoryField(string Name, string Value);
