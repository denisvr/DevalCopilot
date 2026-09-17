using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordSimulatedAgentStep;

public sealed class RecordSimulatedAgentStepCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordSimulatedAgentStepCommand, Result<RecordSimulatedAgentStepCommandResult>>
{
    public async Task<Result<RecordSimulatedAgentStepCommandResult>> HandleAsync(
        RecordSimulatedAgentStepCommand command,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.MessageType))
        {
            return Result<RecordSimulatedAgentStepCommandResult>.Failure(
                Error.Conflict("collaboration_messages.invalid", "The simulated collaboration message is not valid."));
        }

        var run = await dbContext.Runs
            .SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);

        if (run is null)
        {
            return Result<RecordSimulatedAgentStepCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        var attempt = await dbContext.Attempts
            .SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (attempt is null || attempt.RunId != run.Id)
        {
            return Result<RecordSimulatedAgentStepCommandResult>.Failure(
                Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        if (attempt.Status != AttemptStatus.Running)
        {
            return Result<RecordSimulatedAgentStepCommandResult>.Failure(
                Error.Conflict("attempts.not_active", $"The attempt is {attempt.Status} and cannot record a step."));
        }

        if (run.Lifecycle != RunLifecycle.Running)
        {
            return Result<RecordSimulatedAgentStepCommandResult>.Failure(
                Error.Conflict("runs.not_running", $"The run is {run.Lifecycle} and cannot advance."));
        }

        var messageId = Guid.NewGuid();
        if (command.InReplyToMessageId == messageId)
        {
            return Result<RecordSimulatedAgentStepCommandResult>.Failure(
                Error.Conflict("collaboration_messages.self_reply", "A collaboration message cannot reply to itself."));
        }

        var referenceViolation = CollaborationMessageReplyPolicy.EvaluateReference(
            command.MessageType,
            command.InReplyToMessageId.HasValue);
        if (referenceViolation != CollaborationMessageReplyViolation.None)
        {
            return Result<RecordSimulatedAgentStepCommandResult>.Failure(
                CollaborationMessageReplyErrors.Create(referenceViolation));
        }

        if (command.InReplyToMessageId.HasValue)
        {
            var reply = await dbContext.CollaborationMessages
                .SingleOrDefaultAsync(candidate => candidate.Id == command.InReplyToMessageId.Value, cancellationToken);
            if (reply is null || reply.RunId != run.Id)
            {
                return Result<RecordSimulatedAgentStepCommandResult>.Failure(
                    Error.NotFound("collaboration_messages.reply_not_found", "The referenced collaboration message was not found for this run."));
            }

            var parentViolation = CollaborationMessageReplyPolicy.Evaluate(command.MessageType, reply.Type);
            if (parentViolation != CollaborationMessageReplyViolation.None)
            {
                return Result<RecordSimulatedAgentStepCommandResult>.Failure(
                    CollaborationMessageReplyErrors.Create(parentViolation));
            }
        }

        var nowUtc = timeProvider.GetUtcNow();
        CollaborationMessage message;
        try
        {
            message = CollaborationMessage.Record(
                messageId,
                run.Id,
                attempt.Id,
                CollaborationMessage.ProtocolVersionOne,
                command.Actor,
                command.Recipient,
                command.MessageType,
                command.InReplyToMessageId,
                command.Summary,
                command.StructuredContentJson,
                CollaborationMessageProvenance.Simulated,
                nowUtc);
        }
        catch (ArgumentException)
        {
            return Result<RecordSimulatedAgentStepCommandResult>.Failure(
                Error.Conflict("collaboration_messages.invalid", "The simulated collaboration message is not valid."));
        }

        run.AdvanceStage(command.Stage, command.Actor, nowUtc);

        var payload = JsonSerializer.Serialize(new { summary = command.Summary });
        var runEvent = RunEvent.Record(
            Guid.NewGuid(), run.Id, command.AttemptId, command.EventType, command.Actor, payload, nowUtc);
        dbContext.Events.Add(runEvent);
        dbContext.CollaborationMessages.Add(message);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<RecordSimulatedAgentStepCommandResult>.Success(
            new RecordSimulatedAgentStepCommandResult(message.Id, runEvent.Sequence));
    }
}
