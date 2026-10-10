using System.Diagnostics;
using DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>Cleanup deletes only the one proven-owned root; every foreign, aliased or unproven directory is left exactly as it was.</summary>
public sealed class OwnedRootCleanerTests : IDisposable
{
    private const string Token = "owner-token";

    private readonly List<string> _created = [];

    public void Dispose()
    {
        foreach (var directory in _created.Where(Directory.Exists))
        {
            foreach (var junction in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories)
                         .Where(path => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)).ToArray())
            {
                Directory.Delete(junction);
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string Root(string? token = Token, string name = "")
    {
        var path = Path.Combine(Path.GetTempPath(), $"{OwnedRootGuard.RootPrefix}xunit-clean-{name}{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _created.Add(path);
        if (token is not null)
        {
            File.WriteAllText(Path.Combine(path, OwnedRootGuard.MarkerFile), token);
        }

        File.WriteAllText(Path.Combine(path, "inside.txt"), "inside");
        return path;
    }

    [Fact]
    public void The_owned_root_is_removed_including_read_only_files_and_nested_directories()
    {
        var root = Root();
        var nested = Directory.CreateDirectory(Path.Combine(root, "workspaces", "w", ".git", "objects")).FullName;
        var readOnly = Path.Combine(nested, "pack");
        File.WriteAllText(readOnly, "object");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);

        var report = OwnedRootCleaner.Remove(root, Token);

        Assert.True(report.Removed);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void A_sibling_that_merely_shares_the_prefix_or_carries_another_token_is_never_touched()
    {
        var own = Root();
        var sameToken = Root(Token, "sibling-");
        var differentToken = Root("another-token", "foreign-");
        var noMarker = Root(null, "unmarked-");

        Assert.True(OwnedRootCleaner.Remove(own, Token).Removed);

        foreach (var sibling in new[] { sameToken, differentToken, noMarker })
        {
            Assert.Equal("inside", File.ReadAllText(Path.Combine(sibling, "inside.txt")));
        }

        Assert.Equal("OwnershipUnproven", OwnedRootCleaner.Remove(differentToken, Token).Reason);
        Assert.Equal("OwnershipUnproven", OwnedRootCleaner.Remove(noMarker, Token).Reason);
        Assert.True(File.Exists(Path.Combine(differentToken, "inside.txt")));
        Assert.True(File.Exists(Path.Combine(noMarker, "inside.txt")));
    }

    [Fact]
    public void A_directory_outside_the_temp_directory_is_never_removed()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"devalcopilot-xunit-outside-{Guid.NewGuid():N}", OwnedRootGuard.RootPrefix + "nested");
        Directory.CreateDirectory(outside);
        _created.Add(Path.GetDirectoryName(outside)!);
        File.WriteAllText(Path.Combine(outside, OwnedRootGuard.MarkerFile), Token);

        var report = OwnedRootCleaner.Remove(outside, Token);

        Assert.Equal("OwnershipUnproven", report.Reason);
        Assert.True(Directory.Exists(outside));
    }

    [Fact]
    public void A_root_holding_an_alias_is_preserved_and_the_alias_target_is_untouched()
    {
        var root = Root();
        var target = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"devalcopilot-xunit-alias-target-{Guid.NewGuid():N}")).FullName;
        _created.Add(target);
        File.WriteAllText(Path.Combine(target, "sentinel.txt"), "unchanged");
        using (var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{Path.Combine(root, "link")}\" \"{target}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!)
        {
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }

        var report = OwnedRootCleaner.Remove(root, Token);

        Assert.Equal("AliasInsideRoot", report.Reason);
        Assert.True(Directory.Exists(root));
        Assert.Equal("unchanged", File.ReadAllText(Path.Combine(target, "sentinel.txt")));
        Assert.Equal("inside", File.ReadAllText(Path.Combine(root, "inside.txt")));
    }

    [Fact]
    public void A_file_that_cannot_be_deleted_preserves_the_root()
    {
        var root = Root();
        using var held = new FileStream(Path.Combine(root, "inside.txt"), FileMode.Open, FileAccess.Read, FileShare.None);

        var report = OwnedRootCleaner.Remove(root, Token);

        Assert.False(report.Removed);
        Assert.Equal("DeletionFailed", report.Reason);
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public void A_deletion_that_fails_part_way_leaves_the_root_still_able_to_prove_its_ownership()
    {
        var root = Root();
        Directory.CreateDirectory(Path.Combine(root, "a-first"));
        File.WriteAllText(Path.Combine(root, "a-first", "gone.txt"), "removed");
        using var held = new FileStream(Path.Combine(root, "zz-last.txt"), FileMode.Create, FileAccess.Write, FileShare.None);

        var report = OwnedRootCleaner.Remove(root, Token);

        Assert.Equal("DeletionFailed", report.Reason);
        Assert.Equal(Token, File.ReadAllText(Path.Combine(root, OwnedRootGuard.MarkerFile)));
        Assert.False(Directory.Exists(Path.Combine(root, "a-first")));
        Assert.Equal("DeletionFailed", OwnedRootCleaner.Remove(root, Token).Reason);
    }
}
