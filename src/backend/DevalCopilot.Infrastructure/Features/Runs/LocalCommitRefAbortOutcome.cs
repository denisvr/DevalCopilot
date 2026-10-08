namespace DevalCopilot.Infrastructure.Features.Runs;

internal enum LocalCommitRefAbortOutcome
{
    /// <summary>The exact <c>abort: ok</c> acknowledgement arrived and the child exited by itself with code zero after stdin
    /// closed, without termination. Git released its own prepared lock; the caller still proves safe release.</summary>
    Confirmed,

    /// <summary>The abort could not be proven (timeout, malformed output, termination, nonzero exit). A prepared lock may remain
    /// and is never removed by path.</summary>
    Unconfirmed,
}
