using Devalente.Shared.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationHistory;

/// <summary>The selected execution accepts no query key at all.</summary>
public sealed class ProjectVerificationDetailGuardFilter(IResultProblemDetailsFactory problemDetails)
    : ProjectVerificationInspectionGuardFilter(problemDetails, []);
