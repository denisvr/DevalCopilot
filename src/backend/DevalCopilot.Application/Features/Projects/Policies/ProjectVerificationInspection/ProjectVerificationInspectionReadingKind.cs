namespace DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

/// <summary><see cref="NotFound"/>: an unknown project, or an execution that is absent or belongs to another project, decided by a database
/// membership predicate over the stored identities. <see cref="Unavailable"/>: the execution is a member of the project but its recorded
/// facts are incoherent. <see cref="Recorded"/>: every ownership and result fact is proved.</summary>
internal enum ProjectVerificationInspectionReadingKind
{
    NotFound,
    Unavailable,
    Recorded,
}
