using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceAvailability;

/// <summary>
/// The recorded arming availability of one explicitly selected plan for one run (ADR-0060): the exact candidate with its confirmation
/// value, or one fixed refusal code, in the same order the arming command decides. It reads recorded rows only (no Git or provider
/// observation and no execution), and arming always decides afresh. An unknown run fails with <c>local_execution_sequence.not_found</c>.
/// </summary>
public sealed record GetLocalExecutionSequenceAvailabilityQuery(
    Guid RunId, Guid PlanProposalMessageId, LocalExecutionAdmissionMode Mode = LocalExecutionAdmissionMode.Immediate)
    : IQuery<Result<GetLocalExecutionSequenceAvailabilityQueryResult>>;
