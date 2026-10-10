namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>Every wait the session performs is bounded by one of these values. The stage bound covers the unchanged ten-minute
/// provider timeout plus the host's own claim, recording and reconciliation time; the overall bound covers both stages.</summary>
public sealed record QualificationLimits(
    TimeSpan Readiness,
    TimeSpan Setup,
    TimeSpan Submission,
    TimeSpan Read,
    TimeSpan PollInterval,
    TimeSpan Stage,
    TimeSpan Shutdown,
    TimeSpan Overall)
{
    public static QualificationLimits Production { get; } = new(
        Readiness: TimeSpan.FromSeconds(90),
        Setup: TimeSpan.FromSeconds(120),
        Submission: TimeSpan.FromSeconds(60),
        Read: TimeSpan.FromSeconds(20),
        PollInterval: TimeSpan.FromSeconds(3),
        Stage: TimeSpan.FromMinutes(12),
        Shutdown: TimeSpan.FromSeconds(60),
        Overall: TimeSpan.FromMinutes(32));
}
