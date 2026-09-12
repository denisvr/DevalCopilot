using Xunit;

namespace DevalCopilot.Architecture.Tests;

public sealed class DependencyDirectionTests
{
    [Theory]
    [InlineData(SolutionAssemblies.Application)]
    [InlineData(SolutionAssemblies.Infrastructure)]
    [InlineData(SolutionAssemblies.Api)]
    public void Domain_does_not_reference_any_outer_layer(string outerLayer)
    {
        var references = SolutionAssemblies.ReferencedNames(SolutionAssemblies.Domain);

        Assert.DoesNotContain(outerLayer, references);
    }

    [Theory]
    [InlineData(SolutionAssemblies.Infrastructure)]
    [InlineData(SolutionAssemblies.Api)]
    public void Application_does_not_reference_any_outer_layer(string outerLayer)
    {
        var references = SolutionAssemblies.ReferencedNames(SolutionAssemblies.Application);

        Assert.DoesNotContain(outerLayer, references);
    }

    [Fact]
    public void Infrastructure_does_not_reference_the_api()
    {
        var references = SolutionAssemblies.ReferencedNames(SolutionAssemblies.Infrastructure);

        Assert.DoesNotContain(SolutionAssemblies.Api, references);
    }

    [Fact]
    public void Domain_does_not_reference_entity_framework_core()
    {
        var references = SolutionAssemblies.ReferencedNames(SolutionAssemblies.Domain);

        Assert.DoesNotContain(references, reference => reference.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Domain_does_not_reference_aspnet_core()
    {
        var references = SolutionAssemblies.ReferencedNames(SolutionAssemblies.Domain);

        Assert.DoesNotContain(references, reference => reference.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_does_not_reference_aspnet_core()
    {
        var references = SolutionAssemblies.ReferencedNames(SolutionAssemblies.Application);

        Assert.DoesNotContain(references, reference => reference.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }
}
