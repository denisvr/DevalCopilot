namespace DevalCopilot.Domain.Features.Projects;

/// <summary>One path-level fact from an immutable <see cref="GitCheckpoint"/>. Paths are
/// evidence supplied by Git, not executable input or a filesystem authority.</summary>
public sealed class GitChangedFile
{
    private GitChangedFile()
    {
    }

    public static GitChangedFile Observe(
        Guid id,
        Guid checkpointId,
        string path,
        string? previousPath,
        string indexStatus,
        string workTreeStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(indexStatus);
        ArgumentNullException.ThrowIfNull(workTreeStatus);

        // A literal space is Git porcelain's valid "no change in this column" marker, so
        // whitespace cannot be rejected here the way a path can.
        if (indexStatus.Length != 1 || workTreeStatus.Length != 1)
        {
            throw new ArgumentException("Git change status values must contain exactly one character.");
        }

        return new GitChangedFile
        {
            Id = id,
            CheckpointId = checkpointId,
            Path = path,
            PreviousPath = previousPath,
            IndexStatus = indexStatus,
            WorkTreeStatus = workTreeStatus,
        };
    }

    public Guid Id { get; private set; }

    public Guid CheckpointId { get; private set; }

    public string Path { get; private set; } = string.Empty;

    public string? PreviousPath { get; private set; }

    public string IndexStatus { get; private set; } = string.Empty;

    public string WorkTreeStatus { get; private set; } = string.Empty;
}
