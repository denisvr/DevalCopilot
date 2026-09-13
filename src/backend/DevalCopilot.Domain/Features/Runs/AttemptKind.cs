namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// What kind of work an <see cref="Attempt"/> represents. <see cref="Simulated"/> is the
/// migration default so every attempt recorded before this kind existed is classified
/// correctly without a data backfill.
/// </summary>
public enum AttemptKind
{
    Simulated = 0,
    Process = 1,
}
