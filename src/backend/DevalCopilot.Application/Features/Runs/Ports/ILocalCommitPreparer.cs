namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Builds, outside any database transaction, the immutable commit for an approved checkpoint (ADR-0029): a complete bounded
/// snapshot read through physically proven handles, an isolated index initialized from the exact parent, raw blobs written
/// through standard input, the tree, attribute and conversion checks against that immutable tree, and one unsigned commit object.
/// It never touches a branch ref, the real index or a working file. Unreachable objects it leaves behind grant no authority.
/// </summary>
public interface ILocalCommitPreparer
{
    Task<LocalCommitPreparationResult> PrepareAsync(LocalCommitPreparationRequest request, CancellationToken cancellationToken);
}
