using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class RunExecutionModeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_enum_values_are_the_persisted_contract()
    {
        Assert.Equal(0, (int)RunExecutionMode.Legacy);
        Assert.Equal(1, (int)RunExecutionMode.Simulated);
        Assert.Equal(2, (int)RunExecutionMode.ManualAgent);
        Assert.Equal(3, Enum.GetValues<RunExecutionMode>().Length);
    }

    [Theory]
    [InlineData(RunExecutionMode.Simulated)]
    [InlineData(RunExecutionMode.ManualAgent)]
    public void A_classified_intent_keeps_its_explicit_mode_and_the_default_created_state(RunExecutionMode mode)
    {
        var run = Run.RecordClassifiedIntent(Guid.NewGuid(), Guid.NewGuid(), 1, mode, "Objective", Now);

        Assert.Equal(mode, run.ExecutionMode);
        Assert.Equal(RunLifecycle.Created, run.Lifecycle);
        Assert.Equal(RunStage.Intake, run.Stage);
        Assert.Equal(Run.DefaultMaximumAgentInvocationTime, run.MaximumAgentInvocationTime);
    }

    [Theory]
    [InlineData((int)RunExecutionMode.Legacy)]
    [InlineData(3)]
    [InlineData(-1)]
    public void A_classified_intent_refuses_the_legacy_value_and_any_undefined_number(int mode)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Run.RecordClassifiedIntent(Guid.NewGuid(), Guid.NewGuid(), 1, (RunExecutionMode)mode, "Objective", Now));
    }

    [Fact]
    public void The_unclassified_intent_factory_is_the_legacy_shape_only()
    {
        var run = Run.RecordIntent(Guid.NewGuid(), Guid.NewGuid(), 1, "Objective", Now);

        Assert.Equal(RunExecutionMode.Legacy, run.ExecutionMode);
    }

    [Fact]
    public void A_run_exposes_no_mode_setter()
    {
        var property = typeof(Run).GetProperty(nameof(Run.ExecutionMode))!;

        Assert.False(property.SetMethod?.IsPublic ?? false);
        Assert.DoesNotContain(
            typeof(Run).GetMethods(),
            method => method.ReturnType == typeof(void)
                && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(RunExecutionMode)));
    }

    [Theory]
    [InlineData("0", RunExecutionMode.Legacy)]
    [InlineData("1", RunExecutionMode.Simulated)]
    [InlineData("2", RunExecutionMode.ManualAgent)]
    public void Only_the_canonical_digits_of_a_defined_mode_are_recognized(string stored, RunExecutionMode expected)
    {
        Assert.Equal(expected, RunExecutionModeStorage.Read(stored));
        Assert.Equal(stored, RunExecutionModeStorage.Format(expected));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("3")]
    [InlineData("-1")]
    [InlineData("02")]
    [InlineData("4294967296")]
    [InlineData("4294967298")]
    [InlineData("r:4004000000000000")]
    [InlineData("b:02")]
    [InlineData("t:2")]
    [InlineData("?:Byte[]")]
    public void Every_other_stored_form_is_unrecognized_and_admitted_by_nothing(string? stored)
    {
        var mode = RunExecutionModeStorage.Read(stored);

        Assert.Equal(RunExecutionModeStorage.Unrecognized, mode);
        Assert.False(RunExecutionModeAdmission.IsRecognized(mode));
        Assert.False(RunExecutionModeAdmission.AdmitsSimulation(mode));
        Assert.False(RunExecutionModeAdmission.AdmitsAgent(mode));
        Assert.False(RunExecutionModeAdmission.AdmitsProcess(mode));
    }

    [Theory]
    [InlineData(RunExecutionMode.Legacy, true, true, true)]
    [InlineData(RunExecutionMode.Simulated, true, false, false)]
    [InlineData(RunExecutionMode.ManualAgent, false, true, false)]
    [InlineData((RunExecutionMode)7, false, false, false)]
    [InlineData((RunExecutionMode)(-1), false, false, false)]
    public void The_admission_table_admits_exactly_the_documented_modes(
        RunExecutionMode mode, bool simulation, bool agent, bool process)
    {
        Assert.Equal(simulation, RunExecutionModeAdmission.AdmitsSimulation(mode));
        Assert.Equal(agent, RunExecutionModeAdmission.AdmitsAgent(mode));
        Assert.Equal(process, RunExecutionModeAdmission.AdmitsProcess(mode));
        Assert.Equal(simulation, RunExecutionModeAdmission.SimulationModes.Contains(mode));
        Assert.Equal(agent, RunExecutionModeAdmission.AgentModes.Contains(mode));
        Assert.Equal(process, RunExecutionModeAdmission.ProcessModes.Contains(mode));
    }

    [Theory]
    [InlineData(RunLifecycle.Created, false)]
    [InlineData(RunLifecycle.Running, false)]
    [InlineData(RunLifecycle.Completed, true)]
    [InlineData(RunLifecycle.Failed, true)]
    [InlineData(RunLifecycle.Interrupted, true)]
    [InlineData((RunLifecycle)99, false)]
    public void Only_a_recognized_terminal_lifecycle_permits_a_new_intent(RunLifecycle lifecycle, bool permits)
    {
        Assert.Equal(permits, RunLifecycleAdmission.PermitsNewIntent(lifecycle));
    }
}
