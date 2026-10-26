using Devalente.Shared.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetProjectVerificationHistory;

/// <summary>The history page accepts only the optional <c>beforeExecutionNumber</c> query key.</summary>
public sealed class ProjectVerificationHistoryGuardFilter(IResultProblemDetailsFactory problemDetails)
    : ProjectVerificationInspectionGuardFilter(problemDetails, [ProjectVerificationBeforeExecutionNumberModelBinder.QueryName]);
