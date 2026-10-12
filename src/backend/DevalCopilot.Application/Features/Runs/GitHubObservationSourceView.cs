namespace DevalCopilot.Application.Features.Runs;

/// <summary>The exact recorded source a GitHub observation scope refers to: the operation, commit and branch of the run's completed
/// manual local delivery, derived from pinned rows and never from the current state of the repository (ADR-0040).</summary>
public sealed record GitHubObservationSourceView(Guid OperationId, string CommitSha, string BranchName);
