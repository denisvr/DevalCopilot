namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The exact reading of the persisted account-usage-warning column. <see cref="IsMalformed"/> means the stored representation is not a
/// canonical whole number inside the accepted range; it is never read as a valid setting, never as null or zero, and never clamped.
/// <see cref="Value"/> is non-null only for a valid setting.
/// </summary>
public readonly record struct CodexAccountUsageWarningReading(bool IsMalformed, int? Value)
{
    public static CodexAccountUsageWarningReading Absent => new(false, null);

    public static CodexAccountUsageWarningReading Malformed => new(true, null);

    public bool IsAbsent => !IsMalformed && Value is null;
}
