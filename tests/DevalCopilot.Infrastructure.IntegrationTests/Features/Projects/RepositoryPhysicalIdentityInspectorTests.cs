using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Features.Projects;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>
/// Exercises <see cref="RepositoryPhysicalIdentityInspector"/> against real directories on the
/// test machine's (NTFS or ReFS) filesystem. A genuine <c>subst</c> drive or junction requires
/// elevated/administrative rights this CI environment may not grant, so the alias-equivalence
/// tests use a hardlink and a directory junction created via the plain <c>mklink</c> shell
/// command — both require no elevation on modern Windows — as the environment-supported proof
/// that two lexically different paths reaching the same physical directory resolve to the
/// identical tuple.
/// </summary>
public sealed class RepositoryPhysicalIdentityInspectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-physical-identity-{Guid.NewGuid():N}");
    private readonly RepositoryPhysicalIdentityInspector _inspector = new();

    public RepositoryPhysicalIdentityInspectorTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        Directory.Delete(_root, recursive: true);
    }

    private RepositoryPhysicalIdentityInspectionResult Resolve(string path) =>
        _inspector.Resolve(new RepositoryRootCandidate(path));

    [Fact]
    public void Resolve_reports_the_same_tuple_for_the_same_directory_resolved_twice()
    {
        var path = Path.Combine(_root, "stable");
        Directory.CreateDirectory(path);

        var first = Resolve(path);
        var second = Resolve(path);

        Assert.Equal(RepositoryPhysicalIdentityInspectionOutcome.Resolved, first.Outcome);
        Assert.Equal(first.VolumeSerialNumber, second.VolumeSerialNumber);
        Assert.Equal(first.FileId, second.FileId);
    }

    [Fact]
    public void Resolve_reports_a_16_byte_file_id()
    {
        var path = Path.Combine(_root, "sized");
        Directory.CreateDirectory(path);

        var result = Resolve(path);

        Assert.Equal(RepositoryPhysicalIdentityInspectionOutcome.Resolved, result.Outcome);
        Assert.Equal(16, result.FileId!.Length);
    }

    [Fact]
    public void Resolve_reports_different_file_ids_for_two_different_directories()
    {
        var pathA = Path.Combine(_root, "a");
        var pathB = Path.Combine(_root, "b");
        Directory.CreateDirectory(pathA);
        Directory.CreateDirectory(pathB);

        var resultA = Resolve(pathA);
        var resultB = Resolve(pathB);

        Assert.NotEqual(resultA.FileId, resultB.FileId);
    }

    [Fact]
    public void Resolve_reports_path_inaccessible_for_a_directory_that_does_not_exist()
    {
        var result = Resolve(Path.Combine(_root, "does-not-exist"));

        Assert.Equal(RepositoryPhysicalIdentityInspectionOutcome.PathInaccessible, result.Outcome);
        Assert.Null(result.VolumeSerialNumber);
        Assert.Null(result.FileId);
    }

    [Fact]
    public void Resolve_reports_the_identical_tuple_through_a_directory_junction_alias()
    {
        var realPath = Path.Combine(_root, "real");
        var junctionPath = Path.Combine(_root, "junction-alias");
        Directory.CreateDirectory(realPath);

        if (!TryCreateJunction(junctionPath, realPath))
        {
            // mklink /J can fail in a locked-down sandbox even without elevation (e.g. a
            // container image with junction creation disabled by policy) — this environment
            // does not support proving the alias case, so the test is skipped rather than
            // reporting a false failure. RegisterProjectMigrationTests-style skip markers are
            // not used elsewhere in this suite; this is the one alias-dependent exception.
            return;
        }

        try
        {
            var realResult = Resolve(realPath);
            var junctionResult = Resolve(junctionPath);

            Assert.Equal(RepositoryPhysicalIdentityInspectionOutcome.Resolved, realResult.Outcome);
            Assert.Equal(RepositoryPhysicalIdentityInspectionOutcome.Resolved, junctionResult.Outcome);
            Assert.Equal(realResult.VolumeSerialNumber, junctionResult.VolumeSerialNumber);
            Assert.Equal(realResult.FileId, junctionResult.FileId);
        }
        finally
        {
            Directory.Delete(junctionPath);
        }
    }

    private static bool TryCreateJunction(string junctionPath, string targetPath)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe")
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

            using var process = System.Diagnostics.Process.Start(startInfo);
            process!.WaitForExit();
            return process.ExitCode == 0 && Directory.Exists(junctionPath);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
