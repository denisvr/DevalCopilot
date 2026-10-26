using Devalente.Shared.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationHistory;

/// <summary>A sealed window accepts only the optional <c>fromOffset</c> and <c>maxBytes</c> query keys.</summary>
public sealed class ProjectVerificationWindowGuardFilter(IResultProblemDetailsFactory problemDetails)
    : ProjectVerificationInspectionGuardFilter(
        problemDetails,
        [ProjectVerificationOffsetModelBinder.QueryName, ProjectVerificationMaxBytesModelBinder.QueryName]);
