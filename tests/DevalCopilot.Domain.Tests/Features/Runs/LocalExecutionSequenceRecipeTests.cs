using System.Text.Json;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>ADR-0060: a pinned verification recipe copies the full execution metadata by value and matches a stored recipe only when
/// every execution-relevant member is equal.</summary>
public sealed class LocalExecutionSequenceRecipeTests
{
    private static readonly DateTimeOffset Configured = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Updated = new(2026, 10, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid CommandId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static VerificationCommand Command(
        Guid? id = null,
        int number = 3,
        string name = "Tests",
        string executable = @"C:\tools\dotnet.exe",
        string[]? arguments = null,
        int timeout = 120,
        bool enabled = true,
        DateTimeOffset? configuredAt = null,
        DateTimeOffset? updatedAt = null)
    {
        var command = VerificationCommand.Configure(
            id ?? CommandId, ProjectId, number, name, executable, arguments ?? ["test", "--no-build"], timeout, enabled,
            configuredAt ?? Configured);
        command.Update(name, executable, arguments ?? ["test", "--no-build"], timeout, enabled, updatedAt ?? Updated);
        return command;
    }

    private static LocalExecutionSequenceRecipe Pinned() =>
        LocalExecutionSequenceRecipe.Pin(Guid.NewGuid(), Guid.NewGuid(), 2, Command());

    [Fact]
    public void Pinning_copies_the_full_execution_metadata_by_value()
    {
        var id = Guid.NewGuid();
        var sequenceId = Guid.NewGuid();
        var command = Command();

        var recipe = LocalExecutionSequenceRecipe.Pin(id, sequenceId, 5, command);

        Assert.Equal(id, recipe.Id);
        Assert.Equal(sequenceId, recipe.SequenceId);
        Assert.Equal(5, recipe.Ordinal);
        Assert.Equal(command.Id, recipe.VerificationCommandId);
        Assert.Equal(3, recipe.CommandNumber);
        Assert.Equal("Tests", recipe.CommandName);
        Assert.Equal(@"C:\tools\dotnet.exe", recipe.ExecutablePath);
        Assert.Equal(JsonSerializer.Serialize(new[] { "test", "--no-build" }), recipe.ArgumentsJson);
        Assert.Equal(120, recipe.TimeoutSeconds);
        Assert.True(recipe.IsEnabled);
        Assert.Equal(Configured, recipe.ConfiguredAtUtc);
        Assert.Equal(Updated, recipe.UpdatedAtUtc);
    }

    [Fact]
    public void Pinning_rejects_empty_identifiers_and_an_out_of_range_ordinal_and_a_missing_command()
    {
        var command = Command();

        Assert.Throws<ArgumentException>(() => LocalExecutionSequenceRecipe.Pin(Guid.Empty, Guid.NewGuid(), 0, command));
        Assert.Throws<ArgumentException>(() => LocalExecutionSequenceRecipe.Pin(Guid.NewGuid(), Guid.Empty, 0, command));
        Assert.Throws<ArgumentOutOfRangeException>(() => LocalExecutionSequenceRecipe.Pin(Guid.NewGuid(), Guid.NewGuid(), -1, command));
        Assert.Throws<ArgumentOutOfRangeException>(() => LocalExecutionSequenceRecipe.Pin(Guid.NewGuid(), Guid.NewGuid(), 32, command));
        Assert.Throws<ArgumentNullException>(() => LocalExecutionSequenceRecipe.Pin(Guid.NewGuid(), Guid.NewGuid(), 0, null!));
        Assert.Equal(31, LocalExecutionSequenceRecipe.Pin(Guid.NewGuid(), Guid.NewGuid(), 31, command).Ordinal);
    }

    [Fact]
    public void A_pinned_recipe_does_not_follow_a_later_edit_of_the_command()
    {
        var command = Command();
        var recipe = LocalExecutionSequenceRecipe.Pin(Guid.NewGuid(), Guid.NewGuid(), 0, command);
        Assert.True(recipe.Matches(command));

        command.Update("Renamed", command.ExecutablePath, command.Arguments.ToArray(), command.TimeoutSeconds, true, Updated.AddHours(1));

        Assert.Equal("Tests", recipe.CommandName);
        Assert.False(recipe.Matches(command));
    }

    [Fact]
    public void An_identical_command_matches()
    {
        Assert.True(Pinned().Matches(Command()));
    }

    [Fact]
    public void A_null_command_never_matches()
    {
        Assert.False(Pinned().Matches(null!));
    }

    public static TheoryData<string> DriftedMembers => new()
    {
        "id", "number", "name", "executable", "argument-value", "argument-order", "argument-added", "argument-removed", "timeout",
        "enabled", "configured", "updated",
    };

    private static VerificationCommand Drifted(string member) => member switch
    {
        "id" => Command(id: Guid.NewGuid()),
        "number" => Command(number: 4),
        "name" => Command(name: "Other"),
        "executable" => Command(executable: @"C:\tools\other.exe"),
        "argument-value" => Command(arguments: ["test", "--build"]),
        "argument-order" => Command(arguments: ["--no-build", "test"]),
        "argument-added" => Command(arguments: ["test", "--no-build", "-v"]),
        "argument-removed" => Command(arguments: ["test"]),
        "timeout" => Command(timeout: 121),
        "enabled" => Command(enabled: false),
        "configured" => Command(configuredAt: Configured.AddTicks(1)),
        "updated" => Command(updatedAt: Updated.AddTicks(1)),
        _ => throw new ArgumentOutOfRangeException(nameof(member)),
    };

    [Theory]
    [MemberData(nameof(DriftedMembers))]
    public void A_command_that_differs_in_any_one_member_does_not_match(string member)
    {
        Assert.False(Pinned().Matches(Drifted(member)));
    }

    [Fact]
    public void The_name_comparison_is_ordinal_and_case_sensitive()
    {
        Assert.False(Pinned().Matches(Command(name: "tests")));
    }
}
