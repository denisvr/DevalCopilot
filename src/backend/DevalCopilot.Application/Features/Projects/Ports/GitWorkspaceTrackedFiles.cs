using System.Collections;

namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>
/// The owned, immutable snapshot of the tracked-file facts a capture carries across the Projects boundary (ADR-0024). It copies
/// whatever collection it was built from, holds only that private copy, and exposes read-only access through
/// <see cref="IReadOnlyList{T}"/> alone: it is neither an array nor a <see cref="List{T}"/> nor an <see cref="IList{T}"/>, so no cast of
/// the returned collection reaches a write path, and later changes to the collection it was copied from are never visible. The
/// per-file values are themselves immutable records. A holder therefore cannot replace attested text after the physical proof that
/// produced it, whichever reader or double supplied the facts.
/// </summary>
public sealed class GitWorkspaceTrackedFiles : IReadOnlyList<GitWorkspaceTrackedFile>
{
    private readonly GitWorkspaceTrackedFile[] files;

    private GitWorkspaceTrackedFiles(GitWorkspaceTrackedFile[] files) => this.files = files;

    public int Count => files.Length;

    public GitWorkspaceTrackedFile this[int index] => files[index];

    /// <summary>A private copy of <paramref name="source"/> (null stays null: no attestation at all). An already-owned snapshot is
    /// returned as is, because it cannot change. A null element is refused rather than carried inward.</summary>
    public static GitWorkspaceTrackedFiles? From(IEnumerable<GitWorkspaceTrackedFile>? source)
    {
        if (source is null)
        {
            return null;
        }

        if (source is GitWorkspaceTrackedFiles owned)
        {
            return owned;
        }

        var copy = source.ToArray();
        if (Array.IndexOf(copy, null) >= 0)
        {
            throw new ArgumentException("A tracked-file fact cannot be null.", nameof(source));
        }

        return new GitWorkspaceTrackedFiles(copy);
    }

    public IEnumerator<GitWorkspaceTrackedFile> GetEnumerator() => ((IEnumerable<GitWorkspaceTrackedFile>)files).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
