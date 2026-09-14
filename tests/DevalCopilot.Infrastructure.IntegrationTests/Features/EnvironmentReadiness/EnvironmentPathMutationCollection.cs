using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>
/// Forces every test class that mutates the real, process-wide <c>PATH</c> environment
/// variable (or that resolves a real executable through it) to run sequentially relative to
/// each other — xUnit runs different collections in parallel by default, which would otherwise
/// let one test's temporary PATH override be in effect while another resolves a real tool.
/// </summary>
[CollectionDefinition(Name)]
public sealed class EnvironmentPathMutationCollection
{
    public const string Name = "EnvironmentPathMutation";
}
