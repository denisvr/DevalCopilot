namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;

/// <summary>The registered project a candidate's run belongs to, as one row read it: a null identity is the absence of that project, and a
/// null name is an absent or oversized stored name (more than the byte guard) that the proof refuses.</summary>
internal sealed record LocalExecutionQueueInspectionProject(Guid? Id, string? Name);
