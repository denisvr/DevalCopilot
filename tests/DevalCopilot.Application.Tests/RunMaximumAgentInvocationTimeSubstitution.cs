using System.Reflection;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Tests;

/// <summary>
/// Test-only helper that overwrites an already-recorded <see cref="Run"/>'s private-setter
/// <see cref="Run.MaximumAgentInvocationTime"/> back to <see langword="null"/>, purely to simulate
/// a historical Run that predates ADR-0013 without needing a raw-SQL insert bypassing
/// <see cref="Run.RecordIntent"/> (which always assigns a real default). Mirrors
/// <see cref="AttemptTimeoutSubstitution"/>. Never a production code path.
/// </summary>
internal static class RunMaximumAgentInvocationTimeSubstitution
{
    public static void SetNull(Run run)
    {
        var property = typeof(Run).GetProperty(nameof(Run.MaximumAgentInvocationTime))
            ?? throw new InvalidOperationException("Run has no MaximumAgentInvocationTime property.");
        var setter = property.GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException("Run.MaximumAgentInvocationTime has no setter.");
        setter.Invoke(run, [null]);
    }
}
