using System.Reflection;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Tests;

/// <summary>
/// Test-only helper that overwrites an already-claimed <see cref="Attempt"/>'s private-setter
/// <see cref="Attempt.AgentTimeout"/>, purely to construct a persisted, non-positive timeout no
/// production factory can produce (every <c>ClaimAgent*</c> factory validates a strictly positive
/// timeout at claim time). Used solely to prove the run-wide invocation-time evidence's fail-closed
/// handling of malformed persisted state — mirrors <see cref="AttemptProviderSubstitution"/>. Never
/// a production code path.
/// </summary>
internal static class AttemptTimeoutSubstitution
{
    public static void SetTimeout(Attempt attempt, TimeSpan? timeout)
    {
        var property = typeof(Attempt).GetProperty(nameof(Attempt.AgentTimeout))
            ?? throw new InvalidOperationException("Attempt has no AgentTimeout property.");
        var setter = property.GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException("Attempt.AgentTimeout has no setter.");
        setter.Invoke(attempt, [timeout]);
    }
}
