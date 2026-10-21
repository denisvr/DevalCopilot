using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

namespace DevalCopilot.Application.Features.Runs.Commands.ClaimLocalExecutionSequenceStep;

/// <summary>
/// Tries the one claim of the step a coordinator observation just reserved (ADR-0060), by composing the existing claim command of the
/// step's fixed kind through <c>IApplicationMediator</c> with the internal step identity that no MVC request can supply. Every original
/// manifest, source, model/effort, budget, token, account, workspace and capacity guard of that claim still applies, and the step link
/// commits in the very transaction that records the operation. Manual transaction: the composed claim performs Git evidence capture and
/// artifact sealing, neither of which may run inside an ambient transaction. The handler writes nothing of its own.
/// </summary>
public sealed record ClaimLocalExecutionSequenceStepCommand(LocalExecutionStepTicket Ticket)
    : IManualTransactionCommand<Result<ClaimLocalExecutionSequenceStepCommandResult>>;
