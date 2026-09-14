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
}
