using System.Diagnostics;
using System.Security.Cryptography;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectGitHubOrigin;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>
/// The repository-local GitHub origin observer (ADR-0039) against real <c>git.exe</c> and real disposable repositories:
/// local-only scope, missing, single and duplicate origins, ignored global, included, worktree, command-line and rewritten
/// definitions, hostile values, refused parent, linked, bare and separate-directory shapes, no hook or filesystem-monitor
/// execution and no write of any kind. No remote, network or account is ever involved.
///
/// <para>
/// Shares <see cref="EnvironmentPathMutationCollection"/> with the other classes that resolve the real Git executable.
/// </para>
/// </summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class GitHubOriginAdapterRealGitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-git-origin-{Guid.NewGuid():N}");
    private readonly string _git;

    public GitHubOriginAdapterRealGitTests()
    {
        Directory.CreateDirectory(_root);
        var descriptor = HostCapabilityCatalog.Get(Capability.Git);
        _git = HostExecutableResolver.TryResolve(descriptor.CandidateExecutableNames, descriptor.FallbackDirectories)
            ?? throw new InvalidOperationException("Git is required for these tests.");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    /// <summary>Adds ambient variables a hostile or merely unusual developer machine could have, beneath the adapter's own.</summary>
    private sealed class AmbientLeakingProcessAdapter(IReadOnlyDictionary<string, string> ambient) : IProcessExecutionAdapter
    {
        private readonly ChildProcessExecutionAdapter _inner = new();

        public List<ProcessExecutionRequest> Requests { get; } = [];

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var environment = ambient.ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (var (key, value) in request.EnvironmentVariables)
            {
                environment[key] = value;
            }

            return _inner.ExecuteAsync(request with { EnvironmentVariables = environment }, cancellationToken);
        }
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        process.Start();
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private string CreateRepo(string name, bool withCommit = true)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        RunGit(path, "init", "-q");
        RunGit(path, "config", "user.email", "test@example.com");
        RunGit(path, "config", "user.name", "Test");
        if (withCommit)
        {
            File.WriteAllText(Path.Combine(path, "file.txt"), "content");
            RunGit(path, "add", "file.txt");
            RunGit(path, "commit", "-q", "-m", "initial");
        }

        return path;
    }

    private string CreateRepoWithOrigin(string name, string url)
    {
        var path = CreateRepo(name);
        RunGit(path, "config", "remote.origin.url", url);
        return path;
    }

    private static GitHubOriginAdapter Adapter(IProcessExecutionAdapter? process = null) =>
        new(process ?? new ChildProcessExecutionAdapter(), TimeProvider.System);

    private Task<GitHubOriginObservation> ObserveAsync(string path, IProcessExecutionAdapter? process = null) =>
        Adapter(process).ObserveAsync(_git, new RepositoryRootCandidate(path), CancellationToken.None);

    private static void AssertAvailable(GitHubOriginObservation observation, string owner, string repository)
    {
        Assert.Equal(GitHubOriginStatus.Available, observation.Status);
        Assert.Equal(new GitHubOriginTarget(owner, repository), observation.Target);
    }

    private static void AssertRefused(GitHubOriginObservation observation) =>
        Assert.Equal(GitHubOriginObservation.Unavailable, observation);

    [Theory]
    [InlineData("https://github.com/acme/widgets")]
    [InlineData("https://github.com/acme/widgets.git")]
    [InlineData("git@github.com:acme/widgets")]
    [InlineData("git@github.com:acme/widgets.git")]
    [InlineData("ssh://git@github.com/acme/widgets")]
    [InlineData("ssh://git@github.com/acme/widgets.git")]
    public async Task A_local_origin_in_each_supported_spelling_is_the_exact_normalized_target(string url)
    {
        var path = CreateRepoWithOrigin("supported", url);

        AssertAvailable(await ObserveAsync(path), "acme", "widgets");
    }

    [Fact]
    public async Task A_repository_without_commits_is_still_observed_because_no_status_or_head_is_read()
    {
        var path = CreateRepo("unborn", withCommit: false);
        RunGit(path, "config", "remote.origin.url", "https://github.com/acme/widgets");

        AssertAvailable(await ObserveAsync(path), "acme", "widgets");
    }

    [Fact]
    public async Task A_repository_with_no_origin_is_not_configured()
    {
        var path = CreateRepo("none");

        var observation = await ObserveAsync(path);

        Assert.Equal(GitHubOriginObservation.NotConfigured, observation);
    }

    [Fact]
    public async Task Another_remote_name_is_never_an_origin()
    {
        var path = CreateRepo("upstream-only");
        RunGit(path, "config", "remote.upstream.url", "https://github.com/acme/widgets");
        RunGit(path, "config", "remote.origin.pushurl", "https://github.com/acme/widgets");
        RunGit(path, "config", "remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/*");

        Assert.Equal(GitHubOriginObservation.NotConfigured, await ObserveAsync(path));
    }

    [Theory]
    [InlineData("https://github.com/acme/widgets", "https://github.com/acme/widgets")]
    [InlineData("https://github.com/acme/widgets", "git@github.com:other/repository")]
    public async Task Duplicate_local_origin_values_are_unavailable_even_when_identical(string first, string second)
    {
        var path = CreateRepoWithOrigin("duplicate", first);
        RunGit(path, "config", "--add", "remote.origin.url", second);

        AssertRefused(await ObserveAsync(path));
    }

    [Fact]
    public async Task A_url_rewrite_and_a_push_url_never_replace_the_configured_value_or_leak_their_text()
    {
        var path = CreateRepoWithOrigin("rewrite", "https://github.com/acme/widgets");
        RunGit(path, "config", "url.https://evil.example/.insteadOf", "https://github.com/");
        RunGit(path, "config", "url.git@evil.example:.pushInsteadOf", "https://github.com/");
        RunGit(path, "config", "remote.origin.pushurl", "https://github.com/other/destination");

        AssertAvailable(await ObserveAsync(path), "acme", "widgets");
    }

    [Fact]
    public async Task A_rewrite_does_not_turn_a_missing_origin_into_one()
    {
        var path = CreateRepo("rewrite-missing");
        RunGit(path, "config", "url.https://github.com/acme/widgets.insteadOf", "anything");

        Assert.Equal(GitHubOriginObservation.NotConfigured, await ObserveAsync(path));
    }

    [Fact]
    public async Task Included_and_conditionally_included_definitions_are_never_a_candidate()
    {
        var path = CreateRepo("included");
        File.WriteAllText(
            Path.Combine(path, ".git", "extra.config"), "[remote \"origin\"]\n\turl = https://github.com/included/repo\n");
        RunGit(path, "config", "include.path", "extra.config");
        RunGit(path, "config", "includeIf.gitdir:**.path", "extra.config");

        Assert.Equal(GitHubOriginObservation.NotConfigured, await ObserveAsync(path));

        RunGit(path, "config", "remote.origin.url", "https://github.com/acme/widgets");
        AssertAvailable(await ObserveAsync(path), "acme", "widgets");
    }

    [Fact]
    public async Task A_worktree_scope_definition_is_never_a_candidate()
    {
        var path = CreateRepo("worktree-scope");
        RunGit(path, "config", "extensions.worktreeConfig", "true");
        RunGit(path, "config", "--worktree", "remote.origin.url", "https://github.com/worktree/scope");
        Assert.True(File.Exists(Path.Combine(path, ".git", "config.worktree")));

        Assert.Equal(GitHubOriginObservation.NotConfigured, await ObserveAsync(path));
    }

    [Fact]
    public async Task Ambient_global_system_and_command_line_configuration_cannot_reach_the_child()
    {
        var path = CreateRepo("ambient");
        var home = Path.Combine(_root, "home");
        Directory.CreateDirectory(home);
        File.WriteAllText(
            Path.Combine(home, ".gitconfig"),
            "[remote \"origin\"]\n\turl = https://github.com/global/home\n[url \"https://evil.example/\"]\n\tinsteadOf = https://github.com/\n");
        var xdg = Path.Combine(_root, "xdg", "git");
        Directory.CreateDirectory(xdg);
        File.WriteAllText(Path.Combine(xdg, "config"), "[remote \"origin\"]\n\turl = https://github.com/global/xdg\n");
        var ambient = new Dictionary<string, string>
        {
            ["HOME"] = home,
            ["USERPROFILE"] = home,
            ["XDG_CONFIG_HOME"] = Path.Combine(_root, "xdg"),
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "remote.origin.url",
            ["GIT_CONFIG_VALUE_0"] = "https://github.com/command/line",
            ["GIT_CONFIG_PARAMETERS"] = "'remote.origin.url=https://github.com/command/parameters'",
        };
        var process = new AmbientLeakingProcessAdapter(ambient);

        Assert.Equal(GitHubOriginObservation.NotConfigured, await ObserveAsync(path, process));

        RunGit(path, "config", "remote.origin.url", "git@github.com:acme/widgets.git");
        Assert.NotEmpty(process.Requests);
        AssertAvailable(await ObserveAsync(path, process), "acme", "widgets");
    }

    [Theory]
    [InlineData("https://user@github.com/acme/widgets")]
    [InlineData("https://user:secret@github.com/acme/widgets")]
    [InlineData("https://github.com:443/acme/widgets")]
    [InlineData("https://github.com/acme/widgets/")]
    [InlineData("https://github.com/acme/widgets/extra")]
    [InlineData("https://github.com/acme/widgets?x=1")]
    [InlineData("https://github.com/acme/widgets#x")]
    [InlineData("https://github.com/acme/wid%67ets")]
    [InlineData("https://GitHub.com/acme/widgets")]
    [InlineData("https://github.com.evil.example/acme/widgets")]
    [InlineData("http://github.com/acme/widgets")]
    [InlineData("git://github.com/acme/widgets")]
    [InlineData("ssh://other@github.com/acme/widgets")]
    [InlineData("ssh://git@github.com:22/acme/widgets")]
    [InlineData("git@gitlab.com:acme/widgets")]
    [InlineData("C:/repos/widgets")]
    [InlineData("../widgets")]
    [InlineData("file:///srv/widgets")]
    [InlineData("https://github.com/acme/.git")]
    [InlineData("https://github.com/-acme/widgets")]
    [InlineData("origin")]
    public async Task A_hostile_or_unsupported_local_value_is_unavailable_without_being_normalized(string url)
    {
        var path = CreateRepoWithOrigin("hostile", url);

        AssertRefused(await ObserveAsync(path));
    }

    [Fact]
    public async Task A_value_with_surrounding_whitespace_or_control_characters_is_unavailable()
    {
        var path = CreateRepo("controls");
        File.AppendAllText(
            Path.Combine(path, ".git", "config"), "[remote \"origin\"]\n\turl = \"https://github.com/acme/widgets\\t\"\n");

        AssertRefused(await ObserveAsync(path));
    }

    [Fact]
    public async Task A_subdirectory_of_a_repository_is_refused_and_so_is_a_plain_directory_inside_one()
    {
        var path = CreateRepoWithOrigin("parent", "https://github.com/acme/widgets");
        var subdirectory = Path.Combine(path, "src");
        Directory.CreateDirectory(subdirectory);
        var plain = Path.Combine(path, "plain-not-a-repository");
        Directory.CreateDirectory(plain);

        AssertRefused(await ObserveAsync(subdirectory));
        AssertRefused(await ObserveAsync(plain));
        AssertRefused(await ObserveAsync(Path.Combine(path, ".git")));
    }

    [Fact]
    public async Task A_directory_that_is_not_in_any_repository_is_refused()
    {
        var path = Path.Combine(_root, "not-a-repository");
        Directory.CreateDirectory(path);

        AssertRefused(await ObserveAsync(path));
    }

    [Fact]
    public async Task A_linked_worktree_is_refused_even_though_it_shares_a_configured_origin()
    {
        var main = CreateRepoWithOrigin("main-checkout", "https://github.com/acme/widgets");
        var linked = Path.Combine(_root, "linked-checkout");
        RunGit(main, "worktree", "add", "-q", linked, "-b", "linked-branch");

        AssertRefused(await ObserveAsync(linked));
        AssertAvailable(await ObserveAsync(main), "acme", "widgets");
    }

    [Fact]
    public async Task A_bare_repository_is_refused()
    {
        var bare = Path.Combine(_root, "bare.git");
        Directory.CreateDirectory(bare);
        RunGit(bare, "init", "-q", "--bare");
        RunGit(bare, "config", "remote.origin.url", "https://github.com/acme/widgets");

        AssertRefused(await ObserveAsync(bare));
    }

    [Fact]
    public async Task A_separate_git_directory_is_refused_because_the_configuration_is_not_the_roots_own()
    {
        var path = Path.Combine(_root, "separate");
        var gitDirectory = Path.Combine(_root, "separate-git-dir");
        Directory.CreateDirectory(path);
        RunGit(path, "init", "-q", "--separate-git-dir", gitDirectory);
        RunGit(path, "config", "remote.origin.url", "https://github.com/acme/widgets");

        AssertRefused(await ObserveAsync(path));
    }

    /// <summary>Runs the real executor, but first lets a hostile actor try to replace the administrative scope just before the
    /// configuration invocation (the second call). The attempt's failure is recorded, not hidden.</summary>
    private sealed class SwappingProcessAdapter(Action swap) : IProcessExecutionAdapter
    {
        private readonly ChildProcessExecutionAdapter _inner = new();
        private int _calls;

        public bool SwapBlocked { get; private set; }

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            if (++_calls == 2)
            {
                try
                {
                    swap();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    SwapBlocked = true;
                }
            }

            return _inner.ExecuteAsync(request, cancellationToken);
        }
    }

    private static void MakeJunction(string link, string target)
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
        startInfo.ArgumentList.Add(link);
        startInfo.ArgumentList.Add(target);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"mklink /J failed: {output}");
    }

    /// <summary>Never a foreign origin: either nothing observable, or exactly the admitted repository's own value.</summary>
    private static void AssertNeverForeign(GitHubOriginObservation observation, string owner, string repository)
    {
        if (observation.Status == GitHubOriginStatus.Available)
        {
            Assert.Equal(new GitHubOriginTarget(owner, repository), observation.Target);
        }
        else
        {
            Assert.Equal(GitHubOriginObservation.Unavailable, observation);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replacing_the_administrative_scope_between_the_two_invocations_never_yields_a_foreign_origin(bool parentFallback)
    {
        var foreign = CreateRepoWithOrigin("race-foreign", "https://github.com/foreign/other");
        var owned = parentFallback ? Path.Combine(foreign, "nested") : Path.Combine(_root, "race-owned");
        Directory.CreateDirectory(owned);
        RunGit(owned, "init", "-q");
        RunGit(owned, "config", "remote.origin.url", "https://github.com/expected/project");
        var moved = Path.Combine(_root, "race-moved-git");
        var process = new SwappingProcessAdapter(() =>
        {
            Directory.Move(Path.Combine(owned, ".git"), moved);
            if (!parentFallback)
            {
                File.WriteAllText(Path.Combine(owned, ".git"), "gitdir: " + Path.Combine(foreign, ".git").Replace('\\', '/') + "\n");
            }
        });

        var observation = await ObserveAsync(owned, process);

        AssertNeverForeign(observation, "expected", "project");
        Assert.True(process.SwapBlocked, "The held administrative scope must refuse the replacement while it is observed.");
    }

    [Fact]
    public async Task Deleting_the_configuration_between_the_two_invocations_is_never_not_configured()
    {
        var path = CreateRepoWithOrigin("race-config", "https://github.com/acme/widgets");
        var process = new SwappingProcessAdapter(() => File.Delete(Path.Combine(path, ".git", "config")));

        var observation = await ObserveAsync(path, process);

        AssertNeverForeign(observation, "acme", "widgets");
        Assert.NotEqual(GitHubOriginStatus.NotConfigured, observation.Status);
    }

    [Fact]
    public async Task A_git_directory_that_is_a_junction_to_another_repository_is_unavailable()
    {
        var foreign = CreateRepoWithOrigin("junction-foreign", "https://github.com/foreign/other");
        var owned = Path.Combine(_root, "junction-owned");
        Directory.CreateDirectory(owned);
        var junction = Path.Combine(owned, ".git");
        MakeJunction(junction, Path.Combine(foreign, ".git"));

        try
        {
            AssertRefused(await ObserveAsync(owned));
        }
        finally
        {
            // Removes only the junction itself, so the recursive cleanup never has to cross it.
            Directory.Delete(junction);
        }
    }

    [Fact]
    public async Task A_missing_configuration_file_in_an_otherwise_intact_git_directory_is_unavailable_not_not_configured()
    {
        var path = CreateRepo("no-config-file");
        File.Delete(Path.Combine(path, ".git", "config"));

        AssertRefused(await ObserveAsync(path));
    }

    /// <summary>Privilege-gated: a file symbolic link needs a privilege this account may lack, in which case the case cannot be
    /// built and this test exercises nothing (stated as a limitation in the handoff, not hidden).</summary>
    [Fact]
    public async Task A_configuration_that_is_a_symbolic_link_to_another_file_is_unavailable_when_one_can_be_created()
    {
        var foreign = CreateRepoWithOrigin("symlink-foreign", "https://github.com/foreign/other");
        var path = CreateRepo("symlink-config");
        var config = Path.Combine(path, ".git", "config");
        File.Delete(config);
        try
        {
            File.CreateSymbolicLink(config, Path.Combine(foreign, ".git", "config"));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return;
        }

        AssertRefused(await ObserveAsync(path));
        File.Delete(config);
    }

    [Fact]
    public async Task A_configuration_that_is_a_directory_is_unavailable()
    {
        var path = CreateRepo("config-directory");
        File.Delete(Path.Combine(path, ".git", "config"));
        Directory.CreateDirectory(Path.Combine(path, ".git", "config"));

        AssertRefused(await ObserveAsync(path));
    }

    [Fact]
    public async Task The_observation_releases_its_hold_on_the_administrative_scope_afterwards()
    {
        var path = CreateRepoWithOrigin("released", "https://github.com/acme/widgets");

        AssertAvailable(await ObserveAsync(path), "acme", "widgets");

        File.Move(Path.Combine(path, ".git", "config"), Path.Combine(path, ".git", "config.moved"));
        Directory.Move(Path.Combine(path, ".git"), Path.Combine(_root, "released-moved-git"));
    }

    [Fact]
    public async Task A_nonexistent_root_is_unavailable_not_a_fault()
    {
        AssertRefused(await ObserveAsync(Path.Combine(_root, "never-created")));
    }

    [Fact]
    public async Task A_hostile_repository_configuration_executes_nothing()
    {
        var path = CreateRepoWithOrigin("hostile-config", "https://github.com/acme/widgets");
        var sentinel = Path.Combine(_root, "executed.sentinel");
        var script = Path.Combine(_root, "touch.cmd");
        File.WriteAllText(script, $"@echo executed> \"{sentinel}\"\r\n");
        var quoted = script.Replace('\\', '/');
        RunGit(path, "config", "core.fsmonitor", quoted);
        RunGit(path, "config", "core.sshCommand", quoted);
        RunGit(path, "config", "core.pager", quoted);
        RunGit(path, "config", "credential.helper", quoted);
        RunGit(path, "config", "alias.rev-parse", $"!{quoted}");
        var hooks = Path.Combine(path, ".git", "hooks");
        File.WriteAllText(Path.Combine(hooks, "reference-transaction"), $"#!/bin/sh\necho executed > \"{sentinel.Replace('\\', '/')}\"\n");

        AssertAvailable(await ObserveAsync(path), "acme", "widgets");
        Assert.False(File.Exists(sentinel));
    }

    [Fact]
    public async Task Observation_writes_nothing_to_the_repository_or_its_working_tree()
    {
        var path = CreateRepoWithOrigin("no-writes", "https://github.com/acme/widgets");
        File.WriteAllText(Path.Combine(path, "untracked.txt"), "untracked");
        File.WriteAllText(Path.Combine(path, "file.txt"), "modified");
        var before = Fingerprint(path);

        AssertAvailable(await ObserveAsync(path), "acme", "widgets");
        AssertAvailable(await ObserveAsync(path), "acme", "widgets");
        Assert.Empty(Directory.GetFiles(Path.Combine(path, ".git"), "*.lock", SearchOption.AllDirectories));

        Assert.Equal(before, Fingerprint(path));
    }

    [Fact]
    public async Task A_missing_origin_observation_also_writes_nothing()
    {
        var path = CreateRepo("no-writes-missing");
        var before = Fingerprint(path);

        Assert.Equal(GitHubOriginObservation.NotConfigured, await ObserveAsync(path));

        Assert.Equal(before, Fingerprint(path));
    }

    /// <summary>Every file of the repository directory and working tree by relative path, size, last write and content hash.</summary>
    private static SortedDictionary<string, string> Fingerprint(string root)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(file);
            using var stream = File.OpenRead(file);
            result[Path.GetRelativePath(root, file)] =
                $"{info.Length}|{info.LastWriteTimeUtc.Ticks}|{Convert.ToHexString(SHA256.HashData(stream))}";
        }

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            result[Path.GetRelativePath(root, directory) + "\\"] = string.Empty;
        }

        return result;
    }
}
