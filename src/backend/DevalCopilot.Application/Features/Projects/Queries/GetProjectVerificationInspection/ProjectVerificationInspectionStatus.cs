namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationInspection;

/// <summary><see cref="Recorded"/>: the execution's ownership and complete supported result tuple are proved. <see cref="Unavailable"/>: the
/// execution belongs to the project but its recorded facts are incoherent; no fact is carried.</summary>
public enum ProjectVerificationInspectionStatus
{
    Recorded,
    Unavailable,
}
