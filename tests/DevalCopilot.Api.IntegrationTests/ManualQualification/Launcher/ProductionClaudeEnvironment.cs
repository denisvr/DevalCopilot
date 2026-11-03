using System.Reflection;
using DevalCopilot.Infrastructure.Features.Processes;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The actual production environment of a Claude <c>--print</c> invocation, obtained by calling the production builder itself with its
/// own current ambient inputs, so the map is never duplicated here and cannot drift from the product. The builder and its inputs are
/// internal to the Infrastructure assembly; this one narrow reflective call grants no public API, no new internal access and no
/// product policy change, and a pinning test fails if the builder's shape ever changes. The map is returned only as a value for the
/// process request; it is never printed, stored or compared by value.
/// </summary>
public static class ProductionClaudeEnvironment
{
    private const string BuilderName = "DevalCopilot.Infrastructure.Features.Runs.ClaudeProcessEnvironment";
    private const string InputsName = "DevalCopilot.Infrastructure.Features.Runs.ClaudeProcessEnvironmentInputs";

    /// <summary>The production map, or null when the production builder refuses (its Windows prerequisite could not be derived), in
    /// which case the caller must not start a process and must not retry with a smaller map.</summary>
    public static Dictionary<string, string>? TryBuild()
    {
        var assembly = typeof(ChildProcessExecutionAdapter).Assembly;
        var builder = assembly.GetType(BuilderName, throwOnError: true)!;
        var inputsType = assembly.GetType(InputsName, throwOnError: true)!;
        var current = inputsType.GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new MissingMemberException(InputsName, "Current");
        var tryBuild = builder.GetMethod("TryBuild", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new MissingMemberException(BuilderName, "TryBuild");
        var arguments = new object?[] { current.GetValue(null), null };
        var built = (bool)tryBuild.Invoke(null, arguments)!;
        return built ? (Dictionary<string, string>?)arguments[1] : null;
    }
}
