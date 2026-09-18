namespace DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;

/// <summary>A Codex final response that has already passed protocol/schema validation, reduced to
/// exactly the two bounded strings <c>CollaborationMessage.Record</c> needs. Domain still
/// independently validates both when the message is actually constructed — this is not the only
/// defense.</summary>
public sealed record ValidatedProposal(string Summary, string StructuredContentJson);
