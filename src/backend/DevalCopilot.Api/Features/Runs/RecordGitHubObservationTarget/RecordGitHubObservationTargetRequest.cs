namespace DevalCopilot.Api.Features.Runs.RecordGitHubObservationTarget;

/// <summary>The only caller-supplied facts of an explicit GitHub observation scope selection: the completed local-commit operation, its
/// exact lowercase 40-character commit, and the owner and repository, each used exactly as given (no trimming, URL, host, branch or
/// credential). The host derives the project, workspace and branch from pinned rows and fixes the host and purpose.</summary>
public sealed record RecordGitHubObservationTargetRequest
{
    public required Guid OperationId { get; init; }

    public required string CommitSha { get; init; }

    public required string Owner { get; init; }

    public required string Repository { get; init; }
}
