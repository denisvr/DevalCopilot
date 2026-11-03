using System.Security.Cryptography;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>Owned files and owned shapes for the approved-launch tests: small regular files written under a test directory stand in
/// for every approved executable and script, so identity, drift and shape tests need no provider, no installed tool and no discovery.
/// The role and Git shapes are exactly what the unchanged production adapters build.</summary>
public sealed class ApprovedFixture
{
    public ApprovedFixture(string directory, bool codexScript = true, bool withOptional = true)
    {
        Directory = directory;
        Root = Path.Combine(directory, "owned-root");
        System.IO.Directory.CreateDirectory(Path.Combine(Root, "workspaces", "w1"));
        System.IO.Directory.CreateDirectory(Path.Combine(Root, "artifacts"));
        System.IO.Directory.CreateDirectory(Path.Combine(directory, "tools"));
        Workspace = Path.Combine(Root, "workspaces", "w1");
        Git = File("git.exe");
        Node = File("node.exe");
        Claude = File("claude.exe");
        CodexExecutable = codexScript ? Node : File("codex.exe");
        CodexScript = codexScript ? File("codex.js") : null;
        var launches = new List<ApprovedLaunch>
        {
            new(ApprovedLaunchSet.Git, Approved(Git), null),
            new(ApprovedLaunchSet.Node, Approved(Node), null),
            new(ApprovedLaunchSet.Codex, Approved(CodexExecutable), CodexScript is null ? null : Approved(CodexScript)),
            new(ApprovedLaunchSet.Claude, Approved(Claude), null),
        };
        if (withOptional)
        {
            DotNet = File("dotnet.exe");
            GitHub = File("gh.exe");
            Docker = File("docker.exe");
            launches.Add(new(ApprovedLaunchSet.DotNet, Approved(DotNet), null));
            launches.Add(new(ApprovedLaunchSet.GitHub, Approved(GitHub), null));
            launches.Add(new(ApprovedLaunchSet.Docker, Approved(Docker), null));
        }

        var (set, refusal) = ApprovedLaunchSet.TryCreate(launches);
        Set = set ?? throw new InvalidOperationException(refusal);
    }

    public string Directory { get; }

    public string Root { get; }

    /// <summary>An isolated stand-in for the user's local application data directory, so the production scratch layout is exercised
    /// without ever touching the real one.</summary>
    public string LocalAppData => Path.Combine(Directory, "localappdata");

    public Guid RunId { get; } = Guid.NewGuid();

    public Guid AttemptId { get; } = Guid.NewGuid();

    public string DiscoveryScratch => Path.Combine(LocalAppData, "DevalCopilot", "tool-discovery-scratch");

    /// <summary>The Codex output-schema file of this run and attempt in the production scratch layout.</summary>
    public string SchemaPath(Guid? attempt = null) =>
        Path.Combine(LocalAppData, "DevalCopilot", "agent-scratch", RunId.ToString(), (attempt ?? AttemptId).ToString(), "schema.json");

    /// <summary>An artifact file of this run and attempt in the production artifact layout.</summary>
    public string ArtifactFile(string stem, Guid? run = null, Guid? attempt = null) =>
        Path.Combine(Root, "artifacts", "runs", (run ?? RunId).ToString(), "attempts", (attempt ?? AttemptId).ToString(), stem + ".partial");

    /// <summary>Writes the exact production proposal schema (or the given text) where Codex would find it.</summary>
    public string WriteSchema(string? text = null, Guid? attempt = null)
    {
        var path = SchemaPath(attempt);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, text ?? JsonSerializer.Serialize(CodexProposalOutputSchema.BuildSchemaDocument()));
        return path;
    }

    public string Workspace { get; }

    public string Git { get; }

    public string Node { get; }

    public string Claude { get; }

    public string CodexExecutable { get; }

    public string? CodexScript { get; }

    public string? DotNet { get; }

    public string? GitHub { get; }

    public string? Docker { get; }

    public ApprovedLaunchSet Set { get; }

    public ProviderTargetFact GitFact => new(Capability.Git, CapabilityProbeReason.None, CapabilityLaunchKind.DirectExecutable, Git, null, "2.50.0");

    public ProviderTargetFact CodexFact => new(
        Capability.CodexCli,
        CapabilityProbeReason.None,
        CodexScript is null ? CapabilityLaunchKind.DirectExecutable : CapabilityLaunchKind.NodeScript,
        CodexExecutable,
        CodexScript,
        "0.43.0");

    public ProviderTargetFact ClaudeFact => new(Capability.ClaudeCli, CapabilityProbeReason.None, CapabilityLaunchKind.DirectExecutable, Claude, null, "2.1.0");

    /// <summary>A judged-target holder with the approved set, the owned root pinned and the three facts accepted.</summary>
    public JudgedTargets Judged(bool pin = true)
    {
        var judged = new JudgedTargets(Set, new OwnedDestinations(LocalAppData));
        judged.Destinations.Pin(Root, Path.Combine(Root, "workspaces"), Path.Combine(Root, "artifacts"));
        judged.Destinations.PinRun(RunId);
        if (pin)
        {
            Assert.True(judged.Pin(GitFact, CodexFact, ClaudeFact));
        }

        return judged;
    }

    public ProcessAdmission Admission(string ledgerDirectory, bool pin = true, int totalLimit = -1, Func<string?>? external = null)
    {
        var judged = Judged(pin);
        var (entries, total) = Profile01Inventory.Create(judged);
        var ledger = new AdmissionLedger(ledgerDirectory, "owner-test-authorization", TimeProvider.System);
        return new ProcessAdmission(entries, totalLimit < 0 ? total : totalLimit, ledger, judged.Revalidate, external)
        {
            LauncherGit = () => judged.LauncherGitPath,
        };
    }

    public static string ClaudeSchema() => JsonSerializer.Serialize(ClaudeCriticalReviewOutputSchema.BuildSchemaDocument());

    // Host shapes -----------------------------------------------------------------------------------------------------

    public static readonly string[] HostPrefix =
    [
        "--no-pager", "-c", "core.fsmonitor=false", "-c", "core.untrackedCache=false", "-c", "protocol.allow=never",
    ];

    public static readonly string[] AttributesOff = ["-c", "core.attributesfile=NUL"];

    public ProcessShape Host(params string[] rest) => new(Git, [.. HostPrefix, .. rest]) { WorkingDirectory = Workspace, ApprovedRoot = Workspace, EnvironmentNames = ["GIT_OPTIONAL_LOCKS", "GIT_TERMINAL_PROMPT"], Timeout = TimeSpan.FromSeconds(5) };

    public ProcessShape Evidence(params string[] rest) => Host([.. AttributesOff, .. rest]);

    public ProcessShape Launcher(params string[] arguments) => new(Git, arguments)
    {
        WorkingDirectory = Path.Combine(Root, "repos"),
        Timeout = TimeSpan.FromSeconds(60),
        EnvironmentNames = [.. GitProcess.ChildEnvironment().Keys],
    };

    public ProcessShape Probe(string executable, params string[] arguments) =>
        new(executable, arguments) { WorkingDirectory = DiscoveryScratch, ApprovedRoot = DiscoveryScratch, EnvironmentNames = [], Timeout = TimeSpan.FromSeconds(5) };

    public ProcessShape Worktree() => Host("worktree", "add", "-b", "devalcopilot/workspace/a/1", Path.Combine(Root, "workspaces", "w2"), new string('a', 40));

    // Role shapes -----------------------------------------------------------------------------------------------------

    /// <summary>The reviewed Codex vector for this run and attempt. The schema file is written with the exact production bytes unless
    /// <paramref name="schema"/> names another path (which is then not created).</summary>
    public string[] CodexVector(string? result = null, string? cd = null, string? schema = null)
    {
        schema ??= WriteSchema();
        return
        [
            .. CodexScript is null ? [] : new[] { CodexScript },
            "exec", "--json", "--output-schema", schema,
            "--output-last-message", result ?? ArtifactFile("final-response"),
            "--sandbox", "read-only", "--cd", cd ?? Workspace, "--ephemeral", "--ignore-user-config", "-",
        ];
    }

    public ProcessShape CodexRole(string[]? arguments = null) => new(CodexExecutable, arguments ?? CodexVector())
    {
        WorkingDirectory = Workspace,
        ApprovedRoot = Workspace,
        EnvironmentNames = ["SystemRoot", "USERPROFILE"],
        Timeout = TimeSpan.FromMinutes(10),
        StandardOutputSinkPath = ArtifactFile("stdout"),
        StandardErrorSinkPath = ArtifactFile("stderr"),
        HasStandardInput = true,
    };

    public static string[] ClaudeVector(string? sessionId = null) =>
    [
        "--print", "--input-format", "text", "--output-format", "json", "--json-schema", ClaudeSchema(),
        "--safe-mode", "--restricted", "--disable-slash-commands", "--no-chrome", "--permission-prompts", "none",
        "--tools", string.Empty, "--strict-mcp-config", "--permission-mode", "plan", "--no-session-persistence",
        "--session-id", sessionId ?? Guid.NewGuid().ToString(), "--max-turns", "1",
    ];

    public ProcessShape ClaudeRole(string[]? arguments = null) => new(Claude, arguments ?? ClaudeVector())
    {
        WorkingDirectory = Workspace,
        ApprovedRoot = Workspace,
        EnvironmentNames = ["SystemRoot", "USERPROFILE", "CLAUDE_CONFIG_DIR"],
        Timeout = TimeSpan.FromMinutes(10),
        StandardOutputSinkPath = ArtifactFile("stdout"),
        StandardErrorSinkPath = ArtifactFile("stderr"),
        HasStandardInput = true,
    };

    /// <summary>Rewrites the file's bytes (its digest changes, the approval does not).</summary>
    public static void Tamper(string path) => System.IO.File.AppendAllText(path, "tampered");

    private string File(string name)
    {
        var path = Path.Combine(Directory, "tools", name);
        if (!System.IO.File.Exists(path))
        {
            System.IO.File.WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes("owned stand-in for " + name));
        }

        return path;
    }

    private static ApprovedFile Approved(string path) =>
        new(path, Convert.ToHexStringLower(SHA256.HashData(System.IO.File.ReadAllBytes(path))));
}
