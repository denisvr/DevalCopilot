namespace DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

/// <summary><see cref="ProjectVerificationInspectionReadingKind.NotFound"/> is an unknown project; Unavailable carries no entry and no cursor.</summary>
internal sealed record ProjectVerificationHistoryReading(
    ProjectVerificationInspectionReadingKind Kind,
    IReadOnlyList<ProjectVerificationHistoryEntry> Entries,
    bool HasMore,
    int? NextBeforeExecutionNumber)
{
    public static readonly ProjectVerificationHistoryReading NotFound = new(ProjectVerificationInspectionReadingKind.NotFound, [], false, null);

    public static readonly ProjectVerificationHistoryReading Unavailable = new(ProjectVerificationInspectionReadingKind.Unavailable, [], false, null);
}
