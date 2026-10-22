namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionStepInspection;

/// <summary><see cref="Recorded"/>: the step and its proved Agent header or verification. <see cref="NoOperation"/>: only the coherent
/// Reserved, Revoked or Unresolved shape with no recorded link; nothing is guessed about a reserved operation. <see cref="Unavailable"/>:
/// the structure or the linked operation cannot be proved.</summary>
public enum LocalExecutionStepInspectionStatus
{
    Recorded,
    NoOperation,
    Unavailable,
}
