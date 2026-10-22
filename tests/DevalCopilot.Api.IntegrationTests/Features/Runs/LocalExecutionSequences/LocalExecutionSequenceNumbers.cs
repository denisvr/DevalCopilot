namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>Unique coordination numbers for sequences a test records directly, far above the small numbers the production counter hands
/// out, so a directly recorded sequence never collides with one that the host armed through the real operation.</summary>
internal static class LocalExecutionSequenceNumbers
{
    private static long last = 1_000_000;

    public static long Next() => Interlocked.Increment(ref last);
}
