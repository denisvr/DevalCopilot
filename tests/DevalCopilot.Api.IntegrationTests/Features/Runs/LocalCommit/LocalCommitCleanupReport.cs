namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>What one real cleanup call did, reported only after it returned or ended. <see cref="Result"/> is the value the real
/// cleanup returned; it is null when the call ended without returning one (an exception or a cancellation), so a failed or refused
/// cleanup can never be read as a successful removal.</summary>
internal sealed record LocalCommitCleanupReport(
    Guid OperationId, string ArtifactRelativePath, bool RemoveOwnedLock, bool? Result)
{
    public bool Succeeded => Result == true;
}
