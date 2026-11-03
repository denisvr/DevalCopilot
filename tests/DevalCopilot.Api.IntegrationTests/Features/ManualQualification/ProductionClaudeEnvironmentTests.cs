using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>Pins the one reflective call into the production Claude environment builder: it must resolve, and it must still produce
/// exactly the documented allowlist. A change of the builder's shape or policy fails here instead of silently changing lot A's
/// filtered context.</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ProductionClaudeEnvironmentTests
{
    private static readonly string[] Allowed = ["USERPROFILE", "CLAUDE_CONFIG_DIR", "SystemRoot"];

    [Fact]
    public void The_production_builder_resolves_and_yields_only_the_documented_allowlist_with_the_system_root()
    {
        var map = ProductionClaudeEnvironment.TryBuild();

        Assert.NotNull(map);
        Assert.Contains("SystemRoot", map.Keys);
        Assert.All(map.Keys, key => Assert.Contains(key, Allowed));
        Assert.DoesNotContain(map.Keys, key => key.Equals("PATH", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(map.Keys, key => key.StartsWith("ANTHROPIC_", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_ambient_variable_outside_the_allowlist_never_reaches_the_production_map()
    {
        const string Name = "DEVALCOPILOT_TEST_AMBIENT_ONLY";
        Environment.SetEnvironmentVariable(Name, "present");
        try
        {
            var map = ProductionClaudeEnvironment.TryBuild();

            Assert.NotNull(map);
            Assert.DoesNotContain(Name, map.Keys);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Name, null);
        }
    }

    [Fact]
    public void The_map_is_built_from_the_current_process_variables_unchanged_and_never_printed()
    {
        var map = ProductionClaudeEnvironment.TryBuild();

        Assert.NotNull(map);
        if (map.TryGetValue("USERPROFILE", out var profile))
        {
            Assert.Equal(Environment.GetEnvironmentVariable("USERPROFILE"), profile);
        }

        Assert.DoesNotContain("USERPROFILE=", string.Join(';', typeof(ProductionClaudeEnvironment).GetMethods().Select(method => method.Name)), StringComparison.Ordinal);
    }
}
