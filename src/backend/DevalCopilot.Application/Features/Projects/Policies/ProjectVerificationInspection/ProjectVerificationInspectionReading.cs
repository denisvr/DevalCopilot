namespace DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

internal sealed record ProjectVerificationInspectionReading(
    ProjectVerificationInspectionReadingKind Kind, ProjectVerificationRecordedExecution? Execution)
{
    public static readonly ProjectVerificationInspectionReading NotFound = new(ProjectVerificationInspectionReadingKind.NotFound, null);

    public static readonly ProjectVerificationInspectionReading Unavailable = new(ProjectVerificationInspectionReadingKind.Unavailable, null);

    public static ProjectVerificationInspectionReading Recorded(ProjectVerificationRecordedExecution execution) =>
        new(ProjectVerificationInspectionReadingKind.Recorded, execution);
}
