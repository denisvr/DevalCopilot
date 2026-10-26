namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationOutputWindow;

/// <summary>The closed outcome of one window. <see cref="Ok"/>: verified text of the recorded descriptor. <see cref="Unavailable"/>: the
/// execution's recorded facts, the stream's metadata or its stored path or hash could not be admitted. <see cref="NotRecorded"/>: no row
/// exists for that exact execution and stream. <see cref="Missing"/> and <see cref="IntegrityMismatch"/>: the artifact store's own
/// sealed-read refusals, kept distinct. No non-Ok outcome discloses a file length or a diagnostic.</summary>
public enum ProjectVerificationOutputWindowStatus
{
    Ok,
    Unavailable,
    NotRecorded,
    Missing,
    IntegrityMismatch,
}
