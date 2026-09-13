using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>
/// Real filesystem, fabricated candidate names — never a real installed tool name — so this
/// never depends on what happens to be installed on the machine running the tests. The PATH
/// value under test is always passed explicitly to <see cref="HostExecutableResolver.TryResolve"/>
/// rather than mutating the process-wide <c>PATH</c> environment variable, which would not be
/// safe under parallel test execution.
/// </summary>
public sealed class HostExecutableResolverTests : IDisposable
{
    private readonly string _pathDirectory =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-resolver-path-{Guid.NewGuid():N}");
    private readonly string _fallbackDirectory =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-resolver-fallback-{Guid.NewGuid():N}");

    public HostExecutableResolverTests()
    {
        Directory.CreateDirectory(_pathDirectory);
        Directory.CreateDirectory(_fallbackDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_pathDirectory))
        {
            Directory.Delete(_pathDirectory, recursive: true);
        }

        if (Directory.Exists(_fallbackDirectory))
        {
            Directory.Delete(_fallbackDirectory, recursive: true);
        }
    }

    [Fact]
    public void TryResolve_finds_a_candidate_on_a_valid_absolute_path_entry()
    {
        var candidatePath = Path.Combine(_pathDirectory, "devalcopilot-fake-tool.exe");
        File.WriteAllText(candidatePath, string.Empty);

        var resolved = HostExecutableResolver.TryResolve(["devalcopilot-fake-tool.exe"], [], hostPathVariable: _pathDirectory);

        Assert.Equal(candidatePath, resolved);
    }

    [Fact]
    public void TryResolve_finds_a_candidate_only_present_in_a_fixed_fallback_directory()
    {
        var candidatePath = Path.Combine(_fallbackDirectory, "devalcopilot-fake-tool.exe");
        File.WriteAllText(candidatePath, string.Empty);

        var resolved = HostExecutableResolver.TryResolve(
            ["devalcopilot-fake-tool.exe"], [_fallbackDirectory], hostPathVariable: string.Empty);

        Assert.Equal(candidatePath, resolved);
    }

    [Fact]
    public void TryResolve_prefers_a_path_match_over_a_fallback_directory_match()
    {
        var pathCandidate = Path.Combine(_pathDirectory, "devalcopilot-fake-tool.exe");
        var fallbackCandidate = Path.Combine(_fallbackDirectory, "devalcopilot-fake-tool.exe");
        File.WriteAllText(pathCandidate, string.Empty);
        File.WriteAllText(fallbackCandidate, string.Empty);

        var resolved = HostExecutableResolver.TryResolve(
            ["devalcopilot-fake-tool.exe"], [_fallbackDirectory], hostPathVariable: _pathDirectory);

        Assert.Equal(pathCandidate, resolved);
    }

    [Fact]
    public void TryResolve_returns_null_when_no_candidate_name_exists_anywhere_searched()
    {
        var resolved = HostExecutableResolver.TryResolve(
            [$"devalcopilot-nonexistent-{Guid.NewGuid():N}.exe"], [_fallbackDirectory], hostPathVariable: _pathDirectory);

        Assert.Null(resolved);
    }

    [Fact]
    public void TryResolve_tries_multiple_candidate_names_in_order()
    {
        var secondCandidatePath = Path.Combine(_pathDirectory, "second-name.exe");
        File.WriteAllText(secondCandidatePath, string.Empty);

        var resolved = HostExecutableResolver.TryResolve(
            ["first-name.exe", "second-name.exe"], [], hostPathVariable: _pathDirectory);

        Assert.Equal(secondCandidatePath, resolved);
    }

    [Fact]
    public void TryResolve_never_finds_an_executable_via_a_relative_path_entry_even_if_it_exists_in_the_current_directory()
    {
        // A file genuinely present in the process's own current working directory — exactly
        // what a relative or "." PATH entry would (unsafely) resolve against if it were ever
        // combined with a candidate name directly.
        var candidateName = $"devalcopilot-cwd-tool-{Guid.NewGuid():N}.exe";
        var pathInCurrentDirectory = Path.Combine(Directory.GetCurrentDirectory(), candidateName);
        File.WriteAllText(pathInCurrentDirectory, string.Empty);
        try
        {
            var resolvedViaDot = HostExecutableResolver.TryResolve([candidateName], [], hostPathVariable: ".");
            var resolvedViaRelativeSegment = HostExecutableResolver.TryResolve([candidateName], [], hostPathVariable: "relative\\segment");
            var resolvedViaDriveRelative = HostExecutableResolver.TryResolve([candidateName], [], hostPathVariable: "C:tools");

            Assert.Null(resolvedViaDot);
            Assert.Null(resolvedViaRelativeSegment);
            Assert.Null(resolvedViaDriveRelative);
        }
        finally
        {
            File.Delete(pathInCurrentDirectory);
        }
    }

    [Fact]
    public void TryResolve_ignores_a_relative_fallback_directory_the_same_way()
    {
        var candidateName = $"devalcopilot-cwd-tool-{Guid.NewGuid():N}.exe";
        var pathInCurrentDirectory = Path.Combine(Directory.GetCurrentDirectory(), candidateName);
        File.WriteAllText(pathInCurrentDirectory, string.Empty);
        try
        {
            var resolved = HostExecutableResolver.TryResolve([candidateName], ["."], hostPathVariable: string.Empty);

            Assert.Null(resolved);
        }
        finally
        {
            File.Delete(pathInCurrentDirectory);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("tools")]
    [InlineData(@"tools\git")]
    [InlineData("C:tools")]
    public void BuildOrderedUniqueSearchDirectories_rejects_empty_relative_and_drive_relative_entries(string malformedEntry)
    {
        var result = HostExecutableResolver.BuildOrderedUniqueSearchDirectories(malformedEntry, []);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildOrderedUniqueSearchDirectories_accepts_and_canonicalizes_fully_qualified_entries()
    {
        var pathVariable = Path.Combine(_pathDirectory, ".");

        var result = HostExecutableResolver.BuildOrderedUniqueSearchDirectories(pathVariable, []);

        Assert.Equal([Path.GetFullPath(_pathDirectory)], result);
    }

    [Fact]
    public void BuildOrderedUniqueSearchDirectories_deduplicates_case_insensitively_while_preserving_first_seen_order()
    {
        var upper = _pathDirectory.ToUpperInvariant();
        var lower = _pathDirectory.ToLowerInvariant();
        var pathVariable = string.Join(Path.PathSeparator, upper, _fallbackDirectory, lower);

        var result = HostExecutableResolver.BuildOrderedUniqueSearchDirectories(pathVariable, [_fallbackDirectory]);

        // The first-seen casing wins, the duplicate later in PATH is dropped, and the fallback
        // directory (already present via PATH) contributes no second entry either.
        Assert.Equal([Path.GetFullPath(upper), Path.GetFullPath(_fallbackDirectory)], result);
    }

    [Fact]
    public void BuildOrderedUniqueSearchDirectories_preserves_search_order_across_path_then_fallback()
    {
        var secondPathDirectory = Path.Combine(Path.GetTempPath(), $"devalcopilot-resolver-path2-{Guid.NewGuid():N}");
        var pathVariable = string.Join(Path.PathSeparator, _pathDirectory, secondPathDirectory);

        var result = HostExecutableResolver.BuildOrderedUniqueSearchDirectories(pathVariable, [_fallbackDirectory]);

        Assert.Equal(
            [Path.GetFullPath(_pathDirectory), Path.GetFullPath(secondPathDirectory), Path.GetFullPath(_fallbackDirectory)],
            result);
    }
}
