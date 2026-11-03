using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>The closed inventory of lot B against the exact whole-request shapes the unchanged production composition issues (resolved
/// offline by rehearsal) and against every shape that must be refused before a process exists: complete role argument vectors,
/// environments, deadlines, destinations, approved launch tuples and file identities. Owned stand-in files only.</summary>
public sealed class Profile01InventoryTests : QualificationTestBase
{
    private ApprovedFixture Fixture(bool codexScript = true, bool withOptional = true) =>
        new(Path.Combine(LedgerDirectory, "approved"), codexScript, withOptional);

    private ProcessAdmission NewAdmission(ApprovedFixture fixture, bool pin = true, int totalLimit = -1) =>
        fixture.Admission(Path.Combine(LedgerDirectory, "inventory"), pin, totalLimit);

    public static TheoryData<string, int> PinnedCeilings => new()
    {
        { "discovery.git", 8 }, { "discovery.dotnet", 8 }, { "discovery.node", 8 }, { "discovery.gh", 8 },
        { "discovery.docker", 8 }, { "discovery.codex", 8 }, { "discovery.claude", 8 },
        { "role.codex.exec", 1 }, { "role.claude.print", 1 },
        { "launcher.git.init", 1 }, { "launcher.git.config", 1 }, { "launcher.git.add", 1 }, { "launcher.git.commit", 1 },
        { "launcher.git.head", 10 }, { "launcher.git.headref", 9 }, { "launcher.git.refs", 9 }, { "launcher.git.index", 9 },
        { "git.revparse.gitdir", 3 }, { "git.revparse.bare", 2 }, { "git.revparse.toplevel", 2 },
        { "git.revparse.commondir", 4 }, { "git.symbolicref", 4 }, { "git.revparse.head", 4 },
        { "git.status.porcelain", 2 }, { "git.worktree.add", 1 },
        { "git.evidence.revparse", 14 }, { "git.evidence.status", 14 }, { "git.evidence.diff", 14 },
        { "git.evidence.hashobject", 8 }, { "git.evidence.lsfiles.tracked", 4 }, { "git.evidence.lsfiles.others", 4 },
    };

    [Theory]
    [MemberData(nameof(PinnedCeilings))]
    public void Every_class_has_exactly_its_resolved_ceiling(string name, int limit)
    {
        var (entries, _) = Profile01Inventory.Create(new JudgedTargets());

        Assert.Equal(limit, Assert.Single(entries, entry => entry.Name == name).Limit);
    }

    [Fact]
    public void The_inventory_lists_exactly_the_pinned_classes_and_its_total_is_their_sum_of_179_starts()
    {
        var (entries, total) = Profile01Inventory.Create(new JudgedTargets());

        Assert.Equal(PinnedCeilings.Select(row => (string)row[0]).Order(StringComparer.Ordinal), entries.Select(entry => entry.Name).Order(StringComparer.Ordinal));
        Assert.Equal(PinnedCeilings.Sum(row => (int)row[1]), total);
        Assert.Equal(179, total);
        Assert.Equal(56, entries.Where(entry => entry.Name.StartsWith("discovery.", StringComparison.Ordinal)).Sum(entry => entry.Limit));
        Assert.Equal(2, entries.Where(entry => entry.Name.StartsWith("role.", StringComparison.Ordinal)).Sum(entry => entry.Limit));
        Assert.Equal(41, entries.Where(entry => entry.Name.StartsWith("launcher.git.", StringComparison.Ordinal)).Sum(entry => entry.Limit));
        Assert.Equal(80, entries.Where(entry => entry.Name.StartsWith("git.", StringComparison.Ordinal)).Sum(entry => entry.Limit));
    }

    [Fact]
    public void Every_production_shape_is_admitted_to_exactly_one_class()
    {
        var fixture = Fixture();
        var admission = NewAdmission(fixture);
        ProcessShape[] shapes =
        [
            fixture.Probe(fixture.Git, "--version"), fixture.Probe(fixture.DotNet!, "--version"), fixture.Probe(fixture.Node, "--version"),
            fixture.Probe(fixture.GitHub!, "--version"), fixture.Probe(fixture.Docker!, "--version"),
            fixture.Probe(fixture.Claude, "--version"), fixture.Probe(fixture.Node, fixture.CodexScript!, "--version"),
            fixture.CodexRole(), fixture.ClaudeRole(),
            fixture.Launcher("init", "--initial-branch", "main"), fixture.Launcher("config", "core.autocrlf", "false"),
            fixture.Launcher("add", "--", "README.md", "AGENTS.md", "CLAUDE.md", "src/Feature.cs"),
            fixture.Launcher("-c", "user.name=Fixture Author", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "--no-verify", "-m", "Initial commit"),
            fixture.Launcher("rev-parse", "HEAD"), fixture.Launcher("rev-parse", "--symbolic-full-name", "HEAD"),
            fixture.Launcher("for-each-ref", "--format=%(refname) %(objectname)"), fixture.Launcher("-c", "core.quotepath=false", "ls-files", "--stage"),
            fixture.Host("rev-parse", "--git-dir"), fixture.Host("rev-parse", "--is-bare-repository"), fixture.Host("rev-parse", "--show-toplevel"),
            fixture.Host("rev-parse", "--git-common-dir"), fixture.Host("symbolic-ref", "-q", "--short", "HEAD"), fixture.Host("rev-parse", "HEAD"),
            fixture.Host("status", "--porcelain=v1"), fixture.Worktree(),
            fixture.Evidence("rev-parse", "HEAD"), fixture.Evidence("status", "--porcelain=v1", "-z", "--no-renames", "--untracked-files=all"),
            fixture.Evidence("diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--binary", "HEAD"),
            fixture.Evidence("hash-object", "--no-filters", "--stdin"),
            fixture.Evidence("--literal-pathspecs", "ls-files", "-z", "-v", "--", "AGENTS.md", "CLAUDE.md"),
            fixture.Evidence("--literal-pathspecs", "ls-files", "-z", "--others", "--ignored", "--exclude-standard", "--", "AGENTS.md", "CLAUDE.md"),
        ];

        foreach (var shape in shapes)
        {
            Assert.NotNull(admission.Admit(shape));
        }

        Assert.Equal(shapes.Length, admission.Facts.TotalAdmitted);
        Assert.Null(admission.Facts.ClosedBy);
    }

    [Fact]
    public void A_direct_codex_launch_is_admitted_as_its_exact_vector_and_a_script_launch_is_then_unlisted()
    {
        var fixture = Fixture(codexScript: false);
        var admission = NewAdmission(fixture);

        Assert.Equal("discovery.codex", admission.Admit(fixture.Probe(fixture.CodexExecutable, "--version")));
        Assert.Equal("role.codex.exec", admission.Admit(fixture.CodexRole()));
    }

    // QP-R3: the complete reviewed role argument vectors ------------------------------------------------------------------

    private static string[] Replace(string[] vector, string from, string to) =>
        [.. vector.Select(argument => argument == from ? to : argument)];

    public static TheoryData<string> CodexVectorsThatAreNotTheReviewedOne => new()
    {
        { "bypass" }, { "full-auto" }, { "sandbox" }, { "model" }, { "effort" }, { "tail" }, { "missingEphemeral" }, { "schemaLeaf" },
        { "resultOutsideRoot" }, { "cdOutsideRoot" }, { "cdIsNotWorkingDirectory" }, { "relativeSchema" },
    };

    [Theory]
    [MemberData(nameof(CodexVectorsThatAreNotTheReviewedOne))]
    public void A_codex_vector_that_is_not_exactly_the_reviewed_one_is_refused(string kind)
    {
        var fixture = Fixture();
        var admission = NewAdmission(fixture);
        var vector = fixture.CodexVector();
        var offset = 1;
        string[] mutated = kind switch
        {
            "bypass" => [.. vector.Take(offset + 1), "--dangerously-bypass-approvals-and-sandbox", .. vector.Skip(offset + 1)],
            "full-auto" => [.. vector.Take(offset + 1), "--full-auto", .. vector.Skip(offset + 1)],
            "sandbox" => Replace(vector, "read-only", "danger-full-access"),
            "model" => [.. vector.SkipLast(1), "--model", "gpt-x", "-"],
            "effort" => [.. vector.SkipLast(1), "--config", "model_reasoning_effort=high", "-"],
            "tail" => [.. vector, "extra"],
            "missingEphemeral" => [.. vector.Where(argument => argument != "--ephemeral")],
            "schemaLeaf" => Replace(vector, fixture.SchemaPath(), Path.Combine(Path.GetDirectoryName(fixture.SchemaPath())!, "other.json")),
            "resultOutsideRoot" => fixture.CodexVector(result: Path.Combine(fixture.Directory, "elsewhere", "final.json")),
            "cdOutsideRoot" => fixture.CodexVector(cd: Path.Combine(fixture.Directory, "elsewhere")),
            "cdIsNotWorkingDirectory" => fixture.CodexVector(cd: Path.Combine(fixture.Root, "workspaces", "other")),
            _ => Replace(vector, fixture.SchemaPath(), "schema.json"),
        };

        var refusal = Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(fixture.CodexRole(mutated)));

        Assert.Equal("Unlisted", refusal.Code);
        Assert.Equal(0, admission.Facts.TotalAdmitted);
    }

    public static TheoryData<string> ClaudeVectorsThatAreNotTheReviewedOne => new()
    {
        { "toolsBash" }, { "maxTurns" }, { "maxTurnsAndTools" }, { "permissionMode" }, { "sessionPersistence" }, { "schema" },
        { "sessionId" }, { "tail" }, { "model" }, { "shorter" },
    };

    [Theory]
    [MemberData(nameof(ClaudeVectorsThatAreNotTheReviewedOne))]
    public void A_claude_vector_that_is_not_exactly_the_reviewed_one_is_refused(string kind)
    {
        var fixture = Fixture();
        var admission = NewAdmission(fixture);
        var vector = ApprovedFixture.ClaudeVector();
        string[] mutated = kind switch
        {
            "toolsBash" => [.. vector.Select(argument => argument == string.Empty ? "Bash" : argument)],
            "maxTurns" => Replace(vector, "1", "99"),
            "maxTurnsAndTools" => ["--print", "--tools", "Bash", "--max-turns", "99"],
            "permissionMode" => Replace(vector, "plan", "bypassPermissions"),
            "sessionPersistence" => [.. vector.Where(argument => argument != "--no-session-persistence")],
            "schema" => Replace(vector, ApprovedFixture.ClaudeSchema(), "{}"),
            "sessionId" => ApprovedFixture.ClaudeVector("not-a-guid"),
            "tail" => [.. vector, "--allowed-tools", "Bash"],
            "model" => [.. vector, "--model", "x"],
            _ => [.. vector.Take(vector.Length - 2)],
        };

        var refusal = Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(fixture.ClaudeRole(mutated)));

        Assert.Equal("Unlisted", refusal.Code);
        Assert.Equal(0, admission.Facts.TotalAdmitted);
    }

    [Fact]
    public void The_reviewers_two_counterexample_vectors_are_refused()
    {
        var fixture = Fixture();
        var bypass = fixture.CodexRole(["exec", "--dangerously-bypass-approvals-and-sandbox", "unreviewed-prompt"]);
        var codexScript = fixture.CodexRole([fixture.CodexScript!, "exec", "--dangerously-bypass-approvals-and-sandbox", "unreviewed-prompt"]);
        var claude = fixture.ClaudeRole(["--print", "--tools", "Bash", "--max-turns", "99"]);

        foreach (var shape in new[] { bypass, codexScript, claude })
        {
            var admission = NewAdmission(fixture);
            Assert.Equal("Unlisted", Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(shape)).Code);
        }
    }

    // QP-R3: environments, deadlines and destinations ---------------------------------------------------------------------

    [Fact]
    public void A_role_with_an_unreviewed_environment_name_deadline_or_destination_is_refused()
    {
        var fixture = Fixture();
        ProcessShape[] shapes =
        [
            fixture.CodexRole() with { EnvironmentNames = ["USERPROFILE", "PATH"] },
            fixture.CodexRole() with { EnvironmentNames = null },
            fixture.CodexRole() with { Timeout = TimeSpan.FromMinutes(11) },
            fixture.CodexRole() with { Timeout = Timeout.InfiniteTimeSpan },
            fixture.CodexRole() with { Timeout = null },
            fixture.CodexRole() with { StandardOutputSinkPath = Path.Combine(fixture.Directory, "elsewhere", "out") },
            fixture.CodexRole() with { StandardErrorSinkPath = null },
            fixture.CodexRole() with { HasStandardInput = false },
            fixture.CodexRole() with { ApprovedRoot = fixture.Root },
            fixture.ClaudeRole() with { EnvironmentNames = ["USERPROFILE", "ANTHROPIC_API_KEY"] },
            fixture.ClaudeRole() with { Timeout = TimeSpan.FromHours(2) },
            fixture.ClaudeRole() with { WorkingDirectory = fixture.Directory, ApprovedRoot = fixture.Directory },
            fixture.ClaudeRole() with { StandardOutputSinkPath = Path.Combine(fixture.Root, "..", "out") },
            fixture.ClaudeRole() with { HasStandardInput = null },
        ];

        foreach (var shape in shapes)
        {
            var admission = NewAdmission(fixture);
            Assert.Equal("Unlisted", Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(shape)).Code);
            Assert.Equal(0, admission.Facts.TotalAdmitted);
        }
    }

    [Fact]
    public void A_host_git_command_with_an_unreviewed_environment_deadline_or_destination_is_refused()
    {
        var fixture = Fixture();
        ProcessShape[] shapes =
        [
            fixture.Host("rev-parse", "HEAD") with { EnvironmentNames = ["GIT_TERMINAL_PROMPT", "GIT_EXEC_PATH"] },
            fixture.Host("rev-parse", "HEAD") with { EnvironmentNames = null },
            fixture.Host("rev-parse", "HEAD") with { Timeout = TimeSpan.FromMinutes(5) },
            fixture.Host("rev-parse", "HEAD") with { WorkingDirectory = fixture.Directory },
            fixture.Host("rev-parse", "HEAD") with { ApprovedRoot = null },
            fixture.Host("worktree", "add", "-b", "devalcopilot/workspace/a/1", Path.Combine(fixture.Directory, "elsewhere"), new string('a', 40)),
            fixture.Host("worktree", "add", "-b", "-x", Path.Combine(fixture.Root, "w"), new string('a', 40)),
            fixture.Host("worktree", "add", "-b", "branch", Path.Combine(fixture.Root, "w"), "HEAD"),
        ];

        foreach (var shape in shapes)
        {
            var admission = NewAdmission(fixture);
            Assert.Equal("Unlisted", Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(shape)).Code);
        }
    }

    // QP-R3: discovery is bound to approved launch tuples, not to names -------------------------------------------------------

    [Fact]
    public void A_probe_by_leaf_name_or_package_substring_of_a_file_that_is_not_the_approved_tuple_is_refused()
    {
        var fixture = Fixture();
        var other = Path.Combine(fixture.Directory, "other");
        Directory.CreateDirectory(other);
        ProcessShape[] shapes =
        [
            fixture.Probe(Path.Combine(other, "git.exe"), "--version"),
            fixture.Probe(Path.Combine(other, "claude.exe"), "--version"),
            fixture.Probe(Path.Combine(other, "node.exe"), Path.Combine(other, "node_modules", "@openai", "codex", "bin", "codex.js"), "--version"),
            fixture.Probe(fixture.Node, Path.Combine(other, "node_modules", "@openai", "codex", "bin", "codex.js"), "--version"),
            fixture.Probe(fixture.Node, Path.Combine(other, "@anthropic-ai", "claude-code", "cli.js"), "--version"),
            fixture.Probe(fixture.Git, "--version", "--build-options"),
            fixture.Probe(fixture.Claude, "--version") with { EnvironmentNames = ["ANTHROPIC_API_KEY"] },
            fixture.Probe(fixture.Claude, "--version") with { Timeout = TimeSpan.FromMinutes(1) },
        ];

        foreach (var shape in shapes)
        {
            var admission = NewAdmission(fixture);
            Assert.Equal("Unlisted", Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(shape)).Code);
        }
    }

    [Fact]
    public void A_capability_the_owner_did_not_approve_is_never_probed_even_by_its_catalog_name()
    {
        var fixture = Fixture(withOptional: false);
        var admission = NewAdmission(fixture);
        var gh = Path.Combine(fixture.Directory, "tools", "gh.exe");
        File.WriteAllText(gh, "present on the machine but never approved");

        Assert.Equal("Unlisted", Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(fixture.Probe(gh, "--version"))).Code);
    }

    [Fact]
    public void The_approved_set_requires_each_provider_explicit_node_components_and_exact_files()
    {
        var fixture = Fixture();
        ApprovedLaunch Launch(string name) => fixture.Set.Find(name)!;

        Assert.Equal("ApprovedProviderMissing", ApprovedLaunchSet.TryCreate([Launch("git"), Launch("claude")]).Refusal);
        Assert.Equal("ApprovedNodeComponentMissing", ApprovedLaunchSet.TryCreate([Launch("git"), Launch("codex"), Launch("claude")]).Refusal);
        Assert.Equal("ApprovedCapabilityDuplicated", ApprovedLaunchSet.TryCreate([Launch("git"), Launch("git")]).Refusal);
        Assert.Equal("ApprovedCapabilityUnknown", ApprovedLaunchSet.TryCreate([new("python", Launch("git").Executable, null)]).Refusal);
        Assert.Equal("ApprovedScriptNotPermitted", ApprovedLaunchSet.TryCreate([new("git", Launch("git").Executable, Launch("codex").Script)]).Refusal);
        Assert.Equal("ApprovedFileNotExact", ApprovedLaunchSet.TryCreate([new("git", new ApprovedFile("git.exe", Launch("git").Executable.Sha256), null)]).Refusal);
        Assert.Equal("ApprovedFileNotExact", ApprovedLaunchSet.TryCreate([new("git", new ApprovedFile(Launch("git").Executable.Path, "abc"), null)]).Refusal);
        var differentNode = new ApprovedLaunch("node", new ApprovedFile(Launch("node").Executable.Path, new string('0', 64)), null);
        Assert.Equal(
            "ApprovedNodeComponentDisagrees",
            ApprovedLaunchSet.TryCreate([Launch("git"), differentNode, Launch("codex"), Launch("claude")]).Refusal);
    }

    // QP-R3: executable identity, launcher Git and drift ---------------------------------------------------------------------

    [Fact]
    public void The_launcher_git_is_the_approved_absolute_file_and_a_path_searched_name_is_refused()
    {
        var fixture = Fixture();
        var admission = NewAdmission(fixture);

        Assert.Equal("launcher.git.head", admission.Admit(fixture.Launcher("rev-parse", "HEAD")));
        Assert.Equal(fixture.Git, admission.LauncherGitPath);
        var searched = fixture.Launcher("rev-parse", "HEAD") with { ExecutablePath = "git" };

        Assert.Equal("Unlisted", Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(searched)).Code);
    }

    [Fact]
    public void The_launcher_and_the_host_share_one_git_identity_and_another_git_file_is_refused_for_both()
    {
        var fixture = Fixture();
        var elsewhere = Path.Combine(fixture.Directory, "other-git.exe");
        File.WriteAllText(elsewhere, "another git");
        var launcher = NewAdmission(fixture);
        var host = NewAdmission(fixture);

        Assert.Throws<ProcessAdmissionRefusedException>(() => launcher.Admit(fixture.Launcher("rev-parse", "HEAD") with { ExecutablePath = elsewhere }));
        Assert.Throws<ProcessAdmissionRefusedException>(() => host.Admit(fixture.Host("rev-parse", "HEAD") with { ExecutablePath = elsewhere }));
    }

    [Fact]
    public void A_replaced_approved_file_is_refused_before_dispatch_with_a_closed_drift_code_and_closes_the_gate()
    {
        var fixture = Fixture();
        var admission = NewAdmission(fixture);
        Assert.Equal("launcher.git.head", admission.Admit(fixture.Launcher("rev-parse", "HEAD")));
        ApprovedFixture.Tamper(fixture.Git);

        var refusal = Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(fixture.Launcher("rev-parse", "HEAD")));

        Assert.Equal("FileIdentityDrift", refusal.Code);
        Assert.Equal(1, admission.Facts.Admitted["launcher.git.head"]);
        Assert.Equal("FileIdentityDrift", admission.Facts.ClosedBy);
    }

    [Theory]
    [InlineData("script")]
    [InlineData("node")]
    [InlineData("claude")]
    public void Every_file_of_a_role_launch_is_verified_again_before_its_start(string target)
    {
        var fixture = Fixture();
        var admission = NewAdmission(fixture);
        var (path, shape) = target switch
        {
            "script" => (fixture.CodexScript!, fixture.CodexRole()),
            "node" => (fixture.Node, fixture.CodexRole()),
            _ => (fixture.Claude, fixture.ClaudeRole()),
        };
        ApprovedFixture.Tamper(path);

        Assert.Equal("FileIdentityDrift", Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(shape)).Code);
        Assert.Equal(0, admission.Facts.TotalAdmitted);
        Assert.False(Directory.Exists(admission.Ledger.LedgerDirectory));
    }

    [Fact]
    public void A_removed_approved_file_is_refused_as_unreadable_not_launched()
    {
        var fixture = Fixture();
        var admission = NewAdmission(fixture);
        File.Delete(fixture.Claude);

        Assert.Equal("FileIdentityNotARealFile", Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(fixture.ClaudeRole())).Code);
    }

    [Fact]
    public void Host_readiness_naming_anything_but_the_approved_tuples_leaves_every_host_git_and_role_shape_unlisted()
    {
        var fixture = Fixture();
        var other = Path.Combine(fixture.Directory, "other");
        Directory.CreateDirectory(other);
        var wrongClaude = fixture.ClaudeFact with { ExecutablePath = Path.Combine(other, "claude.exe") };
        var wrongScript = fixture.CodexFact with { ScriptPath = Path.Combine(other, "codex.js") };
        var wrongKind = fixture.CodexFact with { LaunchKind = DevalCopilot.Domain.Features.EnvironmentReadiness.CapabilityLaunchKind.DirectExecutable };
        var wrongGit = fixture.GitFact with { ExecutablePath = Path.Combine(other, "git.exe") };

        foreach (var (git, codex, claude) in new[]
        {
            (fixture.GitFact, fixture.CodexFact, wrongClaude),
            (fixture.GitFact, wrongScript, fixture.ClaudeFact),
            (fixture.GitFact, wrongKind, fixture.ClaudeFact),
            (wrongGit, fixture.CodexFact, fixture.ClaudeFact),
        })
        {
            var judged = new JudgedTargets(fixture.Set);
            judged.Destinations.Pin(fixture.Root);

            Assert.False(judged.Pin(git, codex, claude));
            Assert.False(judged.IsPinned);
            Assert.Null(judged.CodexRoleOffset(fixture.CodexRole()));
            Assert.False(judged.IsHostGit(fixture.Host("rev-parse", "HEAD")));
        }
    }

    [Fact]
    public void Nothing_is_launchable_before_an_approved_set_exists_and_the_set_is_approved_only_once()
    {
        var fixture = Fixture();
        var judged = new JudgedTargets();
        var (entries, _) = Profile01Inventory.Create(judged);
        var ledger = new AdmissionLedger(Path.Combine(LedgerDirectory, "inventory"), "owner-test-authorization", TimeProvider.System);
        var admission = new ProcessAdmission(entries, 179, ledger, judged.Revalidate);

        Assert.Equal("Unlisted", Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(fixture.Launcher("rev-parse", "HEAD"))).Code);
        Assert.True(judged.Approve(fixture.Set));
        Assert.False(judged.Approve(fixture.Set));
    }

    // Existing production-shape refusals, now over whole shapes ------------------------------------------------------------

    public static TheoryData<string, string[]> Unlisted => new()
    {
        { "codex app-server", ["app-server", "--stdio"] },
        { "claude version beyond probe", ["--version", "--help"] },
        { "claude auth status", ["auth", "status"] },
        { "claude auth login", ["auth", "login"] },
        { "claude help", ["--help"] },
        { "codex login status", ["login", "status"] },
        { "codex model catalog", ["debug", "models"] },
    };

    [Theory]
    [MemberData(nameof(Unlisted))]
    public void A_provider_command_that_is_not_a_listed_role_or_probe_is_refused_before_a_process(string label, string[] arguments)
    {
        Assert.False(string.IsNullOrEmpty(label));
        var fixture = Fixture();
        var admission = NewAdmission(fixture);

        var refusal = Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(new ProcessShape(fixture.Claude, arguments)));

        Assert.Equal("Unlisted", refusal.Code);
        Assert.Equal(1, admission.Facts.TotalRefused);
        Assert.Equal(0, admission.Facts.TotalAdmitted);
        Assert.False(Directory.Exists(admission.Ledger.LedgerDirectory));
    }

    [Fact]
    public void A_codex_app_server_launch_through_the_node_script_is_refused()
    {
        var fixture = Fixture();
        var admission = NewAdmission(fixture);

        Assert.Equal(
            "Unlisted",
            Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(new ProcessShape(fixture.Node, [fixture.CodexScript!, "app-server", "--stdio"]))).Code);
    }

    public static TheoryData<string[]> RemoteOrOtherGit => new()
    {
        { ["fetch", "origin"] },
        { ["push", "origin", "main"] },
        { ["remote", "-v"] },
        { ["ls-remote", "origin"] },
        { ["config", "--get", "remote.origin.url"] },
        { ["credential", "fill"] },
        { ["gc"] },
    };

    [Theory]
    [MemberData(nameof(RemoteOrOtherGit))]
    public void A_remote_or_unlisted_git_command_is_refused_for_the_launcher_and_for_the_host(string[] subcommand)
    {
        var fixture = Fixture();

        foreach (var shape in new[] { fixture.Launcher(subcommand), fixture.Host(subcommand), fixture.Evidence(subcommand) })
        {
            var admission = NewAdmission(fixture);
            Assert.Equal("Unlisted", Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(shape)).Code);
        }
    }

    [Fact]
    public void A_host_git_command_with_a_changed_safety_prefix_or_an_unlisted_pathspec_is_refused()
    {
        var fixture = Fixture();
        ProcessShape[] shapes =
        [
            new(fixture.Git, ["rev-parse", "HEAD"]),
            fixture.Host("rev-parse", "HEAD") with { Arguments = [.. ApprovedFixture.HostPrefix.Take(4), "rev-parse", "HEAD"] },
            fixture.Evidence("--literal-pathspecs", "ls-files", "-z", "-v", "--", "AGENTS.md", "secrets.txt"),
            fixture.Evidence("--literal-pathspecs", "ls-files", "-z", "-v", "--"),
            fixture.Host("worktree", "add", "-b", "x", "p"),
        ];

        foreach (var shape in shapes)
        {
            var admission = NewAdmission(fixture);
            Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(shape));
            Assert.Equal(0, admission.Facts.TotalAdmitted);
        }
    }

    [Fact]
    public void A_git_or_role_request_before_the_judged_targets_are_pinned_is_unlisted()
    {
        var fixture = Fixture();
        var admission = NewAdmission(fixture, pin: false);

        Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(fixture.Host("rev-parse", "HEAD")));
        Assert.Equal(0, admission.Facts.TotalAdmitted);
        foreach (var shape in new[] { fixture.ClaudeRole(), fixture.CodexRole() })
        {
            var again = NewAdmission(fixture, pin: false);
            Assert.Throws<ProcessAdmissionRefusedException>(() => again.Admit(shape));
        }
    }

    [Fact]
    public void A_ninth_periodic_discovery_probe_of_one_capability_is_refused_by_its_ceiling()
    {
        var fixture = Fixture();
        var admission = NewAdmission(fixture);
        var probe = fixture.Probe(fixture.Claude, "--version");

        for (var index = 0; index < Profile01Inventory.DiscoveryPerCapability; index++)
        {
            Assert.Equal("discovery.claude", admission.Admit(probe));
        }

        var refusal = Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(probe));

        Assert.Equal("ClassLimit.discovery.claude", refusal.Code);
        Assert.Equal(8, admission.Facts.Admitted["discovery.claude"]);
        Assert.Equal(1, admission.Facts.Refused["ClassLimit.discovery.claude"]);
        Assert.Equal("ClassLimit.discovery.claude", admission.Facts.ClosedBy);
    }

    [Fact]
    public void A_second_role_invocation_of_either_provider_is_refused_with_no_retry_allowance()
    {
        var fixture = Fixture();
        var claudeAdmission = NewAdmission(fixture);
        Assert.Equal("role.claude.print", claudeAdmission.Admit(fixture.ClaudeRole()));
        Assert.Equal("ClassLimit.role.claude.print", Assert.Throws<ProcessAdmissionRefusedException>(() => claudeAdmission.Admit(fixture.ClaudeRole())).Code);

        var codexAdmission = fixture.Admission(Path.Combine(LedgerDirectory, "inventory-codex"));
        Assert.Equal("role.codex.exec", codexAdmission.Admit(fixture.CodexRole()));
        Assert.Equal("ClassLimit.role.codex.exec", Assert.Throws<ProcessAdmissionRefusedException>(() => codexAdmission.Admit(fixture.CodexRole())).Code);
    }

    [Fact]
    public void The_authorization_total_refuses_any_start_beyond_it_even_for_a_listed_class()
    {
        var fixture = Fixture();
        var admission = NewAdmission(fixture, totalLimit: 3);
        var probe = fixture.Probe(fixture.Git, "--version");
        Assert.Equal("discovery.git", admission.Admit(probe));
        Assert.Equal("discovery.git", admission.Admit(probe));
        Assert.Equal("discovery.git", admission.Admit(probe));

        Assert.Equal("TotalLimit", Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(probe)).Code);
    }

    [Fact]
    public void A_refusal_code_never_carries_a_path_an_argument_or_text_of_the_request()
    {
        var fixture = Fixture();
        var admission = NewAdmission(fixture);

        var refusal = Assert.Throws<ProcessAdmissionRefusedException>(
            () => admission.Admit(new ProcessShape(@"C:\Users\someone\secret.exe", ["--password", "hunter2"])));

        Assert.Equal("Unlisted", refusal.Code);
        Assert.DoesNotContain("someone", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", string.Join(',', admission.Facts.Refused.Keys), StringComparison.Ordinal);
    }
}
