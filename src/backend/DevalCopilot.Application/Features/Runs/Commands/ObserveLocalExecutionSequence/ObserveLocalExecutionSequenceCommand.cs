using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.ObserveLocalExecutionSequence;

/// <summary>
/// One bounded coordinator observation of one local execution sequence (ADR-0060), decided entirely from fresh database state inside one
/// short write-locked transaction and with no external work: it observes the preparation, result and advancement deadlines, drains a
/// stopping sequence, classifies the terminal operation of the claimed step, and either ends the sequence or concludes the step and
/// reserves the one preparation intent of its successor (or of the implementation step of an Armed sequence). The result carries the
/// internal ticket only when this very call reserved a step, so only that caller may try to claim it, once. It never claims, retries,
/// repairs, resumes or rearms.
/// </summary>
public sealed record ObserveLocalExecutionSequenceCommand(Guid SequenceId)
    : IManualTransactionCommand<Result<ObserveLocalExecutionSequenceCommandResult>>;
