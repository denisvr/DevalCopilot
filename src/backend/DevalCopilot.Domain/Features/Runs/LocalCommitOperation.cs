namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One explicit, host-executed local commit of an exact, verified, Agent-reviewed and human-approved checkpoint (ADR-0029).
/// At most one operation is admitted per run. Every identifying fact is immutable once prepared; only the status moves, through
/// guarded transitions that never turn an ambiguous state into a success or a failure by assumption. The operation owns no
/// Agent budget, grant or provider observation.
/// </summary>
public sealed class LocalCommitOperation
{
    public const int MaximumChangedPaths = 128;

    public const int MaximumBytesPerFile = 256 * 1024;

    public const long MaximumTotalBytes = 4L * 1024 * 1024;

    private List<LocalCommitAuthorityMember> members = [];

    private LocalCommitOperation()
    {
    }

    /// <summary>Everything the host derived before branch mutation. The commit, tree, index and metadata facts are exactly what
    /// the executor and recovery must later prove.</summary>
    public sealed record PreparedFacts(
        Guid Id,
        Guid RunId,
        Guid ProjectId,
        Guid GitWorkspaceId,
        Guid RepositoryMutationLeaseId,
        Guid GitCheckpointId,
        int CheckpointNumber,
        string CheckpointFingerprintSha256,
        Guid CodeReviewAttemptId,
        Guid CodeReviewApprovalMessageId,
        Guid ExecutionReportMessageId,
        Guid AgentCheckpointReviewId,
        Guid HumanCheckpointReviewId,
        string RequestSha256,
        string AuthoritySha256,
        string NormalizedMessage,
        string BranchName,
        string ParentCommitSha,
        string TreeSha,
        string CommitSha,
        string AuthorName,
        string AuthorEmail,
        long CommitTimeUnixSeconds,
        string IndexPreimageSha256,
        string PreparedIndexSha256,
        string PreparedIndexRelativePath,
        int ChangedPathCount,
        long TotalBytes,
        DateTimeOffset CreatedAtUtc);

    public static LocalCommitOperation Prepare(PreparedFacts facts, IReadOnlyCollection<LocalCommitAuthorityMember> authority)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(authority);

        if (new[]
            {
                facts.Id, facts.RunId, facts.ProjectId, facts.GitWorkspaceId, facts.RepositoryMutationLeaseId, facts.GitCheckpointId,
                facts.CodeReviewAttemptId, facts.CodeReviewApprovalMessageId, facts.ExecutionReportMessageId,
                facts.AgentCheckpointReviewId, facts.HumanCheckpointReviewId,
            }.Any(identifier => identifier == Guid.Empty))
        {
            throw new ArgumentException("A local-commit operation requires durable identifiers.", nameof(facts));
        }

        if (facts.CheckpointNumber < 1 || facts.ChangedPathCount is < 1 or > MaximumChangedPaths
            || facts.TotalBytes is < 0 or > MaximumTotalBytes)
        {
            throw new ArgumentException("A local-commit operation requires bounded, nonempty changes.", nameof(facts));
        }

        if (!IsSha256(facts.CheckpointFingerprintSha256) || !IsSha256(facts.RequestSha256) || !IsSha256(facts.AuthoritySha256)
            || !IsSha256(facts.IndexPreimageSha256) || !IsSha256(facts.PreparedIndexSha256)
            || !IsObjectId(facts.ParentCommitSha) || !IsObjectId(facts.TreeSha) || !IsObjectId(facts.CommitSha))
        {
            throw new ArgumentException("A local-commit operation requires exact object and content identities.", nameof(facts));
        }

        if (!LocalCommitMessagePolicy.TryNormalize(facts.NormalizedMessage, out var normalized)
            || !string.Equals(normalized, facts.NormalizedMessage, StringComparison.Ordinal))
        {
            throw new ArgumentException("A local-commit operation requires a normalized message.", nameof(facts));
        }

        if (string.IsNullOrWhiteSpace(facts.BranchName) || string.IsNullOrWhiteSpace(facts.AuthorName)
            || string.IsNullOrWhiteSpace(facts.AuthorEmail) || string.IsNullOrWhiteSpace(facts.PreparedIndexRelativePath))
        {
            throw new ArgumentException("A local-commit operation requires its branch, author and prepared artifact.", nameof(facts));
        }

        if (authority.Count == 0
            || authority.Any(member => member.OperationId != facts.Id)
            || authority.Select(member => (member.Kind, member.Sequence)).Distinct().Count() != authority.Count)
        {
            throw new ArgumentException("A local-commit operation requires its coherent authority membership.", nameof(authority));
        }

        return new LocalCommitOperation
        {
            Id = facts.Id,
            RunId = facts.RunId,
            ProjectId = facts.ProjectId,
            GitWorkspaceId = facts.GitWorkspaceId,
            RepositoryMutationLeaseId = facts.RepositoryMutationLeaseId,
            GitCheckpointId = facts.GitCheckpointId,
            CheckpointNumber = facts.CheckpointNumber,
            CheckpointFingerprintSha256 = facts.CheckpointFingerprintSha256,
            CodeReviewAttemptId = facts.CodeReviewAttemptId,
            CodeReviewApprovalMessageId = facts.CodeReviewApprovalMessageId,
            ExecutionReportMessageId = facts.ExecutionReportMessageId,
            AgentCheckpointReviewId = facts.AgentCheckpointReviewId,
            HumanCheckpointReviewId = facts.HumanCheckpointReviewId,
            RequestSha256 = facts.RequestSha256,
            AuthoritySha256 = facts.AuthoritySha256,
            NormalizedMessage = facts.NormalizedMessage,
            BranchName = facts.BranchName,
            ParentCommitSha = facts.ParentCommitSha,
            TreeSha = facts.TreeSha,
            CommitSha = facts.CommitSha,
            AuthorName = facts.AuthorName,
            AuthorEmail = facts.AuthorEmail,
            CommitTimeUnixSeconds = facts.CommitTimeUnixSeconds,
            IndexPreimageSha256 = facts.IndexPreimageSha256,
            PreparedIndexSha256 = facts.PreparedIndexSha256,
            PreparedIndexRelativePath = facts.PreparedIndexRelativePath,
            ChangedPathCount = facts.ChangedPathCount,
            TotalBytes = facts.TotalBytes,
            Status = LocalCommitStatus.Prepared,
            CreatedAtUtc = facts.CreatedAtUtc,
            members = authority.ToList(),
        };
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid GitWorkspaceId { get; private set; }

    public Guid RepositoryMutationLeaseId { get; private set; }

    public Guid GitCheckpointId { get; private set; }

    public int CheckpointNumber { get; private set; }

    public string CheckpointFingerprintSha256 { get; private set; } = string.Empty;

    public Guid CodeReviewAttemptId { get; private set; }

    public Guid CodeReviewApprovalMessageId { get; private set; }

    public Guid ExecutionReportMessageId { get; private set; }

    public Guid AgentCheckpointReviewId { get; private set; }

    public Guid HumanCheckpointReviewId { get; private set; }

    public string RequestSha256 { get; private set; } = string.Empty;

    /// <summary>The digest of the complete authority identity, including the whole human decision set, compared again at the
    /// execution seam.</summary>
    public string AuthoritySha256 { get; private set; } = string.Empty;

    public string NormalizedMessage { get; private set; } = string.Empty;

    public string BranchName { get; private set; } = string.Empty;

    public string ParentCommitSha { get; private set; } = string.Empty;

    public string TreeSha { get; private set; } = string.Empty;

    public string CommitSha { get; private set; } = string.Empty;

    public string AuthorName { get; private set; } = string.Empty;

    public string AuthorEmail { get; private set; } = string.Empty;

    /// <summary>UTC seconds since the Unix epoch, used for both author and committer dates.</summary>
    public long CommitTimeUnixSeconds { get; private set; }

    /// <summary>SHA-256 of the worktree's real index file as it stood when the prepared index was built.</summary>
    public string IndexPreimageSha256 { get; private set; } = string.Empty;

    public string PreparedIndexSha256 { get; private set; } = string.Empty;

    /// <summary>Relative to the host's local-commit artifact root; the artifact is owned and bounded.</summary>
    public string PreparedIndexRelativePath { get; private set; } = string.Empty;

    public int ChangedPathCount { get; private set; }

    public long TotalBytes { get; private set; }

    public LocalCommitStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>The single-use execution marker: set once, before the first ref or index change, and never cleared.</summary>
    public DateTimeOffset? ExecutionStartedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>A fixed, safe reason code for a non-successful or ambiguous outcome; never an exception message or a path.</summary>
    public string? OutcomeReasonCode { get; private set; }

    /// <summary>Physical observations committed after exclusive acquisition and before the ref mutation. They describe live
    /// handles held by the current host, not a restart-time authority to adopt a pathname.</summary>
    public string? IndexAdministrativeDirectoryIdentity { get; private set; }

    public string? IndexPreimageIdentity { get; private set; }

    public long? IndexPreimageLength { get; private set; }

    public string? PreparedIndexArtifactIdentity { get; private set; }

    public long? PreparedIndexArtifactLength { get; private set; }

    public string? IndexLockIdentity { get; private set; }

    public long? IndexLockLength { get; private set; }

    public DateTimeOffset? IndexAcquiredAtUtc { get; private set; }

    /// <summary>The operation-derived same-directory name selected and committed before the first index rename.</summary>
    public string? IndexQuarantineName { get; private set; }

    public DateTimeOffset? IndexReplacementPlannedAtUtc { get; private set; }

    public IReadOnlyCollection<LocalCommitAuthorityMember> Members => members.AsReadOnly();

    public string CommitMessage => LocalCommitMessagePolicy.BuildCommitMessage(NormalizedMessage, Id);

    public bool IsTerminal => Status is LocalCommitStatus.Completed or LocalCommitStatus.Failed or LocalCommitStatus.Interrupted;

    public void RecordIndexAcquisition(
        string administrativeDirectoryIdentity,
        string preimageIdentity,
        long preimageLength,
        string artifactIdentity,
        long artifactLength,
        string lockIdentity,
        long lockLength,
        DateTimeOffset nowUtc)
    {
        if (Status != LocalCommitStatus.Executing || IndexAcquiredAtUtc is not null
            || !IsPhysicalIdentity(administrativeDirectoryIdentity) || !IsPhysicalIdentity(preimageIdentity)
            || !IsPhysicalIdentity(artifactIdentity) || !IsPhysicalIdentity(lockIdentity)
            || preimageLength is < 0 or > 16L * 1024 * 1024
            || artifactLength is < 0 or > 16L * 1024 * 1024
            || lockLength is < 0 or > 16L * 1024 * 1024)
        {
            throw new InvalidOperationException("Cannot record an invalid local-commit index acquisition.");
        }

        IndexAdministrativeDirectoryIdentity = administrativeDirectoryIdentity;
        IndexPreimageIdentity = preimageIdentity;
        IndexPreimageLength = preimageLength;
        PreparedIndexArtifactIdentity = artifactIdentity;
        PreparedIndexArtifactLength = artifactLength;
        IndexLockIdentity = lockIdentity;
        IndexLockLength = lockLength;
        IndexAcquiredAtUtc = nowUtc;
    }

    public void PlanIndexReplacement(string quarantineName, DateTimeOffset nowUtc)
    {
        if (Status != LocalCommitStatus.Executing || IndexAcquiredAtUtc is null || IndexReplacementPlannedAtUtc is not null
            || string.IsNullOrWhiteSpace(quarantineName) || quarantineName != Path.GetFileName(quarantineName)
            || quarantineName.Length > 240)
        {
            throw new InvalidOperationException("Cannot plan an invalid local-commit index replacement.");
        }

        IndexQuarantineName = quarantineName;
        IndexReplacementPlannedAtUtc = nowUtc;
    }

    /// <summary>Records a replacement acquisition made after process loss. This is deliberately narrower than the first
    /// acquisition: the original plan must already be durable, and the old receipt is replaced only while no outcome has been
    /// inferred. A file ID is evidence of this new live handle, never revival of the old capability.</summary>
    public void RenewIndexAcquisitionForRecovery(
        string administrativeDirectoryIdentity,
        string preimageIdentity,
        long preimageLength,
        string artifactIdentity,
        long artifactLength,
        string lockIdentity,
        long lockLength,
        DateTimeOffset nowUtc)
    {
        if (Status != LocalCommitStatus.Executing || IndexAcquiredAtUtc is null
            || IndexReplacementPlannedAtUtc is null || string.IsNullOrWhiteSpace(IndexQuarantineName)
            || !IsPhysicalIdentity(administrativeDirectoryIdentity) || !IsPhysicalIdentity(preimageIdentity)
            || !IsPhysicalIdentity(artifactIdentity) || !IsPhysicalIdentity(lockIdentity)
            || preimageLength is < 0 or > 16L * 1024 * 1024
            || artifactLength is < 0 or > 16L * 1024 * 1024
            || lockLength is < 0 or > 16L * 1024 * 1024)
        {
            throw new InvalidOperationException("Cannot renew an invalid local-commit index acquisition.");
        }

        IndexAdministrativeDirectoryIdentity = administrativeDirectoryIdentity;
        IndexPreimageIdentity = preimageIdentity;
        IndexPreimageLength = preimageLength;
        PreparedIndexArtifactIdentity = artifactIdentity;
        PreparedIndexArtifactLength = artifactLength;
        IndexLockIdentity = lockIdentity;
        IndexLockLength = lockLength;
        IndexAcquiredAtUtc = nowUtc;
    }

    public void MarkExecuting(DateTimeOffset nowUtc)
    {
        if (Status != LocalCommitStatus.Prepared || ExecutionStartedAtUtc is not null)
        {
            throw new InvalidOperationException($"Cannot start a local commit that is {Status}.");
        }

        Status = LocalCommitStatus.Executing;
        ExecutionStartedAtUtc = nowUtc;
    }

    public void Complete(DateTimeOffset nowUtc)
    {
        if (Status is not (LocalCommitStatus.Executing or LocalCommitStatus.NeedsAttention) || ExecutionStartedAtUtc is null)
        {
            throw new InvalidOperationException($"Cannot complete a local commit that is {Status}.");
        }

        Status = LocalCommitStatus.Completed;
        CompletedAtUtc = nowUtc;
        OutcomeReasonCode = null;
    }

    /// <summary>A definitely unpromoted failure: the branch and index are proven unchanged.</summary>
    public void Fail(string reasonCode, DateTimeOffset nowUtc)
    {
        RequireOpen(reasonCode);
        Status = LocalCommitStatus.Failed;
        CompletedAtUtc = nowUtc;
        OutcomeReasonCode = reasonCode;
    }

    /// <summary>A startup interruption proven unpromoted.</summary>
    public void Interrupt(string reasonCode, DateTimeOffset nowUtc)
    {
        RequireOpen(reasonCode);
        Status = LocalCommitStatus.Interrupted;
        CompletedAtUtc = nowUtc;
        OutcomeReasonCode = reasonCode;
    }

    /// <summary>Ambiguity: neither promoted nor proven unpromoted. Never terminal.</summary>
    public void MarkNeedsAttention(string reasonCode)
    {
        RequireOpen(reasonCode);
        Status = LocalCommitStatus.NeedsAttention;
        OutcomeReasonCode = reasonCode;
    }

    internal static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    public static bool IsObjectId(string? value) =>
        value is { Length: 40 } && value.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private void RequireOpen(string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        if (IsTerminal)
        {
            throw new InvalidOperationException($"Cannot change a local commit that is {Status}.");
        }
    }

    private static bool IsPhysicalIdentity(string? value) =>
        value is { Length: 49 } && value[16] == ':' && value.Where((_, index) => index != 16)
            .All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
