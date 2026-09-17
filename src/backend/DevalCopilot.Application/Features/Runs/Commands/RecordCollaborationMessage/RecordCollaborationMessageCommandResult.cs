namespace DevalCopilot.Application.Features.Runs.Commands.RecordCollaborationMessage;

public sealed record RecordCollaborationMessageCommandResult(Guid MessageId, long MessageSequence, long EventSequence);
