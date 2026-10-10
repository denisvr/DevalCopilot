namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

public sealed record CleanupReport(bool Removed, string Reason)
{
    public static CleanupReport RemovedRoot { get; } = new(true, "Removed");

    public static CleanupReport Preserved(string reason) => new(false, reason);
}
