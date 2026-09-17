using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordSimulatedAgentStep;

/// <summary>
/// Owns its own save so the assigned monotonic event sequence can be reported back to
/// the caller immediately, for the post-commit SignalR notification.
/// </summary>
public sealed record RecordSimulatedAgentStepCommand(
    Guid RunId,
    Guid AttemptId,
    RunStage Stage,
    ParticipantKind Actor,
    ParticipantKind Recipient,
    string EventType,
    CollaborationMessageType MessageType,
    Guid? InReplyToMessageId,
    string Summary,
    string StructuredContentJson) : IManualTransactionCommand<Result<RecordSimulatedAgentStepCommandResult>>;
