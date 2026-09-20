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

        // A real Agent attempt's protocol output is never appended by this generic, caller-selected
        // path: only its own role-specific atomic result handler may record it, alongside the
        // terminal Domain transition, cardinality, artifact, checkpoint, and review-evidence
        // invariants that handler alone enforces. Routing an Agent attempt through here would let a
        // caller append an extra message, bypass those invariants entirely, or record output with
        // no corresponding terminal outcome. The full Attempt is loaded (not merely its existence
        // probed) specifically so its Kind can be inspected before any further decision is made.
        Attempt? attempt = null;
        if (command.AttemptId.HasValue)
        {
            attempt = await dbContext.Attempts
                .SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId.Value && candidate.RunId == run.Id, cancellationToken);
            if (attempt is null)
            {
                return Result<RecordCollaborationMessageCommandResult>.Failure(
                    Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
            }

            if (attempt.Kind == AttemptKind.Agent)
            {
                return Result<RecordCollaborationMessageCommandResult>.Failure(Error.Conflict(
                    "collaboration_messages.agent_attempt_requires_result_handler",
                    "A real Agent attempt's protocol output can only be recorded by its own role-specific result handler."));
            }

            // This legacy, caller-selected path may only ever apply to the deterministic Simulated
            // walking-skeleton sequence. A Process attempt (or any other non-Simulated kind) has no
            // AgentRole or collaboration-authorship contract at all, so nothing here could ever
            // validate the caller's chosen actor/type against it — recording a message against it
            // would falsely associate that fact with an attempt that never had any collaboration
            // role to begin with.
            if (attempt.Kind != AttemptKind.Simulated)
            {
                return Result<RecordCollaborationMessageCommandResult>.Failure(Error.Conflict(
                    "collaboration_messages.attempt_requires_simulated_kind",
                    "A collaboration message may only reference a Simulated attempt through this path."));
            }
        }
        else if (command.Actor is ParticipantKind.Codex or ParticipantKind.Claude)
        {
            return Result<RecordCollaborationMessageCommandResult>.Failure(Error.Conflict(
                "collaboration_messages.actor_requires_attempt",
                "An agent-provider actor requires an owning Agent attempt."));
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
            message.Actor,
            eventPayload,
            nowUtc);
        dbContext.Events.Add(runEvent);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<RecordCollaborationMessageCommandResult>.Success(
            new RecordCollaborationMessageCommandResult(message.Id, message.Sequence, runEvent.Sequence));
    }
}
