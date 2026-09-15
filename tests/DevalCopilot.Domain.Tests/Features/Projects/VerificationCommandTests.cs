using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Projects;

public sealed class VerificationCommandTests
{
    [Fact]
    public void Configure_defensively_copies_literal_arguments_and_allows_later_configuration_updates()
    {
        var arguments = new List<string> { "test", "--no-restore" };
        var configuredAtUtc = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
        var command = VerificationCommand.Configure(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "Unit tests",
            @"C:\Program Files\dotnet\dotnet.exe",
            arguments,
            300,
            isEnabled: true,
            configuredAtUtc);

        arguments.Add("--tampered");
        command.Update(
            "Unit tests (bounded)",
            @"C:\Program Files\dotnet\dotnet.exe",
            ["test", "--no-build"],
            120,
            isEnabled: false,
            configuredAtUtc.AddMinutes(1));

        Assert.Equal(["test", "--no-build"], command.Arguments);
        Assert.Equal("Unit tests (bounded)", command.Name);
        Assert.False(command.IsEnabled);
        Assert.Equal(120, command.TimeoutSeconds);
        Assert.Equal(configuredAtUtc, command.ConfiguredAtUtc);
        Assert.Equal(configuredAtUtc.AddMinutes(1), command.UpdatedAtUtc);
    }

    [Fact]
    public void Configure_rejects_relative_executables_and_unbounded_configuration()
    {
        Assert.Throws<ArgumentException>(() => VerificationCommand.Configure(
            Guid.NewGuid(), Guid.NewGuid(), 1, "Tests", "dotnet", [], 60, true, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => VerificationCommand.Configure(
            Guid.NewGuid(), Guid.NewGuid(), 1, "Tests", @"C:\dotnet.exe", [], 0, true, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => VerificationCommand.Configure(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "Tests",
            @"C:\dotnet.exe",
            Enumerable.Repeat("--flag", VerificationCommand.MaximumArgumentCount + 1).ToArray(),
            60,
            true,
            DateTimeOffset.UtcNow));
    }
}
