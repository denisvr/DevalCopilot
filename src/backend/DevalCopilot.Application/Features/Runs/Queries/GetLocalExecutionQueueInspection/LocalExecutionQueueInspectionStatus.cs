namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;

/// <summary><see cref="Empty"/>: the database records no Waiting, Reserved or Unresolved admission, which is neither an idle host nor free
/// capacity. <see cref="Recorded"/>: every outstanding admission proved coherent. <see cref="Unavailable"/>: a candidate, reference, bound
/// or historical proof defect, and nothing of it is shown.</summary>
public enum LocalExecutionQueueInspectionStatus
{
    Empty,
    Recorded,
    Unavailable,
}
