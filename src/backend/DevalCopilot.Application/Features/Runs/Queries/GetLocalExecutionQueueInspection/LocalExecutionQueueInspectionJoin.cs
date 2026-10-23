using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;

/// <summary>The joined entities of one global row before projection; never materialized. Initializer members so the query translator can
/// bind each member to its joined entity.</summary>
internal sealed class LocalExecutionQueueInspectionJoin
{
    public required LocalExecutionAdmission Candidate { get; init; }

    public required Project Project { get; init; }

    public required LocalExecutionAdmissionHistoryJoin History { get; init; }
}
