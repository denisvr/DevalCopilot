using System.Reflection;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Tests;

/// <summary>
/// Test-only helper that overwrites an already-claimed <see cref="Attempt"/>'s private-setter
/// <see cref="Attempt.AgentProvider"/>, purely to construct a role/provider combination no
/// production factory can produce today — every <c>ClaimAgent*</c> factory intentionally fixes
/// exactly one provider per role (Slice A). Used solely to prove Slice B.1's read-side eligibility
/// gates (the <c>Create*Attempt</c> handlers and their identity/deduplication helpers) authorize
/// upstream collaboration input by <see cref="AgentRole"/> alone, never by which provider actually
/// produced the attempt. Never a production code path.
/// </summary>
internal static class AttemptProviderSubstitution
{
    public static void SetProvider(Attempt attempt, AgentProvider provider) => SetProviderValue(attempt, provider);

    /// <summary>Sets AgentProvider to <see langword="null"/> or, via an out-of-range boxed
    /// <see cref="AgentProvider"/> value, an undefined enum member — purely to exercise this
    /// slice's fail-closed handling of malformed persisted state. Never reachable through any
    /// production factory or transition.</summary>
    public static void SetProvider(Attempt attempt, AgentProvider? provider) => SetProviderValue(attempt, provider);

    public static void SetUndefinedProvider(Attempt attempt) => SetProviderValue(attempt, (AgentProvider)999);

    private static void SetProviderValue(Attempt attempt, object? value)
    {
        var property = typeof(Attempt).GetProperty(nameof(Attempt.AgentProvider))
            ?? throw new InvalidOperationException("Attempt has no AgentProvider property.");
        var setter = property.GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException("Attempt.AgentProvider has no setter.");
        setter.Invoke(attempt, [value]);
    }
}
