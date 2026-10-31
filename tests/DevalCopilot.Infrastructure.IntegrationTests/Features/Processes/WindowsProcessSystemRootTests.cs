using System.ComponentModel;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Processes;

/// <summary>
/// Exercises the one shared Windows SystemRoot rule with fabricated system-directory
/// inputs only: it never reads the real system directory, an ambient <c>SystemRoot</c>/<c>windir</c>,
/// or any installed provider. Path-syntax cases are Windows-only because lexical full-qualification
/// is the host's own path semantics; the applicability cases are host-independent.
/// </summary>
public sealed class WindowsProcessSystemRootTests
{
    private const string SensitiveMarker = "SENSITIVE-SYSTEM-DIRECTORY-MARKER-4c91";

    [WindowsOnlyTheory]
    [InlineData(@"C:\Windows\System32", @"C:\Windows")]
    [InlineData(@"C:\Windows\System32\", @"C:\Windows")]
    [InlineData(@"D:\FabricatedOs\Core", @"D:\FabricatedOs")]
    [InlineData(@"E:\a\b\c\leaf-with-any-name", @"E:\a\b\c")]
    [InlineData(@"\\fabricated-server\share\Windows\System32", @"\\fabricated-server\share\Windows")]
    public void A_windows_node_script_gets_exactly_the_parent_of_the_system_directory_as_SystemRoot(
        string systemDirectory, string expectedSystemRoot)
    {
        var environment = new Dictionary<string, string>();

        var added = WindowsProcessSystemRoot.TryAdd(environment, requiresSystemRoot: true, new(true, () => systemDirectory));

        Assert.True(added);
        var entry = Assert.Single(environment);
        Assert.Equal("SystemRoot", entry.Key);
        Assert.Equal(expectedSystemRoot, entry.Value);
    }

    [WindowsPlatformFact]
    public void Success_adds_only_SystemRoot_and_preserves_every_entry_already_in_the_map()
    {
        var environment = new Dictionary<string, string> { ["USERPROFILE"] = @"C:\fabricated-profile", ["CODEX_HOME"] = "  " };

        var added = WindowsProcessSystemRoot.TryAdd(
            environment, requiresSystemRoot: true, new(true, () => @"C:\Windows\System32"));

        Assert.True(added);
        Assert.Equal(3, environment.Count);
        Assert.Equal(@"C:\fabricated-profile", environment["USERPROFILE"]);
        Assert.Equal("  ", environment["CODEX_HOME"]);
        Assert.Equal(@"C:\Windows", environment["SystemRoot"]);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void A_native_or_non_windows_request_keeps_its_map_and_never_evaluates_the_reader(bool isWindows, bool requiresSystemRoot)
    {
        var evaluations = 0;
        var environment = new Dictionary<string, string> { ["USERPROFILE"] = @"C:\fabricated-profile" };

        var added = WindowsProcessSystemRoot.TryAdd(
            environment,
            requiresSystemRoot,
            new(isWindows, () =>
            {
                evaluations++;
                throw new InvalidOperationException(SensitiveMarker);
            }));

        Assert.True(added);
        Assert.Equal(0, evaluations);
        var entry = Assert.Single(environment);
        Assert.Equal("USERPROFILE", entry.Key);
        Assert.Equal(@"C:\fabricated-profile", entry.Value);
    }

    [WindowsOnlyTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("System32")]
    [InlineData(@"..\Windows\System32")]
    [InlineData(@"\Windows\System32")]
    [InlineData(@"C:Windows\System32")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\")]
    [InlineData(@"\\fabricated-server\share")]
    [InlineData(@"\\fabricated-server\share\Windows")]
    [InlineData(@"C:\Windows\..\Windows\System32")]
    [InlineData(@"C:\Windows\.\System32")]
    [InlineData(@"C:\Windows\System32\..")]
    [InlineData(@"C:\Windows\System32\.")]
    [InlineData(@"C:\Windows\\System32")]
    [InlineData(@"C:\Windows\System32\\")]
    [InlineData(@"C:\Windows\Sys|tem32")]
    public void A_missing_or_unsafe_system_directory_is_refused_and_leaves_the_map_unchanged(string? systemDirectory)
    {
        AssertRefused(() => systemDirectory);
    }

    [WindowsOnlyTheory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(31)]
    public void A_system_directory_with_a_control_character_is_refused(int controlCharacter)
    {
        var systemDirectory = @"C:\Win" + (char)controlCharacter + @"dows\System32";

        AssertRefused(() => systemDirectory);
    }

    [Fact]
    public void An_unconfigured_reader_is_refused_for_a_windows_node_script()
    {
        var environment = new Dictionary<string, string>();

        Assert.False(WindowsProcessSystemRoot.TryAdd(environment, requiresSystemRoot: true, new(true, null)));
        Assert.Empty(environment);
    }

    [Theory]
    [InlineData(nameof(InvalidOperationException))]
    [InlineData(nameof(IOException))]
    [InlineData(nameof(UnauthorizedAccessException))]
    [InlineData(nameof(ArgumentException))]
    [InlineData(nameof(NotSupportedException))]
    [InlineData(nameof(PlatformNotSupportedException))]
    [InlineData(nameof(Win32Exception))]
    public void A_throwing_reader_is_refused_without_surfacing_the_failure(string failureType)
    {
        Exception failure = failureType switch
        {
            nameof(InvalidOperationException) => new InvalidOperationException(SensitiveMarker),
            nameof(IOException) => new IOException(SensitiveMarker),
            nameof(UnauthorizedAccessException) => new UnauthorizedAccessException(SensitiveMarker),
            nameof(ArgumentException) => new ArgumentException(SensitiveMarker),
            nameof(NotSupportedException) => new NotSupportedException(SensitiveMarker),
            nameof(PlatformNotSupportedException) => new PlatformNotSupportedException(SensitiveMarker),
            _ => new Win32Exception(5, SensitiveMarker),
        };
        var environment = new Dictionary<string, string> { ["USERPROFILE"] = @"C:\fabricated-profile" };

        var added = WindowsProcessSystemRoot.TryAdd(
            environment, requiresSystemRoot: true, new(true, () => throw failure));

        Assert.False(added);
        var entry = Assert.Single(environment);
        Assert.Equal("USERPROFILE", entry.Key);
    }

    [Fact]
    public void Cancellation_from_the_reader_is_not_swallowed()
    {
        Assert.Throws<OperationCanceledException>(() => WindowsProcessSystemRoot.TryAdd(
            new Dictionary<string, string>(), requiresSystemRoot: true, new(true, () => throw new OperationCanceledException())));
    }

    private static void AssertRefused(Func<string?> readSystemDirectory)
    {
        var environment = new Dictionary<string, string> { ["USERPROFILE"] = @"C:\fabricated-profile" };

        var added = WindowsProcessSystemRoot.TryAdd(environment, requiresSystemRoot: true, new(true, readSystemDirectory));

        Assert.False(added);
        var entry = Assert.Single(environment);
        Assert.Equal("USERPROFILE", entry.Key);
        Assert.Equal(@"C:\fabricated-profile", entry.Value);
    }
}
