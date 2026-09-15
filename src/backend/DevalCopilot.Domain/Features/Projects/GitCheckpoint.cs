namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// Immutable, numbered source evidence captured from one Ready tool-owned workspace. The
/// fingerprint covers the resolved HEAD, Git's porcelain state, the complete tracked binary
/// diff, and hashes of every bounded untracked file. It is therefore a statement about the
/// observed source content, not merely a branch label or a timestamp.
/// </summary>
public sealed class GitCheckpoint
{
    private List<GitChangedFile> changedFiles = [];

    private GitCheckpoint()
    {
    }

    public static GitCheckpoint Capture(
        Guid id,
        Guid workspaceId,
        int checkpointNumber,
        DateTimeOffset capturedAtUtc,
        string headCommitSha,
        string fingerprintSha256,
        IReadOnlyCollection<GitChangedFile> changedFiles)
    {
        if (checkpointNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(checkpointNumber));
        }

        if (!IsSha(headCommitSha))
        {
            throw new ArgumentException("A checkpoint requires a full Git commit SHA.", nameof(headCommitSha));
        }

        if (!IsSha256(fingerprintSha256))
        {
            throw new ArgumentException("A checkpoint requires a SHA-256 fingerprint.", nameof(fingerprintSha256));
        }

        ArgumentNullException.ThrowIfNull(changedFiles);

        return new GitCheckpoint
        {
            Id = id,
            WorkspaceId = workspaceId,
            CheckpointNumber = checkpointNumber,
            CapturedAtUtc = capturedAtUtc,
            HeadCommitSha = headCommitSha,
            FingerprintSha256 = fingerprintSha256,
            changedFiles = changedFiles.ToList(),
        };
    }

    public Guid Id { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public int CheckpointNumber { get; private set; }

    public DateTimeOffset CapturedAtUtc { get; private set; }

    public string HeadCommitSha { get; private set; } = string.Empty;

    public string FingerprintSha256 { get; private set; } = string.Empty;

    public IReadOnlyCollection<GitChangedFile> ChangedFiles => changedFiles.AsReadOnly();

    private static bool IsSha(string value) =>
        value.Length == 40 && value.All(character => char.IsAsciiHexDigit(character));

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => char.IsAsciiHexDigit(character));
}
