namespace DevalCopilot.Api.Startup;

/// <summary>ADR-0072: the typed outcome of startup recovery: one fatal failure, or success with zero or more bounded warnings.</summary>
public sealed record StartupRecoveryReport(StartupFailure? Failure, IReadOnlyList<StartupWarningCategory> Warnings)
{
    public static StartupRecoveryReport Clean { get; } = new(null, []);

    public bool IsFatal => Failure is not null;
}
