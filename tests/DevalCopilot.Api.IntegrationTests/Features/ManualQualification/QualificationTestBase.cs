using DevalCopilot.Api.IntegrationTests.ManualQualification;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>A fresh ledger directory per test, outside every real ledger, with tiny limits so the bounded waits finish at once.</summary>
public abstract class QualificationTestBase : IDisposable
{
    protected static readonly QualificationLimits Tiny = new(
        Readiness: TimeSpan.FromSeconds(1),
        Setup: TimeSpan.FromSeconds(1),
        Submission: TimeSpan.FromSeconds(1),
        Read: TimeSpan.FromSeconds(1),
        PollInterval: TimeSpan.FromMilliseconds(5),
        Stage: TimeSpan.FromMilliseconds(300),
        Shutdown: TimeSpan.FromSeconds(1),
        Overall: TimeSpan.FromSeconds(20));

    protected QualificationTestBase()
    {
        LedgerDirectory = Path.Combine(Path.GetTempPath(), $"devalcopilot-xunit-ledger-{Guid.NewGuid():N}");
        Ledger = new InvocationLedger(LedgerDirectory, TimeProvider.System);
    }

    protected string LedgerDirectory { get; }

    protected InvocationLedger Ledger { get; }

    public void Dispose()
    {
        if (Directory.Exists(LedgerDirectory))
        {
            Directory.Delete(LedgerDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    protected Task<QualificationReport> RunAsync(
        ScriptedEnvironment environment,
        string sessionId = "session-under-test",
        CancellationToken cancellationToken = default) =>
        new QualificationSession(Ledger, environment, Tiny, sessionId).RunAsync(cancellationToken);
}
