namespace DevalCopilot.Domain.Features.Projects;

public sealed class Project
{
    private Project()
    {
    }

    /// <summary>
    /// <paramref name="canonicalPath"/> is expected to already be Infrastructure-normalized
    /// (fully qualified, existence/reparse/UNC/filesystem-root policy already applied) —
    /// Domain performs no filesystem I/O and re-derives no path semantics; it only computes the
    /// pure, deterministic <see cref="RegistrationIdentityKey"/> string transform from it.
    /// That key exists solely to prevent duplicate <em>registration</em> of the same lexical
    /// path; per ADR-0007 it is explicitly non-authoritative for repository-mutation exclusion,
    /// writer leases, or worktree ownership. A future physical-identity capture must be
    /// introduced, and every existing registration backfilled with it, before any such
    /// mechanism may rely on repository identity.
    /// </summary>
    public static Project Register(Guid id, string name, string canonicalPath, DateTimeOffset registeredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);

        // A structural invariant, not filesystem I/O: Path.IsPathFullyQualified is pure string
        // parsing, the same class of operation as the ToUpperInvariant() transform below — the
        // deeper existence/reparse/UNC/filesystem-root policy remains Infrastructure's alone.
        if (!Path.IsPathFullyQualified(canonicalPath))
        {
            throw new ArgumentException("Canonical path must be fully qualified.", nameof(canonicalPath));
        }

        return new Project
        {
            Id = id,
            Name = name,
            CanonicalPath = canonicalPath,
            RegistrationIdentityKey = canonicalPath.ToUpperInvariant(),
            RegisteredAtUtc = registeredAtUtc,
            NextExecutionNumber = 1,
            NextBaselineNumber = 1,
            NextWorkspaceNumber = 1,
            PhysicalIdentityStatus = PhysicalIdentityStatus.Unresolved,
            PhysicalIdentityFailureReason = PhysicalIdentityFailureReason.None,
        };
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string CanonicalPath { get; private set; } = string.Empty;

    /// <summary>Registration-only duplicate-detection key. See the doc comment on
    /// <see cref="Register"/> and ADR-0007 before using this for anything else.</summary>
    public string RegistrationIdentityKey { get; private set; } = string.Empty;

    /// <summary>Null only for a legacy row that predates this slice's registration flow (the
    /// one-time migration honestly represents "unknown" rather than fabricating a historical
    /// date). Every project registered through <see cref="Register"/> always has a real
    /// value.</summary>
    public DateTimeOffset? RegisteredAtUtc { get; private set; }

    public int NextExecutionNumber { get; private set; }

    public int NextBaselineNumber { get; private set; }

    public int NextWorkspaceNumber { get; private set; }

    /// <summary>Windows physical repository identity — volume serial number plus 128-bit file
    /// ID (<c>FILE_ID_INFO</c>), resolved via <c>GetFileInformationByHandleEx</c>. Null until
    /// <see cref="PhysicalIdentityStatus"/> is <see cref="Projects.PhysicalIdentityStatus.Resolved"/>.
    /// This — never <see cref="RegistrationIdentityKey"/> — is the authoritative key a
    /// <see cref="RepositoryMutationLease"/> is keyed by. See ADR-0007 and ADR-0008.</summary>
    public ulong? PhysicalVolumeSerialNumber { get; private set; }

    /// <summary>16 bytes when set. Null until resolved.</summary>
    public byte[]? PhysicalFileId { get; private set; }

    public PhysicalIdentityStatus PhysicalIdentityStatus { get; private set; }

    /// <summary>Meaningful only when <see cref="PhysicalIdentityStatus"/> is
    /// <see cref="Projects.PhysicalIdentityStatus.Unavailable"/>; <see cref="PhysicalIdentityFailureReason.None"/>
    /// otherwise. A closed, safe reason code — never raw Win32 error text.</summary>
    public PhysicalIdentityFailureReason PhysicalIdentityFailureReason { get; private set; }

    public int ReserveExecutionNumber()
    {
        var executionNumber = NextExecutionNumber;
        NextExecutionNumber++;

        return executionNumber;
    }

    /// <summary>Reserves the next monotonic <see cref="RepositoryBaseline.BaselineNumber"/> for
    /// this project — the exact same in-aggregate-counter, single-transaction pattern already
    /// proven safe by <see cref="ReserveExecutionNumber"/>, plus a DB-level unique
    /// <c>(ProjectId, BaselineNumber)</c> index as a defense-in-depth backstop.</summary>
    public int ReserveBaselineNumber()
    {
        var baselineNumber = NextBaselineNumber;
        NextBaselineNumber++;

        return baselineNumber;
    }

    /// <summary>Reserves the next monotonic <see cref="GitWorkspace.WorkspaceNumber"/> for this
    /// project — never reused, so a retired workspace's deterministic path/branch name is never
    /// recreated identically. Same in-aggregate-counter pattern as
    /// <see cref="ReserveBaselineNumber"/>.</summary>
    public int ReserveWorkspaceNumber()
    {
        var workspaceNumber = NextWorkspaceNumber;
        NextWorkspaceNumber++;

        return workspaceNumber;
    }

    /// <summary>True only when physical identity is already <see cref="Projects.PhysicalIdentityStatus.Resolved"/>
    /// and matches the given tuple exactly. Application must consult this before deciding
    /// whether a fresh resolution is a confirming re-check or a genuine mismatch.</summary>
    public bool PhysicalIdentityMatches(ulong volumeSerialNumber, byte[] fileId)
    {
        ArgumentNullException.ThrowIfNull(fileId);

        return PhysicalIdentityStatus == PhysicalIdentityStatus.Resolved
            && PhysicalVolumeSerialNumber == volumeSerialNumber
            && PhysicalFileId is not null
            && PhysicalFileId.AsSpan().SequenceEqual(fileId);
    }

    /// <summary>Persists a successful physical-identity resolution. Refuses (a genuine
    /// programmer-error guard, not an expected business outcome) to silently overwrite an
    /// already-<see cref="Projects.PhysicalIdentityStatus.Resolved"/> project with a different
    /// tuple — the caller must check <see cref="PhysicalIdentityMatches"/> first and surface a
    /// typed conflict instead of calling this when it returns false.</summary>
    public void RecordPhysicalIdentityResolved(ulong volumeSerialNumber, byte[] fileId)
    {
        ArgumentNullException.ThrowIfNull(fileId);

        if (PhysicalIdentityStatus == PhysicalIdentityStatus.Resolved && !PhysicalIdentityMatches(volumeSerialNumber, fileId))
        {
            throw new InvalidOperationException(
                "A resolved physical identity is never silently overwritten by a different one.");
        }

        PhysicalVolumeSerialNumber = volumeSerialNumber;
        PhysicalFileId = fileId;
        PhysicalIdentityStatus = PhysicalIdentityStatus.Resolved;
        PhysicalIdentityFailureReason = PhysicalIdentityFailureReason.None;
    }

    /// <summary>Persists a failed physical-identity resolution with a closed, safe reason.
    /// Refuses to demote an already-<see cref="Projects.PhysicalIdentityStatus.Resolved"/>
    /// project on one transient failure — the caller must not invoke this once resolved; a
    /// recheck that cannot currently verify a resolved identity returns its own typed outcome
    /// without persisting anything.</summary>
    public void RecordPhysicalIdentityUnavailable(PhysicalIdentityFailureReason reason)
    {
        if (reason == PhysicalIdentityFailureReason.None)
        {
            throw new ArgumentException("A failure reason is required.", nameof(reason));
        }

        if (PhysicalIdentityStatus == PhysicalIdentityStatus.Resolved)
        {
            throw new InvalidOperationException(
                "An already-resolved physical identity is never demoted by a failed recheck.");
        }

        PhysicalIdentityStatus = PhysicalIdentityStatus.Unavailable;
        PhysicalIdentityFailureReason = reason;
    }
}
