using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>The preview reader's containment and identity proof, exercised directly with a CORRECT git
/// blob identity for the content it is asked about, so every refusal below is the containment,
/// path-form, or file-type rule and not merely a hash mismatch. Nothing outside the worktree may be
/// read or returned.</summary>
[SupportedOSPlatform("windows")]
public sealed class UntrackedFilePreviewReaderTests : IDisposable
{
    private const string OutsideSecret = "TOP SECRET TEXT FROM OUTSIDE THE WORKTREE";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-preview-reader-{Guid.NewGuid():N}");
    private readonly string _worktree;
    private readonly string _outside;
    private readonly List<string> _junctions = [];

    public UntrackedFilePreviewReaderTests()
    {
        _worktree = Path.Combine(_root, "worktree");
        _outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(_worktree);
        Directory.CreateDirectory(_outside);
        GitWorkspaceUntrackedPreviewTests.RunGit(_worktree, "init", "-q");
    }

    public void Dispose()
    {
        foreach (var junction in _junctions.Where(Directory.Exists))
        {
            Directory.Delete(junction, recursive: false);
        }

        Directory.Delete(_root, recursive: true);
    }

    [WindowsOnlyFact]
    public void A_regular_file_inside_the_worktree_with_a_matching_identity_is_previewed()
    {
        var hash = WriteInside("dir/ok.txt", "hello");

        var file = Assert.Single(Read(("dir/ok.txt", hash)));

        Assert.Null(file.Omission);
        Assert.Equal("hello", file.Text);
        Assert.True(file.ContentComplete);
    }

    [WindowsOnlyFact]
    public void A_junction_inside_the_worktree_to_an_outside_directory_is_refused_even_with_the_correct_identity()
    {
        var outsideHash = WriteOutside("secret.txt", OutsideSecret);
        CreateJunction(Path.Combine(_worktree, "link"), _outside);

        var file = Assert.Single(Read(("link/secret.txt", outsideHash)));

        Assert.Equal(GitWorkspaceUntrackedOmission.ContainmentUnproven, file.Omission);
        Assert.Null(file.Text);
        Assert.Null(file.SizeBytes);
    }

    [WindowsOnlyFact]
    public void A_junction_inside_the_worktree_to_another_place_inside_it_is_also_not_the_reported_path()
    {
        var hash = WriteInside("real/file.txt", "inside twice");
        CreateJunction(Path.Combine(_worktree, "alias"), Path.Combine(_worktree, "real"));

        var file = Assert.Single(Read(("alias/file.txt", hash)));

        Assert.Equal(GitWorkspaceUntrackedOmission.ContainmentUnproven, file.Omission);
        Assert.Null(file.Text);
    }

    [RequiresFileSymlinkSupportFact]
    public void A_file_symbolic_link_to_an_outside_file_is_refused_without_returning_its_text()
    {
        var outsideHash = WriteOutside("target.txt", OutsideSecret);
        File.CreateSymbolicLink(Path.Combine(_worktree, "link.txt"), Path.Combine(_outside, "target.txt"));

        var file = Assert.Single(Read(("link.txt", outsideHash)));

        Assert.Equal(GitWorkspaceUntrackedOmission.ContainmentUnproven, file.Omission);
        Assert.Null(file.Text);
    }

    [WindowsOnlyFact]
    public void A_case_distinct_spelling_of_an_existing_file_is_not_treated_as_that_path()
    {
        var hash = WriteInside("Readme.md", "case");

        var same = Assert.Single(Read(("Readme.md", hash)));
        var other = Assert.Single(Read(("README.md", hash)));

        Assert.Equal("case", same.Text);
        Assert.Equal(GitWorkspaceUntrackedOmission.ContainmentUnproven, other.Omission);
        Assert.Null(other.Text);
    }

    [WindowsOnlyTheory]
    [InlineData("../outside/secret.txt")]
    [InlineData("a/../../outside/secret.txt")]
    [InlineData("./secret.txt")]
    [InlineData("/secret.txt")]
    [InlineData("C:secret.txt")]
    [InlineData("secret.txt:stream")]
    [InlineData("dir\\secret.txt")]
    [InlineData("a//b.txt")]
    [InlineData("")]
    public void A_path_that_could_name_something_else_is_refused_before_it_is_opened(string path)
    {
        var outsideHash = WriteOutside("secret.txt", OutsideSecret);

        var file = Assert.Single(Read((path, outsideHash)));

        Assert.Equal(GitWorkspaceUntrackedOmission.ContainmentUnproven, file.Omission);
        Assert.Null(file.Text);
    }

    [WindowsOnlyFact]
    public void A_directory_a_trailing_slash_path_or_a_directory_junction_is_not_a_regular_file()
    {
        Directory.CreateDirectory(Path.Combine(_worktree, "folder"));
        CreateJunction(Path.Combine(_worktree, "linked-dir"), _outside);
        var hash = WriteInside("f.txt", "x");

        var results = Read(("folder", hash), ("folder/", hash), ("linked-dir", hash));

        Assert.All(results, file =>
        {
            Assert.Equal(GitWorkspaceUntrackedOmission.NotRegularFile, file.Omission);
            Assert.Null(file.Text);
        });
    }

    [WindowsOnlyFact]
    public void A_missing_swapped_or_replaced_path_is_omitted_and_never_mispresented()
    {
        var hash = WriteInside("swap.txt", "captured content");

        File.Delete(Path.Combine(_worktree, "swap.txt"));
        Assert.Equal(GitWorkspaceUntrackedOmission.Missing, Assert.Single(Read(("swap.txt", hash))).Omission);

        File.WriteAllText(Path.Combine(_worktree, "swap.txt"), "different content");
        var swapped = Assert.Single(Read(("swap.txt", hash)));
        Assert.Equal(GitWorkspaceUntrackedOmission.ContentIdentityMismatch, swapped.Omission);
        Assert.Null(swapped.Text);

        File.Delete(Path.Combine(_worktree, "swap.txt"));
        Directory.CreateDirectory(Path.Combine(_worktree, "swap.txt"));
        Assert.Equal(GitWorkspaceUntrackedOmission.NotRegularFile, Assert.Single(Read(("swap.txt", hash))).Omission);
    }

    [WindowsOnlyFact]
    public void A_same_size_replacement_is_a_content_mismatch_not_a_preview()
    {
        var hash = WriteInside("same-size.txt", "aaaaaaaa");
        File.WriteAllText(Path.Combine(_worktree, "same-size.txt"), "bbbbbbbb");

        var file = Assert.Single(Read(("same-size.txt", hash)));

        Assert.Equal(GitWorkspaceUntrackedOmission.ContentIdentityMismatch, file.Omission);
        Assert.Null(file.Text);
    }

    [WindowsOnlyFact]
    public void A_worktree_root_that_cannot_be_resolved_omits_every_file_as_unproven()
    {
        var hash = WriteInside("f.txt", "x");

        var files = UntrackedFilePreviewReader.ReadAll(Path.Combine(_root, "does-not-exist"), [("f.txt", hash)]);

        Assert.Equal(GitWorkspaceUntrackedOmission.ContainmentUnproven, Assert.Single(files).Omission);
    }

    [RequiresCaseSensitiveDirectorySupportFact]
    public void A_junction_into_a_case_distinct_sibling_of_the_worktree_root_is_refused_even_with_the_correct_identity()
    {
        // Parent is case-sensitive, so "Artifacts" (the worktree) and "ARTIFACTS" (outside it) are distinct
        // directories. The junction's final path differs from the root only by the case of the root prefix.
        var parent = Path.Combine(_root, "case-sensitive-parent");
        Directory.CreateDirectory(parent);
        Assert.True(RequiresCaseSensitiveDirectorySupportFactAttribute.TryEnableCaseSensitivity(parent));
        var worktree = Path.Combine(parent, "Artifacts");
        var sibling = Path.Combine(parent, "ARTIFACTS");
        Directory.CreateDirectory(worktree);
        Directory.CreateDirectory(Path.Combine(sibling, "link"));
        Assert.NotEqual(Path.GetFullPath(worktree), Path.GetFullPath(sibling));
        Assert.Equal(2, Directory.GetDirectories(parent).Length);
        var outsideHash = Write(sibling, "link/file.txt", OutsideSecret);
        var insideHash = Write(worktree, "ok.txt", "contained");
        CreateJunction(Path.Combine(worktree, "link"), Path.Combine(sibling, "link"));
        var redirectedRoot = Path.Combine(_root, "redirected-root");
        CreateJunction(redirectedRoot, worktree);

        var escaped = Assert.Single(UntrackedFilePreviewReader.ReadAll(worktree, [("link/file.txt", outsideHash)]));
        var contained = Assert.Single(UntrackedFilePreviewReader.ReadAll(worktree, [("ok.txt", insideHash)]));
        var throughRedirect = Assert.Single(UntrackedFilePreviewReader.ReadAll(redirectedRoot, [("ok.txt", insideHash)]));

        Assert.Equal(GitWorkspaceUntrackedOmission.ContainmentUnproven, escaped.Omission);
        Assert.Null(escaped.Text);
        Assert.Null(escaped.SizeBytes);
        Assert.Equal("contained", contained.Text);
        Assert.Equal("contained", throughRedirect.Text);
    }

    private IReadOnlyList<GitWorkspaceUntrackedFile> Read(params (string Path, string Hash)[] files) =>
        UntrackedFilePreviewReader.ReadAll(_worktree, files);

    private string WriteInside(string relativePath, string content) => Write(_worktree, relativePath, content);

    private string WriteOutside(string relativePath, string content) => Write(_outside, relativePath, content);

    private static string Write(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
        return GitWorkspaceUntrackedPreviewTests.GitOutput(root, "hash-object", "--no-filters", "--", path);
    }

    private void CreateJunction(string junctionPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(junctionPath);
        startInfo.ArgumentList.Add(targetPath);
        using var process = Process.Start(startInfo)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        _junctions.Add(junctionPath);
    }
}
