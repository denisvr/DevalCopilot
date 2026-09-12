using DevalCopilot.Application.Features.Runs.Commands.StartSimulatedRun;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class StartSimulatedRunCommandValidatorTests
{
    private readonly StartSimulatedRunCommandValidator _validator = new();

    [Fact]
    public void Validate_succeeds_for_a_well_formed_command()
    {
        var result = _validator.Validate(new StartSimulatedRunCommand(Guid.NewGuid(), "Add token budgets"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_fails_when_the_project_id_is_empty()
    {
        var result = _validator.Validate(new StartSimulatedRunCommand(Guid.Empty, "Add token budgets"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_fails_when_the_objective_is_blank()
    {
        var result = _validator.Validate(new StartSimulatedRunCommand(Guid.NewGuid(), "   "));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_fails_when_the_objective_exceeds_the_maximum_length()
    {
        var result = _validator.Validate(new StartSimulatedRunCommand(Guid.NewGuid(), new string('x', 2001)));

        Assert.False(result.IsValid);
    }
}
