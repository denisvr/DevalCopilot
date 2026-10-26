using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequenceInspection;

/// <summary>
/// The recorded index of one run's planning sequence (ADR-0068): the stored bookkeeping header and every recorded step with its recorded
/// link, read from bounded untracked scalar facts and proved structurally as a whole. It invokes no live status, arming, classifier,
/// admission, fence or mutation handler and reads no current lease, latest workspace or checkpoint, message content, Git, filesystem,
/// process, provider or account state. An unknown run fails with <c>planning_sequence_inspection.not_found</c>.
/// </summary>
public sealed record GetPlanningSequenceInspectionQuery(Guid RunId) : IQuery<Result<GetPlanningSequenceInspectionQueryResult>>;
