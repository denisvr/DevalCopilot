namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningImplementationAuthorization;

public enum PlanningImplementationAuthorizationState
{
    Absent = 0,
    Available = 1,
    Consumed = 2,
    Stale = 3,
    Invalid = 4,
}
