using System.Diagnostics;
using DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.BrowserJourney;

/// <summary>
/// The browser journey host writes only beneath its verified disposable root, and every writable destination (layout directories,
/// installed binaries, the launch-target verdict) must be a plain location: neither it nor any existing ancestor beneath the root
/// may be an alias. A directory junction is a real alias on the real file system, so each refusal is observed through the outside
/// directory's unchanged sentinel, never through the host's own claim.
/// </summary>
public sealed class HostWritableDestinationTests : IDisposable
{
    private const string Token = "owner-token";

    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), $"devalcopilot-e2e-xunit-host{Guid.NewGuid():N}");
    private readonly string _outside = Path.Combine(Path.GetTempPath(), $"devalcopilot-xunit-host-outside-{Guid.NewGuid():N}");
    private readonly OwnedRootGuard _root;

    public HostWritableDestinationTests()
    {
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(Path.Combine(_rootPath, OwnedRootGuard.MarkerFile), Token);
        Directory.CreateDirectory(_outside);
        _root = OwnedRootGuard.Verify(_rootPath, Token);
    }

    public void Dispose()
    {
        foreach (var directory in new[] { _rootPath, _outside })
        {
            if (Directory.Exists(directory))
            {
                foreach (var junction in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories)
                             .Where(path => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)))
                {
                    Directory.Delete(junction);
                }

                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private string Sentinel(string name)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_outside, name)).FullName;
        File.WriteAllText(Path.Combine(directory, "sentinel.txt"), "unchanged");
        return directory;
    }

    private static void AssertUnchanged(string directory)
    {
        Assert.Equal(["sentinel.txt"], Directory.GetFileSystemEntries(directory).Select(entry => Path.GetFileName(entry)!).ToArray());
        Assert.Equal("unchanged", File.ReadAllText(Path.Combine(directory, "sentinel.txt")));
    }

    private static void CreateJunction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("workspaces")]
    [InlineData("artifacts")]
    [InlineData("db")]
    [InlineData("fixture")]
    public void Layout_creation_refuses_an_aliased_directory_and_creates_nothing_through_it(string name)
    {
        var target = Sentinel(name);
        CreateJunction(Path.Combine(_rootPath, name), target);

        Assert.Throws<InvalidOperationException>(_root.CreateLayout);

        AssertUnchanged(target);
    }

    [Fact]
    public void Installation_through_an_aliased_bin_directory_copies_nothing_outside()
    {
        var target = Sentinel("bin");
        CreateJunction(Path.Combine(_rootPath, "bin"), target);

        Assert.Throws<InvalidOperationException>(() => FixtureInstaller.Install(_root, AppContext.BaseDirectory));

        AssertUnchanged(target);
    }

    [Fact]
    public void Installation_refuses_an_aliased_binary_before_copying_any_file()
    {
        Directory.CreateDirectory(Path.Combine(_rootPath, "bin"));
        var target = Sentinel("binary");
        CreateJunction(Path.Combine(_rootPath, "bin", "claude.exe"), target);

        Assert.Throws<InvalidOperationException>(() => FixtureInstaller.Install(_root, AppContext.BaseDirectory));

        AssertUnchanged(target);
        Assert.Equal(["claude.exe"], Directory.GetFileSystemEntries(Path.Combine(_rootPath, "bin")).Select(entry => Path.GetFileName(entry)!).ToArray());
    }

    [Fact]
    public async Task The_launch_target_verdict_is_refused_through_an_aliased_state_directory_or_leaf()
    {
        var state = Sentinel("state");
        CreateJunction(Path.Combine(_rootPath, "fixture"), state);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _root.WriteFixtureStateFileAsync("launch-targets.json", "{}", CancellationToken.None));
        AssertUnchanged(state);

        Directory.Delete(Path.Combine(_rootPath, "fixture"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "fixture"));
        var leaf = Sentinel("leaf");
        CreateJunction(Path.Combine(_rootPath, "fixture", "launch-targets.json"), leaf);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _root.WriteFixtureStateFileAsync("launch-targets.json", "{}", CancellationToken.None));
        AssertUnchanged(leaf);
    }

    [Fact]
    public async Task Plain_owned_paths_receive_the_layout_the_installed_binaries_and_the_verdict()
    {
        _root.CreateLayout();
        FixtureInstaller.Install(_root, AppContext.BaseDirectory);
        await _root.WriteFixtureStateFileAsync("launch-targets.json", "{\"verified\":true}", CancellationToken.None);

        foreach (var role in FixtureInstaller.RoleNames)
        {
            Assert.True(File.Exists(Path.Combine(_root.Bin, role + ".exe")));
        }

        Assert.Equal("{\"verified\":true}", File.ReadAllText(Path.Combine(_root.FixtureState, "launch-targets.json")));
        Assert.Empty(Directory.GetFileSystemEntries(_outside));
    }
}
