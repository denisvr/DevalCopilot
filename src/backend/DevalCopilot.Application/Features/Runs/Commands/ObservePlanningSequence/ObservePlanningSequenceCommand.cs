using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.ObservePlanningSequence;

/// <summary>
/// One bounded coordinator observation of one planning sequence (ADR-0055), decided entirely from fresh database state inside one short
/// write-locked transaction and with no external work: it observes the preparation and result deadlines, drains a stopping sequence,
/// classifies the terminal attempt of the claimed step, and either ends the sequence or concludes the step and reserves the one
/// preparation intent of its successor (or of the Planner step of an Armed sequence). The result carries the internal ticket only when
/// this very call reserved a step, so only that caller may try to claim it, once. It never claims, retries, repairs, resumes or rearms.
/// </summary>
public sealed record ObservePlanningSequenceCommand(Guid SequenceId)
    : IManualTransactionCommand<Result<ObservePlanningSequenceCommandResult>>;
