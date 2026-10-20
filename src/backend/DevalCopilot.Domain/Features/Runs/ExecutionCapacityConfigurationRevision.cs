namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One immutable revision of the host-wide recorded-mutating admission limit (ADR-0059): a positive revision and the limit it set, one or
/// two. Revision one is seeded with the default of two by the migration; every explicit operator change appends exactly the next revision,
/// and the latest revision is the current setting. The rows are ordered configuration history, never a per-run fact, a grant, a pending
/// request or evidence that anything was released. A limit above two, a per-project or per-provider override and a non-positive revision
/// are not representable.
/// </summary>
public sealed class ExecutionCapacityConfigurationRevision
{
    public const int MinimumLimit = 1;

    public const int MaximumLimit = 2;

    /// <summary>The limit of the seeded first revision and the ceiling ADR-0006 and ADR-0058 established.</summary>
    public const int DefaultLimit = 2;

    private ExecutionCapacityConfigurationRevision()
    {
    }

    /// <summary>Whether <paramref name="limit"/> is one of the only two settings the host supports.</summary>
    public static bool IsSupportedLimit(int limit) => limit is >= MinimumLimit and <= MaximumLimit;

    /// <summary>The revision after <paramref name="current"/>, or null when it would overflow (a change is then refused without writing).</summary>
    public static long? NextRevisionAfter(long current) => current is < 1 or long.MaxValue ? null : current + 1;

    public static ExecutionCapacityConfigurationRevision Record(long revision, int limit)
    {
        if (revision < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), "A configuration revision is positive.");
        }

        if (!IsSupportedLimit(limit))
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "The recorded-mutating limit is one or two.");
        }

        return new ExecutionCapacityConfigurationRevision { Revision = revision, Limit = limit };
    }

    public long Revision { get; private set; }

    public int Limit { get; private set; }
}
