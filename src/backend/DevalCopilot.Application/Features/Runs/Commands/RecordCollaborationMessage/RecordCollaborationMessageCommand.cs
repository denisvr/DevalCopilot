using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordCollaborationMessage;

/// <summary>
/// Internal application boundary for a future provider adapter to append one validated,
/// project-owned protocol fact. It is intentionally not exposed as a public HTTP write API.
/// </summary>
public sealed record RecordCollaborationMessageCommand(
    Guid MessageId,
    Guid RunId,
    Guid? AttemptId,
    string ProtocolVersion,
    ParticipantKind Actor,
    ParticipantKind Recipient,
    CollaborationMessageType Type,
    Guid? InReplyToMessageId,
    string Summary,
    string StructuredContentJson,
    CollaborationMessageProvenance Provenance) : IManualTransactionCommand<Result<RecordCollaborationMessageCommandResult>>;
