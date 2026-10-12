namespace DevalCopilot.Api.Features.Runs;

/// <summary>The exact recorded operation, commit and branch of a run's completed local delivery that a GitHub observation scope refers
/// to (ADR-0040). Derived from pinned rows, never from the repository's current state.</summary>
public sealed record GitHubObservationSourceResponse(Guid OperationId, string CommitSha, string BranchName);
