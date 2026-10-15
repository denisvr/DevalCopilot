namespace DevalCopilot.Api.Features.Runs.GetGitHubRepositoryPermissionObservation;

/// <summary>The opaque repository node identifier and owner and repository names GitHub returned (equal to the recorded scope ignoring only
/// ASCII case), the host's observation time and GitHub's literal viewer permission: exactly one of READ, TRIAGE, TRIAGE_PLUS, WRITE,
/// MAINTAIN or ADMIN, or null exactly when GitHub returned an explicit null. A null permission is not a denial and not proof of an account
/// type, and no value is a derived push capability.</summary>
public sealed record GitHubRepositoryPermissionObservationResponse(
    string RepositoryNodeId, string Owner, string Repository, DateTimeOffset ObservedAtUtc, string? ViewerPermission);
