using System.Reflection;

namespace DevalCopilot.Architecture.Tests;

internal static class SolutionAssemblies
{
    internal const string Domain = "DevalCopilot.Domain";
    internal const string Application = "DevalCopilot.Application";
    internal const string Infrastructure = "DevalCopilot.Infrastructure";
    internal const string Api = "DevalCopilot.Api";

    internal static Assembly Load(string assemblyName) => Assembly.Load(assemblyName);

    internal static IReadOnlyCollection<string> ReferencedNames(string assemblyName) =>
        Load(assemblyName)
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();
}
