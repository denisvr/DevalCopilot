using System.Text.Json;
using System.Text.RegularExpressions;
using DevalCopilot.Application.Features.Runs;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The complete, closed inventory of every process one read-only qualification exchange (lot B) may start, derived from the unchanged
/// production composition by an offline rehearsal and pinned here. Each class is an exact request shape with its own ceiling; the
/// authorization total is their sum. A shape is the whole request: the approved executable (and script), the complete argument vector
/// with only the reviewed dynamic members constrained (a schema or result path, the workspace, a session identity, a branch, a commit
/// or a pathspec), the environment variable names, the deadline, and every working directory, sink and destination, which must lie in
/// the session's one owned root. Anything else, including a Codex <c>app-server</c>, a help, authentication or account command, a
/// remote Git command, a Git command of another shape, a role argument vector other than the reviewed one, or a launch of a file the
/// owner did not approve, is unlisted and refused before its process exists. The ceilings are the counts the complete production flow
/// issues, so an extra command, a retry or a changed discovery behavior stops the session rather than receiving an implicit allowance.
/// </summary>
public static partial class Profile01Inventory
{
    /// <summary>The most version probes per approved capability: the first probe plus one refresh every five minutes inside the
    /// 35-minute outer envelope.</summary>
    public const int DiscoveryPerCapability = 8;

    private static readonly TimeSpan DiscoveryDeadline = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GitDeadline = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LauncherGitDeadline = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RoleDeadline = TimeSpan.FromMinutes(10);

    private static readonly string[] DiscoveryEnvironment = ["SystemRoot"];
    private static readonly string[] CodexEnvironment = ["USERPROFILE", "CODEX_HOME", "SystemRoot"];
    private static readonly string[] ClaudeEnvironment = ["USERPROFILE", "CLAUDE_CONFIG_DIR", "SystemRoot"];
    private static readonly string[] GitEnvironment = ["GIT_TERMINAL_PROMPT", "GIT_OPTIONAL_LOCKS", "GIT_CONFIG_NOSYSTEM"];

    private static readonly string[] HostGitPrefix =
    [
        "--no-pager", "-c", "core.fsmonitor=false", "-c", "core.untrackedCache=false", "-c", "protocol.allow=never",
    ];

    private static readonly string[] AttributesOff = ["-c", "core.attributesfile=NUL"];
    private static readonly string[] InstructionFiles = ["AGENTS.md", "CLAUDE.md"];

    public static (IReadOnlyList<InventoryEntry> Entries, int Total) Create(JudgedTargets judged)
    {
        var entries = new List<InventoryEntry>();
        entries.AddRange(ApprovedDiscovery(judged));
        entries.Add(new("role.codex.exec", 1, shape => IsCodexRole(judged, shape)));
        entries.Add(new("role.claude.print", 1, shape => IsClaudeRole(judged, shape)));
        entries.AddRange(LauncherGit(judged));
        entries.AddRange(HostGit(judged));
        return (entries, entries.Sum(entry => entry.Limit));
    }

    /// <summary>One discovery class per catalog capability: the owner-approved launch tuple of that capability (an executable, or the
    /// Node executable then the approved script) followed by exactly <c>--version</c>. A capability the owner did not approve has a
    /// class that matches nothing; a probe of any other file, or of an approved file by another argument shape, is unlisted.</summary>
    private static IEnumerable<InventoryEntry> ApprovedDiscovery(JudgedTargets judged) =>
        ApprovedLaunchSet.CapabilityNames.Select(capability => new InventoryEntry(
            "discovery." + capability,
            DiscoveryPerCapability,
            shape => judged.Approved?.Find(capability) is { } launch
                && launch.Executable.IsPath(shape.ExecutablePath)
                && (launch.Script is null
                    ? shape.ArgumentsAre("--version")
                    : shape.Arguments is [var script, "--version"] && launch.Script.IsPath(script))
                && shape.EnvironmentNamesWithin(DiscoveryEnvironment)
                && shape.TimeoutWithin(DiscoveryDeadline)
                && judged.Destinations.IsDiscoveryScratch(shape.WorkingDirectory)
                && judged.Destinations.IsDiscoveryScratch(shape.ApprovedRoot)));

    /// <summary>The Codex role: the approved launch followed by exactly the reviewed read-only <c>exec</c> vector, with no model or
    /// effort member. The result file and both sinks must be this session's run's artifact files of ONE attempt, and the schema must be
    /// that same attempt's file in the fixed production scratch location (<c>agent-scratch\run\attempt\schema.json</c>) whose bytes are
    /// exactly the production proposal contract, read and compared here: an arbitrary file named <c>schema.json</c> never qualifies.
    /// The working directory, the approved root and <c>--cd</c> are one workspace inside the owned workspaces directory, standard
    /// input is supplied, and the deadline is the reviewed ten-minute invocation limit at most.</summary>
    private static bool IsCodexRole(JudgedTargets judged, ProcessShape shape)
    {
        if (judged.CodexRoleOffset(shape) is not { } offset
            || shape.Arguments.Skip(offset).ToArray() is not
                ["exec", "--json", "--output-schema", var schema, "--output-last-message", var result, "--sandbox", "read-only",
                    "--cd", var workspace, "--ephemeral", "--ignore-user-config", "-"])
        {
            return false;
        }

        return judged.Destinations.AttemptOfArtifact(result, "final-response") is { } attempt
            && IsAttemptSinks(judged, shape, attempt)
            && IsRoleWorkspace(judged, shape, workspace)
            && shape.EnvironmentNamesWithin(CodexEnvironment)
            && shape.TimeoutWithin(RoleDeadline)
            && judged.Destinations.IsAttemptSchema(schema, attempt)
            && SchemaHasExactContractBytes(schema);
    }

    /// <summary>Both sinks are the stdout and stderr files of the same attempt of this session's run.</summary>
    private static bool IsAttemptSinks(JudgedTargets judged, ProcessShape shape, Guid attempt) =>
        judged.Destinations.AttemptOfArtifact(shape.StandardOutputSinkPath, "stdout") == attempt
        && judged.Destinations.AttemptOfArtifact(shape.StandardErrorSinkPath, "stderr") == attempt;

    private const long MaximumSchemaBytes = 64 * 1024;

    /// <summary>The schema file is a regular, non-alias file whose text is exactly the serialized production proposal contract.</summary>
    private static bool SchemaHasExactContractBytes(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint) || file.LinkTarget is not null
                || file.Length is < 1 or > MaximumSchemaBytes)
            {
                return false;
            }

            // Raw bytes, so a byte-order mark or any invisible difference is a different contract.
            var expected = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(CodexProposalOutputSchema.BuildSchemaDocument()));
            return File.ReadAllBytes(path).AsSpan().SequenceEqual(expected);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The Claude role: the approved launch followed by exactly the reviewed no-tools, single-turn, plan-mode,
    /// no-persistence <c>--print</c> vector. The schema member must be the production critical-review schema, the session identity a
    /// GUID, and there is no model or effort member.</summary>
    private static bool IsClaudeRole(JudgedTargets judged, ProcessShape shape)
    {
        if (judged.ClaudeRoleOffset(shape) is not { } offset)
        {
            return false;
        }

        var arguments = shape.Arguments.Skip(offset).ToArray();
        if (arguments.Length != ClaudeVector.Length || arguments.Where((argument, index) =>
                ClaudeVector[index] is { } fixedValue && !string.Equals(argument, fixedValue, StringComparison.Ordinal)).Any())
        {
            return false;
        }

        return string.Equals(arguments[ClaudeSchemaIndex], ClaudeSchemaJson(), StringComparison.Ordinal)
            && Guid.TryParseExact(arguments[ClaudeSessionIndex], "D", out _)
            && judged.Destinations.AttemptOfArtifact(shape.StandardOutputSinkPath, "stdout") is { } attempt
            && IsAttemptSinks(judged, shape, attempt)
            && IsRoleWorkspace(judged, shape, shape.WorkingDirectory)
            && shape.EnvironmentNamesWithin(ClaudeEnvironment)
            && shape.TimeoutWithin(RoleDeadline);
    }

    private const int ClaudeSchemaIndex = 6;
    private const int ClaudeSessionIndex = 20;

    /// <summary>The Claude vector with the two reviewed dynamic members (the schema text and the session identity) left null.</summary>
    private static readonly string?[] ClaudeVector =
    [
        "--print",
        "--input-format", "text",
        "--output-format", "json",
        "--json-schema", null,
        "--safe-mode",
        "--restricted",
        "--disable-slash-commands",
        "--no-chrome",
        "--permission-prompts", "none",
        "--tools", string.Empty,
        "--strict-mcp-config",
        "--permission-mode", "plan",
        "--no-session-persistence",
        "--session-id", null,
        "--max-turns", "1",
    ];

    private static string ClaudeSchemaJson() =>
        JsonSerializer.Serialize(ClaudeCriticalReviewOutputSchema.BuildSchemaDocument());

    /// <summary>A role invocation runs in the prepared workspace: the working directory equals the stated workspace and its approved
    /// root, both inside the owned workspaces directory, and standard input is supplied.</summary>
    private static bool IsRoleWorkspace(JudgedTargets judged, ProcessShape shape, string? workspace) =>
        workspace is not null
        && string.Equals(shape.WorkingDirectory, workspace, StringComparison.OrdinalIgnoreCase)
        && string.Equals(shape.ApprovedRoot, workspace, StringComparison.OrdinalIgnoreCase)
        && judged.Destinations.InWorkspaces(workspace)
        && shape.HasStandardInput == true;

    /// <summary>The launcher's own fixture creation (five commands) and its nine complete read-only snapshots of the source and the
    /// prepared worktree. The fixture's final <c>rev-parse HEAD</c> shares its shape with a snapshot's, so that class is one larger.
    /// Every command runs the approved Git file in a directory inside the owned root.</summary>
    private static IEnumerable<InventoryEntry> LauncherGit(JudgedTargets judged)
    {
        InventoryEntry Launcher(string name, int limit, string[] arguments) =>
            new(name, limit, shape => IsLauncherGit(judged, shape) && shape.ArgumentsAre(arguments));

        return
        [
            Launcher("launcher.git.init", 1, ["init", "--initial-branch", "main"]),
            Launcher("launcher.git.config", 1, ["config", "core.autocrlf", "false"]),
            new("launcher.git.add", 1, shape => IsLauncherGit(judged, shape)
                && shape.Arguments is ["add", "--", "README.md", "AGENTS.md", "CLAUDE.md", "src/Feature.cs"]),
            Launcher("launcher.git.commit", 1, [
                "-c", "user.name=Fixture Author",
                "-c", "user.email=fixture@example.invalid",
                "-c", "commit.gpgsign=false",
                "commit", "--no-verify", "-m", "Initial commit",
            ]),
            Launcher("launcher.git.head", 10, ["rev-parse", "HEAD"]),
            Launcher("launcher.git.headref", 9, ["rev-parse", "--symbolic-full-name", "HEAD"]),
            Launcher("launcher.git.refs", 9, ["for-each-ref", "--format=%(refname) %(objectname)"]),
            Launcher("launcher.git.index", 9, ["-c", "core.quotepath=false", "ls-files", "--stage"]),
        ];
    }

    private static bool IsLauncherGit(JudgedTargets judged, ProcessShape shape) =>
        judged.IsLauncherGit(shape)
        && judged.Destinations.ContainsOrIs(shape.WorkingDirectory)
        && shape.TimeoutWithin(LauncherGitDeadline)
        && shape.EnvironmentNamesWithin([.. GitProcess.EnvironmentNames])
        && PinnedLauncherGitConfiguration.All(name => shape.EnvironmentNames!.Contains(name, StringComparer.OrdinalIgnoreCase));

    /// <summary>The variables that pin the launcher Git child's effective configuration; a shape without them is unlisted.</summary>
    private static readonly string[] PinnedLauncherGitConfiguration =
        ["GIT_CONFIG_NOSYSTEM", "GIT_CONFIG_GLOBAL", "GIT_CONFIG_COUNT", "GIT_OPTIONAL_LOCKS", "GIT_TERMINAL_PROMPT"];

    /// <summary>The host's local Git commands: repository registration and identity (read-only), the one worktree creation, and the
    /// bounded evidence reads around each role invocation. Remote, network, credential and helper commands are never listed.</summary>
    private static IEnumerable<InventoryEntry> HostGit(JudgedTargets judged)
    {
        InventoryEntry Plain(string name, int limit, string[] rest) =>
            new(name, limit, shape => IsHost(judged, shape, out var tail) && tail.SequenceEqual(rest, StringComparer.Ordinal));

        InventoryEntry Evidence(string name, int limit, string[] rest) =>
            new(name, limit, shape => IsHost(judged, shape, out var tail)
                && tail.Take(AttributesOff.Length).SequenceEqual(AttributesOff, StringComparer.Ordinal)
                && tail.Skip(AttributesOff.Length).SequenceEqual(rest, StringComparer.Ordinal));

        InventoryEntry EvidencePathspec(string name, int limit, string[] leading) =>
            new(name, limit, shape => IsHost(judged, shape, out var tail)
                && tail.Take(AttributesOff.Length).SequenceEqual(AttributesOff, StringComparer.Ordinal)
                && IsPathspecRead(tail.Skip(AttributesOff.Length).ToArray(), leading));

        return
        [
            Plain("git.revparse.gitdir", 3, ["rev-parse", "--git-dir"]),
            Plain("git.revparse.bare", 2, ["rev-parse", "--is-bare-repository"]),
            Plain("git.revparse.toplevel", 2, ["rev-parse", "--show-toplevel"]),
            Plain("git.revparse.commondir", 4, ["rev-parse", "--git-common-dir"]),
            Plain("git.symbolicref", 4, ["symbolic-ref", "-q", "--short", "HEAD"]),
            Plain("git.revparse.head", 4, ["rev-parse", "HEAD"]),
            Plain("git.status.porcelain", 2, ["status", "--porcelain=v1"]),
            new("git.worktree.add", 1, shape => IsHost(judged, shape, out var tail)
                && tail is ["worktree", "add", "-b", var branch, var path, var commit]
                && BranchName().IsMatch(branch)
                && judged.Destinations.Contains(path)
                && CommitId().IsMatch(commit)),
            Evidence("git.evidence.revparse", 14, ["rev-parse", "HEAD"]),
            Evidence("git.evidence.status", 14, ["status", "--porcelain=v1", "-z", "--no-renames", "--untracked-files=all"]),
            Evidence("git.evidence.diff", 14, ["diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--binary", "HEAD"]),
            Evidence("git.evidence.hashobject", 8, ["hash-object", "--no-filters", "--stdin"]),
            EvidencePathspec("git.evidence.lsfiles.tracked", 4, ["--literal-pathspecs", "ls-files", "-z", "-v", "--"]),
            EvidencePathspec(
                "git.evidence.lsfiles.others",
                4,
                ["--literal-pathspecs", "ls-files", "-z", "--others", "--ignored", "--exclude-standard", "--"]),
        ];
    }

    /// <summary>A host Git command: the approved Git file (after the judgement accepted it) with the host's hardening prefix, the
    /// hardening environment names, a bounded deadline and a working directory and approved root inside the owned root.</summary>
    private static bool IsHost(JudgedTargets judged, ProcessShape shape, out string[] tail)
    {
        tail = [];
        if (!judged.IsHostGit(shape)
            || shape.Arguments.Count <= HostGitPrefix.Length
            || !shape.Arguments.Take(HostGitPrefix.Length).SequenceEqual(HostGitPrefix, StringComparer.Ordinal)
            || !shape.EnvironmentNamesWithin(GitEnvironment)
            || !shape.TimeoutWithin(GitDeadline)
            || !judged.Destinations.ContainsOrIs(shape.WorkingDirectory)
            || !judged.Destinations.ContainsOrIs(shape.ApprovedRoot))
        {
            return false;
        }

        tail = [.. shape.Arguments.Skip(HostGitPrefix.Length)];
        return true;
    }

    private static bool IsPathspecRead(string[] arguments, string[] leading) =>
        arguments.Length > leading.Length
        && arguments.Take(leading.Length).SequenceEqual(leading, StringComparer.Ordinal)
        && arguments.Skip(leading.Length).All(path => InstructionFiles.Contains(path, StringComparer.Ordinal));

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9/_.-]{0,99}$")]
    private static partial Regex BranchName();

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex CommitId();
}
