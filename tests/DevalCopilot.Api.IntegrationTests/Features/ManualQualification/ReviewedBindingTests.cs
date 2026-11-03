using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>QP-R3 (correction 2): the reviewed ten-minute role limit, the schema bound to this run and attempt by location and exact
/// contract bytes, the exact production scratch boundary, attempt-bound artifact sinks, and the launcher Git's constrained
/// environment. Owned stand-in files only; the production scratch layout is mirrored under a test directory, never the real one.</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ReviewedBindingTests : QualificationTestBase
{
    private ApprovedFixture Fixture(bool codexScript = false) => new(Path.Combine(LedgerDirectory, "approved"), codexScript);

    private ProcessAdmission Admission(ApprovedFixture fixture) => fixture.Admission(Path.Combine(LedgerDirectory, "counts"));

    private static void AssertUnlisted(ProcessAdmission admission, ProcessShape shape)
    {
        Assert.Equal("Unlisted", Assert.Throws<ProcessAdmissionRefusedException>(() => admission.Admit(shape)).Code);
        Assert.Equal(0, admission.Facts.TotalAdmitted);
    }

    // Ten-minute roles -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Both_roles_are_admitted_at_exactly_the_reviewed_ten_minutes_and_refused_one_tick_beyond()
    {
        var fixture = Fixture();

        Assert.Equal("role.codex.exec", Admission(fixture).Admit(fixture.CodexRole()));
        Assert.Equal("role.claude.print", Admission(fixture).Admit(fixture.ClaudeRole()));
        foreach (var beyond in new[] { TimeSpan.FromMinutes(10) + TimeSpan.FromTicks(1), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(20) })
        {
            AssertUnlisted(Admission(fixture), fixture.CodexRole() with { Timeout = beyond });
            AssertUnlisted(Admission(fixture), fixture.ClaudeRole() with { Timeout = beyond });
        }
    }

    [Fact]
    public void The_reviewers_fifteen_minute_role_request_is_refused()
    {
        var fixture = Fixture();

        AssertUnlisted(Admission(fixture), fixture.CodexRole() with { Timeout = TimeSpan.FromMinutes(15) });
    }

    // Schema bound to this run and attempt by location and exact bytes -----------------------------------------------------------

    [Fact]
    public void The_schema_of_this_run_and_attempt_with_the_exact_contract_bytes_is_admitted()
    {
        var fixture = Fixture();

        Assert.Equal("role.codex.exec", Admission(fixture).Admit(fixture.CodexRole()));
    }

    [Fact]
    public void An_arbitrary_absolute_file_named_schema_json_never_qualifies()
    {
        var fixture = Fixture();
        var arbitrary = Path.Combine(LedgerDirectory, "unapproved", "schema.json");
        Directory.CreateDirectory(Path.GetDirectoryName(arbitrary)!);
        File.WriteAllText(arbitrary, File.ReadAllText(fixture.WriteSchema()));

        AssertUnlisted(Admission(fixture), fixture.CodexRole(fixture.CodexVector(schema: arbitrary)));
    }

    [Fact]
    public void A_schema_in_the_production_scratch_of_another_run_or_attempt_is_refused()
    {
        var fixture = Fixture();
        var otherAttempt = fixture.WriteSchema(attempt: Guid.NewGuid());
        var otherRun = Path.Combine(fixture.LocalAppData, "DevalCopilot", "agent-scratch", Guid.NewGuid().ToString(), fixture.AttemptId.ToString(), "schema.json");
        Directory.CreateDirectory(Path.GetDirectoryName(otherRun)!);
        File.WriteAllText(otherRun, File.ReadAllText(otherAttempt));

        AssertUnlisted(Admission(fixture), fixture.CodexRole(fixture.CodexVector(schema: otherAttempt)));
        AssertUnlisted(Admission(fixture), fixture.CodexRole(fixture.CodexVector(schema: otherRun)));
    }

    [Fact]
    public void A_schema_whose_bytes_are_not_exactly_the_production_contract_is_refused()
    {
        var fixture = Fixture();
        var exact = File.ReadAllText(fixture.WriteSchema());
        string[] variants =
        [
            exact + " ", exact + "\n", ((char)0xFEFF).ToString() + exact, exact.Replace("\"type\"", "\"typ\"", StringComparison.Ordinal),
            "{}", string.Empty, new string('x', 70 * 1024),
        ];

        foreach (var text in variants)
        {
            fixture.WriteSchema(text);
            AssertUnlisted(Admission(fixture), fixture.CodexRole(fixture.CodexVector(schema: fixture.SchemaPath())));
        }
    }

    [Fact]
    public void A_missing_schema_file_is_refused_and_a_replaced_one_is_read_again_at_every_admission()
    {
        var fixture = Fixture();
        var vector = fixture.CodexVector();
        File.Delete(fixture.SchemaPath());
        AssertUnlisted(Admission(fixture), fixture.CodexRole(vector));

        fixture.WriteSchema();
        var admission = Admission(fixture);
        Assert.Equal("role.codex.exec", admission.Admit(fixture.CodexRole(vector)));
    }

    [Fact]
    public void The_result_and_both_sinks_must_be_the_files_of_the_one_attempt_of_this_run()
    {
        var fixture = Fixture();
        var other = Guid.NewGuid();
        ProcessShape[] shapes =
        [
            fixture.CodexRole() with { StandardOutputSinkPath = fixture.ArtifactFile("stdout", attempt: other) },
            fixture.CodexRole() with { StandardErrorSinkPath = fixture.ArtifactFile("stderr", run: Guid.NewGuid()) },
            fixture.CodexRole() with { StandardOutputSinkPath = fixture.ArtifactFile("stderr") },
            fixture.CodexRole() with { StandardOutputSinkPath = Path.Combine(fixture.Root, "artifacts", "out.partial") },
            fixture.CodexRole(fixture.CodexVector(result: fixture.ArtifactFile("final-response", attempt: other))),
            fixture.CodexRole(fixture.CodexVector(result: fixture.ArtifactFile("final-response", run: Guid.NewGuid()))),
            fixture.CodexRole(fixture.CodexVector(result: fixture.ArtifactFile("stdout"))),
            fixture.ClaudeRole() with { StandardOutputSinkPath = fixture.ArtifactFile("stdout", attempt: other) },
            fixture.ClaudeRole() with { StandardErrorSinkPath = fixture.ArtifactFile("stderr", run: Guid.NewGuid()) },
            fixture.ClaudeRole() with { StandardErrorSinkPath = fixture.ArtifactFile("stdout") },
            fixture.ClaudeRole() with { StandardOutputSinkPath = null },
        ];

        foreach (var shape in shapes)
        {
            AssertUnlisted(Admission(fixture), shape);
        }
    }

    [Fact]
    public void The_workspace_must_be_inside_the_owned_workspaces_directory_not_merely_the_owned_root()
    {
        var fixture = Fixture();
        var elsewhere = Path.Combine(fixture.Root, "repos", "source");

        AssertUnlisted(Admission(fixture), fixture.CodexRole(fixture.CodexVector(cd: elsewhere)) with { WorkingDirectory = elsewhere, ApprovedRoot = elsewhere });
        AssertUnlisted(Admission(fixture), fixture.ClaudeRole() with { WorkingDirectory = elsewhere, ApprovedRoot = elsewhere });
    }

    [Fact]
    public void A_run_that_was_never_pinned_binds_nothing_so_every_attempt_bound_class_matches_nothing()
    {
        var fixture = Fixture();
        var judged = new JudgedTargets(fixture.Set, new OwnedDestinations(fixture.LocalAppData));
        judged.Destinations.Pin(fixture.Root);
        Assert.True(judged.Pin(fixture.GitFact, fixture.CodexFact, fixture.ClaudeFact));
        var (entries, total) = Profile01Inventory.Create(judged);
        ProcessAdmission Fresh() => new(
            entries, total, new AdmissionLedger(Path.Combine(LedgerDirectory, "counts"), "owner-test-authorization", TimeProvider.System), judged.Revalidate);

        AssertUnlisted(Fresh(), fixture.CodexRole());
        AssertUnlisted(Fresh(), fixture.ClaudeRole());
    }

    // The exact production scratch boundary -------------------------------------------------------------------------------------

    [Fact]
    public void Discovery_probes_run_only_in_the_exact_tool_discovery_scratch_directory()
    {
        var fixture = Fixture();
        Assert.Equal("discovery.git", Admission(fixture).Admit(fixture.Probe(fixture.Git, "--version")));
        var other = Path.Combine(fixture.LocalAppData, "DevalCopilot", "tool-discovery-scratch-other");
        ProcessShape[] shapes =
        [
            fixture.Probe(fixture.Git, "--version") with { WorkingDirectory = other, ApprovedRoot = other },
            fixture.Probe(fixture.Git, "--version") with { WorkingDirectory = fixture.Directory, ApprovedRoot = fixture.Directory },
            fixture.Probe(fixture.Git, "--version") with { ApprovedRoot = fixture.Directory },
            fixture.Probe(fixture.Git, "--version") with { WorkingDirectory = Path.Combine(fixture.DiscoveryScratch, "child") },
            fixture.Probe(fixture.Git, "--version") with { WorkingDirectory = null },
            fixture.Probe(fixture.Git, "--version") with { WorkingDirectory = fixture.Root, ApprovedRoot = fixture.Root },
        ];

        foreach (var shape in shapes)
        {
            AssertUnlisted(Admission(fixture), shape);
        }
    }

    [Fact]
    public void The_scratch_locations_mirror_the_production_names_under_the_local_application_data_directory()
    {
        var real = new OwnedDestinations();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(Path.Combine(local, "DevalCopilot", "tool-discovery-scratch"), real.DiscoveryScratch);
    }

    // The launcher's own Git: constrained effective environment and configuration --------------------------------------------

    [Fact]
    public void A_launcher_git_shape_must_carry_the_pinned_configuration_variables_and_nothing_else()
    {
        var fixture = Fixture();
        var names = GitProcess.ChildEnvironment().Keys.ToArray();
        Assert.Equal("launcher.git.head", Admission(fixture).Admit(fixture.Launcher("rev-parse", "HEAD")));
        ProcessShape[] shapes =
        [
            fixture.Launcher("rev-parse", "HEAD") with { EnvironmentNames = null },
            fixture.Launcher("rev-parse", "HEAD") with { EnvironmentNames = [.. names.Where(name => name != "GIT_CONFIG_NOSYSTEM")] },
            fixture.Launcher("rev-parse", "HEAD") with { EnvironmentNames = [.. names.Where(name => name != "GIT_CONFIG_COUNT")] },
            fixture.Launcher("rev-parse", "HEAD") with { EnvironmentNames = [.. names, "GIT_EXEC_PATH"] },
            fixture.Launcher("rev-parse", "HEAD") with { EnvironmentNames = [.. names, "PATH"] },
            fixture.Launcher("rev-parse", "HEAD") with { EnvironmentNames = [.. names, "GIT_ASKPASS"] },
            fixture.Launcher("rev-parse", "HEAD") with { EnvironmentNames = [.. names, "HOME"] },
        ];

        foreach (var shape in shapes)
        {
            AssertUnlisted(Admission(fixture), shape);
        }
    }

    [Fact]
    public void The_launcher_git_child_starts_from_an_empty_environment_so_no_ambient_variable_reaches_it()
    {
        var ambient = new Dictionary<string, string>
        {
            ["GIT_DIR"] = Path.Combine(LedgerDirectory, "ambient-git-dir"),
            ["GIT_ASKPASS"] = "ambient-askpass",
            ["GIT_EXEC_PATH"] = Path.Combine(LedgerDirectory, "ambient-exec"),
            ["GIT_SSH_COMMAND"] = "ambient-ssh",
            ["GIT_CONFIG_PARAMETERS"] = "'core.hooksPath=ambient'",
            ["GIT_WORK_TREE"] = Path.Combine(LedgerDirectory, "ambient-tree"),
            ["HOME"] = LedgerDirectory,
            ["PATH"] = LedgerDirectory,
        };
        var previous = ambient.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var (name, value) in ambient)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            var environment = GitProcess.ChildEnvironment();

            Assert.All(environment.Keys, name => Assert.Contains(name, GitProcess.EnvironmentNames, StringComparer.OrdinalIgnoreCase));
            Assert.DoesNotContain(environment.Keys, name => ambient.ContainsKey(name));
            Assert.Equal("NUL", environment["GIT_CONFIG_GLOBAL"]);
            Assert.Equal("1", environment["GIT_CONFIG_NOSYSTEM"]);
            Assert.Equal("0", environment["GIT_OPTIONAL_LOCKS"]);
            Assert.Equal("0", environment["GIT_TERMINAL_PROMPT"]);
            var configured = Enumerable.Range(0, int.Parse(environment["GIT_CONFIG_COUNT"], System.Globalization.CultureInfo.InvariantCulture))
                .ToDictionary(index => environment[$"GIT_CONFIG_KEY_{index}"], index => environment[$"GIT_CONFIG_VALUE_{index}"]);
            Assert.Equal("NUL", configured["core.hooksPath"]);
            Assert.Equal("false", configured["core.fsmonitor"]);
            Assert.Equal("never", configured["protocol.allow"]);
        }
        finally
        {
            foreach (var (name, value) in previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    [Fact]
    public void An_ambient_git_directory_and_a_repository_hook_cannot_change_what_the_launcher_git_does()
    {
        // Git writes read-only object files, so this scene lives in its own directory that is cleared attribute by attribute.
        var scene = Path.Combine(Path.GetTempPath(), "devalcopilot-xunit-git-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(scene, "fixture-repo");
        Directory.CreateDirectory(repository);
        var ambientDirectory = Path.Combine(scene, "ambient-git-dir");
        var marker = Path.Combine(scene, "hook-ran.txt");
        var previous = Environment.GetEnvironmentVariable("GIT_DIR");
        try
        {
            Environment.SetEnvironmentVariable("GIT_DIR", ambientDirectory);
            GitProcess.Run(repository, "init", "--initial-branch", "main");
            Assert.True(Directory.Exists(Path.Combine(repository, ".git")), "the repository was created elsewhere than the working directory");
            Assert.False(Directory.Exists(ambientDirectory));

            File.WriteAllText(Path.Combine(repository, "a.txt"), "a\n");
            var hook = Path.Combine(repository, ".git", "hooks", "post-commit");
            Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
            File.WriteAllText(hook, "#!/bin/sh\necho ran > '" + marker.Replace('\\', '/') + "'\n");
            GitProcess.Run(repository, "add", "--", "a.txt");
            GitProcess.Run(repository, "-c", "user.name=Fixture Author", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "-m", "first");

            Assert.False(File.Exists(marker), "a repository hook ran under the launcher Git");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_DIR", previous);
            if (Directory.Exists(scene))
            {
                foreach (var file in Directory.EnumerateFiles(scene, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(scene, recursive: true);
            }
        }
    }
}
