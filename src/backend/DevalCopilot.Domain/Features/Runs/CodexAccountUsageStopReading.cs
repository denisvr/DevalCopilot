namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The exact reading of one persisted account-usage-stop column. <see cref="IsMalformed"/> means the stored representation is not a
/// canonical whole number inside the accepted range; it is never read as a valid setting, never as null or zero, and never clamped.
/// <see cref="Value"/> is non-null only for a valid setting.
/// </summary>
public readonly record struct CodexAccountUsageStopReading(bool IsMalformed, int? Value)
{
    public static CodexAccountUsageStopReading Absent => new(false, null);

    public static CodexAccountUsageStopReading Malformed => new(true, null);

    public bool IsAbsent => !IsMalformed && Value is null;
}
