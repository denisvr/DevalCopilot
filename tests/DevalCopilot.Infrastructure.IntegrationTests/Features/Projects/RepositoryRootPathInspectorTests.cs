using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Projects;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

public sealed class RepositoryRootPathInspectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-root-inspector-{Guid.NewGuid():N}");
    private readonly RepositoryRootPathInspector _inspector = new();

    public RepositoryRootPathInspectorTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Inspect_succeeds_for_an_existing_absolute_directory()
    {
        var result = _inspector.Inspect(_root);

        Assert.Equal(RepositoryRootInspectionOutcome.Success, result.Outcome);
        Assert.Equal(_root, result.Candidate!.CanonicalPath);
    }

    [Fact]
    public void Inspect_normalizes_a_trailing_separator_to_the_same_canonical_path()
    {
        var withSeparator = _root + Path.DirectorySeparatorChar;

        var result = _inspector.Inspect(withSeparator);

        Assert.Equal(RepositoryRootInspectionOutcome.Success, result.Outcome);
        Assert.Equal(_root, result.Candidate!.CanonicalPath);
    }

    [Fact]
    public void Inspect_normalizes_a_dot_dot_segment_to_the_same_canonical_path()
    {
        var withDotDot = Path.Combine(_root, "child", "..");
        Directory.CreateDirectory(Path.Combine(_root, "child"));

        var result = _inspector.Inspect(withDotDot);

        Assert.Equal(RepositoryRootInspectionOutcome.Success, result.Outcome);
        Assert.Equal(_root, result.Candidate!.CanonicalPath);
    }

    [Fact]
    public void Inspect_rejects_a_relative_path()
    {
        var result = _inspector.Inspect("relative\\path");

        Assert.Equal(RepositoryRootInspectionOutcome.NotAbsolute, result.Outcome);
        Assert.Null(result.Candidate);
    }

    [Fact]
    public void Inspect_rejects_a_unc_path()
    {
        var result = _inspector.Inspect(@"\\server\share\repo");

        Assert.Equal(RepositoryRootInspectionOutcome.RemoteRootNotSupported, result.Outcome);
    }

    [Fact]
    public void Inspect_rejects_an_extended_length_device_path()
    {
        var result = _inspector.Inspect(@"\\?\C:\repos\foo");

        Assert.Equal(RepositoryRootInspectionOutcome.RemoteRootNotSupported, result.Outcome);
    }

    [Fact]
    public void Inspect_rejects_a_filesystem_drive_root()
    {
        var result = _inspector.Inspect(Path.GetPathRoot(_root)!);

        Assert.Equal(RepositoryRootInspectionOutcome.FilesystemRootNotSupported, result.Outcome);
    }

    [Fact]
    public void Inspect_rejects_a_path_that_does_not_exist()
    {
        var result = _inspector.Inspect(Path.Combine(_root, "does-not-exist"));

        Assert.Equal(RepositoryRootInspectionOutcome.PathNotFound, result.Outcome);
    }

    [Fact]
    public void Inspect_rejects_a_path_that_is_a_file_not_a_directory()
    {
        var filePath = Path.Combine(_root, "file.txt");
        File.WriteAllText(filePath, "content");

        var result = _inspector.Inspect(filePath);

        Assert.Equal(RepositoryRootInspectionOutcome.PathNotFound, result.Outcome);
    }

    [Fact]
    public void Inspect_rejects_a_directory_symbolic_link()
    {
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        var linkPath = Path.Combine(_root, "link");

        try
        {
            Directory.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Creating a directory symlink requires elevation or Developer Mode on Windows.
            // Not this inspector's concern to prove; skip only the link-creation precondition.
            return;
        }

        var result = _inspector.Inspect(linkPath);

        Assert.Equal(RepositoryRootInspectionOutcome.ReparsePointNotSupported, result.Outcome);
    }
}
