using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordCollaborationMessage;

public sealed class RecordCollaborationMessageCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordCollaborationMessageCommand, Result<RecordCollaborationMessageCommandResult>>
{
    public async Task<Result<RecordCollaborationMessageCommandResult>> HandleAsync(
        RecordCollaborationMessageCommand command,
        CancellationToken cancellationToken)
    {
        if (command.MessageId == Guid.Empty)
        {
            return Result<RecordCollaborationMessageCommandResult>.Failure(
                Error.Conflict("collaboration_messages.invalid", "The collaboration message is not valid."));
        }

        if (!Enum.IsDefined(command.Type))
        {
            return Result<RecordCollaborationMessageCommandResult>.Failure(
                Error.Conflict("collaboration_messages.invalid", "The collaboration message is not valid."));
        }

        if (command.InReplyToMessageId == command.MessageId)
        {
            return Result<RecordCollaborationMessageCommandResult>.Failure(
                Error.Conflict("collaboration_messages.self_reply", "A collaboration message cannot reply to itself."));
        }

        var referenceViolation = CollaborationMessageReplyPolicy.EvaluateReference(
            command.Type,
            command.InReplyToMessageId.HasValue);
        if (referenceViolation != CollaborationMessageReplyViolation.None)
        {
            return Result<RecordCollaborationMessageCommandResult>.Failure(
                CollaborationMessageReplyErrors.Create(referenceViolation));
        }

        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Result<RecordCollaborationMessageCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        if (command.AttemptId.HasValue)
        {
            var attemptBelongsToRun = await dbContext.Attempts
                .AnyAsync(candidate => candidate.Id == command.AttemptId.Value && candidate.RunId == run.Id, cancellationToken);
            if (!attemptBelongsToRun)
            {
                return Result<RecordCollaborationMessageCommandResult>.Failure(
                    Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
            }
        }

        CollaborationMessage? reply = null;
        if (command.InReplyToMessageId.HasValue)
        {
            reply = await dbContext.CollaborationMessages
                .SingleOrDefaultAsync(candidate => candidate.Id == command.InReplyToMessageId.Value, cancellationToken);
            if (reply is null || reply.RunId != run.Id)
            {
                return Result<RecordCollaborationMessageCommandResult>.Failure(
                    Error.NotFound("collaboration_messages.reply_not_found", "The referenced collaboration message was not found for this run."));
            }

            var parentViolation = CollaborationMessageReplyPolicy.Evaluate(command.Type, reply.Type);
            if (parentViolation != CollaborationMessageReplyViolation.None)
            {
                return Result<RecordCollaborationMessageCommandResult>.Failure(
                    CollaborationMessageReplyErrors.Create(parentViolation));
            }
        }

        var nowUtc = timeProvider.GetUtcNow();
        CollaborationMessage message;
        try
        {
            message = CollaborationMessage.Record(
                command.MessageId,
                run.Id,
                command.AttemptId,
                command.ProtocolVersion,
                command.Actor,
                command.Recipient,
                command.Type,
                command.InReplyToMessageId,
                command.Summary,
                command.StructuredContentJson,
                command.Provenance,
                nowUtc);
        }
        catch (ArgumentException)
        {
            return Result<RecordCollaborationMessageCommandResult>.Failure(
                Error.Conflict("collaboration_messages.invalid", "The collaboration message is not valid."));
        }

        dbContext.CollaborationMessages.Add(message);
        var eventPayload = JsonSerializer.Serialize(new
        {
            messageId = message.Id,
            type = message.Type.ToString(),
            provenance = message.Provenance.ToString(),
        });
        var runEvent = RunEvent.Record(
            Guid.NewGuid(),
            run.Id,
            command.AttemptId,
            RunEventType.CollaborationMessageRecorded,
            command.Actor,
            eventPayload,
            nowUtc);
        dbContext.Events.Add(runEvent);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<RecordCollaborationMessageCommandResult>.Success(
            new RecordCollaborationMessageCommandResult(message.Id, message.Sequence, runEvent.Sequence));
    }
}
