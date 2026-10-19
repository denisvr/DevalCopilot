using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;

namespace DevalCopilot.Application.Features.Runs.Commands.ClaimPlanningSequenceStep;

/// <summary>
/// Tries the one claim of the step a coordinator observation just reserved (ADR-0055), by composing the existing claim command of the
/// step's fixed role through <c>IApplicationMediator</c> with the internal step identity that no MVC request can supply. Every
/// original manifest, source, model/effort, budget, token and account guard of that claim still applies, and the step link commits in
/// the very transaction that records the attempt. Manual transaction: the composed claim performs Git evidence capture and artifact
/// sealing, neither of which may run inside an ambient transaction. The handler writes nothing of its own, so a failed claim leaves
/// no tracked state behind for the coordinator's following resolution.
/// </summary>
public sealed record ClaimPlanningSequenceStepCommand(PlanningSequenceStepTicket Ticket)
    : IManualTransactionCommand<Result<ClaimPlanningSequenceStepCommandResult>>;
