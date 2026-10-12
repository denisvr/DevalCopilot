using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0039: the origin observation is one protected, bodyless mediator query that reads the project and the Git discovery
/// snapshot untracked, never rechecks, updates or backfills an identity, and delegates to one narrow adapter that composes only two
/// fixed local Git invocations on the existing process engine with no remote, network, credential, gh or configuration-write
/// operation, no retry and no swallowed unexpected fault.</summary>
public sealed partial class ProjectGitHubOriginBoundaryTests
{
    private const string QueryFolder = "DevalCopilot.Application/Features/Projects/Queries/GetProjectGitHubOrigin";
    private const string AdapterPath = "DevalCopilot.Infrastructure/Features/Projects/GitHubOriginAdapter.cs";
    private const string ParserPath = "DevalCopilot.Infrastructure/Features/Projects/GitHubOriginUrlParser.cs";
    private const string Endpoint = "DevalCopilot.Api/Features/Projects/GetProjectGitHubOrigin/GetProjectGitHubOriginEndpoint.cs";

    private static string BackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevalCopilot.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "backend");
    }

    private static string Code(string path) => string.Join(
        '\n', File.ReadAllLines(path).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
            && !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));

    private static string File_(string relative) => Code(Path.Combine(BackendRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string QueryCode()
    {
        var folder = Path.Combine(BackendRoot(), QueryFolder.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(folder), "The origin observation must live in its own operation folder.");
        return string.Join('\n', Directory.EnumerateFiles(folder, "*.cs").Order(StringComparer.Ordinal).Select(Code));
    }

    [GeneratedRegex(@"dbContext\.([A-Za-z]+)")]
    private static partial Regex DbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\s*\.AsNoTracking\(\)")]
    private static partial Regex UntrackedDbSetAccess();

    [Fact]
    public void The_query_reads_only_untracked_projects_and_capability_snapshots_and_writes_or_reaches_nothing()
    {
        var code = QueryCode();

        Assert.Equal(DbSetAccess().Matches(code).Count, UntrackedDbSetAccess().Matches(code).Count);
        Assert.Equal(
            ["HostCapabilitySnapshots", "Projects"],
            DbSetAccess().Matches(code).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
        foreach (var forbidden in new[]
        {
            "SaveChanges", ".Add(", ".Remove(", ".Update(", "ExecuteSql", "FromSql", "Attach(", "System.IO.File", "System.IO.Directory",
            "Process", "Http", "Task.Run", "RecordPhysicalIdentity", "PhysicalIdentityRecheck", "RepositoryMutationLease", "catch",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_query_validates_the_stored_identity_before_and_after_through_only_the_two_read_only_ports()
    {
        var code = Code(Path.Combine(BackendRoot(), QueryFolder, "GetProjectGitHubOriginQueryHandler.cs"));

        Assert.Single(Regex.Matches(code, @"rootPathInspector\.Inspect\("));
        Assert.Single(Regex.Matches(code, @"physicalIdentityInspector\.Resolve\("));
        Assert.Equal(2, Regex.Matches(code, @"CurrentCandidate\(project\)").Count);
        Assert.Single(Regex.Matches(code, @"observer\.ObserveAsync\("));
        Assert.Contains("project.PhysicalIdentityMatches(", code, StringComparison.Ordinal);
        Assert.True(
            code.IndexOf("CurrentCandidate(project)", StringComparison.Ordinal) < code.IndexOf("observer.ObserveAsync(", StringComparison.Ordinal),
            "The identity must be validated before the observer is ever invoked.");
    }

    [Fact]
    public void The_adapter_composes_only_two_fixed_local_git_invocations_on_the_existing_process_engine()
    {
        var code = File_(AdapterPath);

        Assert.Equal(2, Regex.Matches(code, @"await RunAsync\(").Count);
        Assert.Contains("\"--file\", GitHubOriginScopePin.ConfigPathOf(root), \"--no-includes\", \"--null\", \"--get-all\", \"remote.origin.url\"", code, StringComparison.Ordinal);
        Assert.True(
            code.IndexOf("_acquireScope(root)", StringComparison.Ordinal) < code.IndexOf("await RunAsync(", StringComparison.Ordinal),
            "The administrative scope must be held before the first Git invocation.");
        Assert.Contains("scope.IsIntact()", code, StringComparison.Ordinal);
        Assert.Contains("\"rev-parse\", \"--is-bare-repository\", \"--git-dir\", \"--git-common-dir\", \"--show-toplevel\"", code, StringComparison.Ordinal);
        Assert.Contains("IProcessExecutionAdapter", code, StringComparison.Ordinal);
        Assert.Contains("GIT_CONFIG_NOSYSTEM", code, StringComparison.Ordinal);
        Assert.Contains("GIT_CONFIG_GLOBAL", code, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "System.Diagnostics", "Process.Start", "ProcessStartInfo", "UseShellExecute", "cmd.exe", "powershell", "get-url", "ls-remote",
            "fetch", "\"push\"", "pushurl", "\"gh\"", "gh.exe", "credential", "\"--local\"", "\"--global\"", "\"--system\"", "\"--worktree\"", "--unset",
            "--add", "--replace", "--edit", "\"-e\"", "Environment.GetEnvironmentVariable", "SaveChanges", "File.Write", "Directory.Create",
            "StandardOutputSinkPath", "StandardErrorSinkPath", "StandardInput", "retry", "Retry", "Delay(", "Task.Run", "catch (Exception",
            "catch\n", "ILogger",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_adapter_catches_only_cancellation_and_the_two_expected_launch_failures()
    {
        var code = File_(AdapterPath);

        var catches = Regex.Matches(code, @"catch \(([A-Za-z0-9]+)").Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(["OperationCanceledException", "Win32Exception", "ArgumentException"], catches);
    }

    [Fact]
    public void The_scope_pin_is_a_read_only_hold_without_delete_sharing_that_catches_only_io_refusals()
    {
        var code = File_("DevalCopilot.Infrastructure/Features/Projects/GitHubOriginScopePin.cs");

        Assert.Contains("FileMode.Open, FileAccess.Read, FileShare.ReadWrite)", code, StringComparison.Ordinal);
        Assert.Contains("FileAttributes.ReparsePoint", code, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "FileShare.Delete", "FileShare.None", "FileAccess.Write", "FileAccess.ReadWrite", "FileMode.Create", "FileMode.Append",
            "File.Write", "File.Delete", "Directory.Create", "Directory.Delete", "Process", "catch (Exception)",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        var catches = Regex.Matches(code, @"catch \(Exception exception\) when \(exception is ([A-Za-z]+) or ([A-Za-z]+)\)")
            .Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}").ToArray();
        Assert.Equal(["IOException|UnauthorizedAccessException", "IOException|UnauthorizedAccessException"], catches);
        Assert.Equal(2, Regex.Matches(code, @"catch \(").Count);
    }

    [Fact]
    public void The_parser_is_a_pure_string_function()
    {
        var code = File_(ParserPath);

        foreach (var forbidden in new[]
        {
            "Uri", "Regex", "Unescape", "Decode", ".Trim(", "ToLower", "ToUpper", "IO.", "Process", "Http", "Environment",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_endpoint_is_one_protected_bodyless_get_of_the_project_route()
    {
        var code = File_(Endpoint);

        Assert.Contains(": ProjectsBaseEndpoint", code, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"{projectId:guid}/github-origin\")]", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"\[Http[A-Za-z]+\("));
        Assert.DoesNotContain("[FromBody]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromQuery]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowAnonymous", code, StringComparison.Ordinal);
        Assert.Contains("mediator.SendAsync(new GetProjectGitHubOriginQuery(projectId)", code, StringComparison.Ordinal);
    }
}
