using System.Security.Cryptography;
using System.Text;
using DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The real environment of the acceptance session: a new owned disposable root under the temp directory, the tiny
/// fictitious repository, the production host on loopback with an in-memory launch secret, the authenticated HTTP routes,
/// read-only evidence and Git snapshots. The judge factory, the preparation hook, the process-table seam and the fixture objective exist so the offline
/// rehearsal can run this very composition against the owned doubles, with a native or a scripted read-only process table; the
/// launcher itself always supplies the real-installed-provider judge, no preparation, the native process table and the fixed
/// objective (the seam has no command-line or configuration route). The admission boundary and the judged-target pins are supplied
/// only through <see cref="Profile01Composition"/>.
/// </summary>
public sealed class ProductionQualificationEnvironment(
    QualificationLimits limits,
    string temporaryDirectory,
    Func<string, string, Func<IReadOnlyList<ProviderTargetFact>, TargetJudgement>> createJudge,
    Action<OwnedRootGuard>? prepareProviders = null,
    IProcessTable? processTable = null,
    int? launcherProcessId = null,
    ProcessAdmission? admission = null,
    JudgedTargets? judgedTargets = null,
    string? objective = null,
    IGitVersionObserver? gitVersions = null) : IQualificationEnvironment
{
    private const int MaximumAgentAttempts = 2;
    private const int MaximumAgentInvocationMinutes = 20;

    private readonly string _launchSecret = RandomNumberGenerator.GetHexString(64, lowercase: true);
    private OwnedRootGuard? _root;
    private string _ownerToken = string.Empty;
    private string _sourcePath = string.Empty;
    private string _workspacePath = string.Empty;
    private Func<IReadOnlyList<ProviderTargetFact>, TargetJudgement>? _judge;
    private ChildProcessWatch? _watch;
    private ProductionHost? _host;
    private QualificationApiClient? _api;
    private StageEvidenceReader? _evidence;
    private Guid _runId;

    /// <summary>The exact root this environment created and its token, set when the root is created; null before that.</summary>
    public OwnedRootReference? OwnedRoot { get; private set; }

    public ProcessAdmissionFacts AdmissionFacts => admission?.Facts ?? ProcessAdmissionFacts.None;

    public IReadOnlyList<string> ForbiddenFragments => new[]
    {
        _launchSecret,
        _root?.Root ?? string.Empty,
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(temporaryDirectory)),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    }.Where(fragment => fragment.Length > 0).ToArray();

    public Task PrepareFixtureAsync(CancellationToken cancellationToken)
    {
        _ownerToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var name = OwnedRootGuard.RootPrefix + "manual-" + Guid.NewGuid().ToString("N");
        var path = Path.Combine(temporaryDirectory, name);
        Directory.CreateDirectory(path);
        var markerPath = Path.Combine(path, OwnedRootGuard.MarkerFile);
        using (var marker = new FileStream(markerPath, FileMode.CreateNew, FileAccess.Write))
        {
            marker.Write(Encoding.ASCII.GetBytes(_ownerToken));
        }

        _root = OwnedRootGuard.Verify(path, _ownerToken);
        OwnedRoot = new OwnedRootReference(_root.Root, _ownerToken);
        _root.CreateLayout();
        // Every working directory, sink and destination a reviewed class admits must lie inside this one root.
        judgedTargets?.Destinations.Pin(_root.Root, _root.Workspaces, _root.Artifacts);
        prepareProviders?.Invoke(_root);
        _sourcePath = Path.Combine(_root.Root, "repos", "manual-qualification-source");
        Directory.CreateDirectory(_sourcePath);
        if (admission is not null && judgedTargets is not null)
        {
            // The first fixture mutation is the next call: the approved Git must first be observed, once, as new enough for the
            // constrained configuration its launcher commands rely on. Unknown, older or incomplete refuses; nothing falls back.
            GitVersionGate.Require(admission, gitVersions ?? new ProcessGitVersionObserver(), judgedTargets.Destinations.DiscoveryScratch);
        }

        FixtureRepository.Create(_sourcePath, admission);
        _judge = createJudge(_root.Root, AppContext.BaseDirectory);
        return Task.CompletedTask;
    }

    public SourceSnapshot SnapshotSource() => GitSourceReader.Read(_sourcePath, admission);

    public SourceSnapshot SnapshotWorkspace() => GitSourceReader.Read(_workspacePath, admission);

    public Task StartHostAsync(CancellationToken cancellationToken)
    {
        var api = ProductionHost.ReserveLoopbackAddress();
        _watch = ChildProcessWatch.Begin(processTable, launcherProcessId);
        _watch.StartSampling(TimeSpan.FromMilliseconds(250));
        _host = new ProductionHost(_root!, api, _launchSecret, admission);
        _host.Start();
        _api = new QualificationApiClient(api, _launchSecret, limits);
        return Task.CompletedTask;
    }

    public async Task<TargetJudgement> ObserveTargetsAsync(TimeSpan bound, CancellationToken cancellationToken)
    {
        var facts = new List<ProviderTargetFact>();
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(bound);
        var wanted = new[] { Capability.Git, Capability.CodexCli, Capability.ClaudeCli };
        try
        {
            while (true)
            {
                facts = await ReadCapabilitiesAsync(wanted, wait.Token);
                if (facts.Count == wanted.Length && facts.All(fact => fact.Reason != CapabilityProbeReason.NeverProbed))
                {
                    break;
                }

                await Task.Delay(500, wait.Token);
            }
        }
        catch (OperationCanceledException) when (BoundElapsed(wait, cancellationToken))
        {
            // The bound elapsed: judge what was observed, so a missing probe is reported as such.
        }

        var git = facts.FirstOrDefault(fact => fact.Capability == Capability.Git);
        var gitCode = git is null ? "NotObserved" : git.Reason.ToString();
        var gitReport = new TargetReport("git", git?.Reason == CapabilityProbeReason.None, gitCode, null, git?.Version);
        var judgement = _judge!(facts);
        var accepted = judgement.Accepted && gitReport.Real;
        var codex = facts.FirstOrDefault(fact => fact.Capability == Capability.CodexCli);
        var claude = facts.FirstOrDefault(fact => fact.Capability == Capability.ClaudeCli);
        var reports = judgement.Targets.ToList();
        if (accepted && git is not null && codex is not null && claude is not null && judgedTargets is not null
            && !judgedTargets.Pin(git, codex, claude))
        {
            // The host resolved something other than the exact tuple the owner approved: nothing of it is ever launched.
            accepted = false;
            reports = [.. reports.Select(report => report with { Real = false, Code = "NotTheApprovedIdentity" })];
            gitReport = gitReport with { Real = false, Code = "NotTheApprovedIdentity" };
        }

        return new TargetJudgement(accepted, [.. reports, gitReport]);
    }

    public async Task<SetupFacts> RegisterAndPrepareAsync(CancellationToken cancellationToken)
    {
        var projectId = await _api!.RegisterProjectAsync(FixtureRepository.ProjectName, _sourcePath, cancellationToken);
        await _api.RecheckIdentityAsync(projectId, cancellationToken);
        var prepared = await _api.PrepareWorkspaceAsync(projectId, cancellationToken);
        _workspacePath = prepared.WorkspacePath;
        await _api.CaptureCheckpointAsync(projectId, cancellationToken);
        _runId = await _api.CreateManualRunAsync(
            projectId,
            objective ?? FixtureRepository.Objective,
            MaximumAgentAttempts,
            MaximumAgentInvocationMinutes,
            cancellationToken);
        // Attempt-bound destinations (artifact files and the Codex schema) must belong to this one run.
        judgedTargets?.Destinations.PinRun(_runId);
        _evidence = new StageEvidenceReader(_host!.Services, _api, _root!.Artifacts, objective ?? FixtureRepository.Objective);
        return new SetupFacts(projectId, _runId, "refs/heads/" + prepared.BranchName);
    }

    public Task<SubmissionResult> SubmitAsync(
        AllowanceRole role,
        Guid? proposalMessageId,
        CancellationToken cancellationToken) =>
        role == AllowanceRole.Planner
            ? _api!.SubmitPlannerAsync(_runId, cancellationToken)
            : _api!.SubmitReviewerAsync(_runId, proposalMessageId!.Value, cancellationToken);

    public Task<StageReading> ReadStageAsync(AllowanceRole role, CancellationToken cancellationToken) =>
        _evidence!.ReadAsync(role, _runId, cancellationToken);

    public async Task<ShutdownReport> StopHostAsync(CancellationToken cancellationToken)
    {
        _api?.Dispose();
        var stopped = true;
        try
        {
            if (_host is not null)
            {
                await _host.DisposeAsync().AsTask().WaitAsync(cancellationToken);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException
            or ObjectDisposedException)
        {
            stopped = false;
        }

        var children = _watch is null
            ? ChildProof.Stopped
            : await _watch.ProveAsync(TimeSpan.FromSeconds(10));
        return new ShutdownReport(stopped, children.Proven, children.Reason);
    }

    public CleanupReport CleanUp() =>
        _root is null ? CleanupReport.Preserved("NoRoot") : OwnedRootCleaner.Remove(_root.Root, _ownerToken);

    public async ValueTask DisposeAsync()
    {
        _api?.Dispose();
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    private static bool BoundElapsed(CancellationTokenSource bound, CancellationToken caller) =>
        bound.IsCancellationRequested && !caller.IsCancellationRequested;

    private async Task<List<ProviderTargetFact>> ReadCapabilitiesAsync(
        Capability[] wanted,
        CancellationToken cancellationToken)
    {
        await using var scope = _host!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var snapshots = await db.HostCapabilitySnapshots.AsNoTracking()
            .Where(snapshot => wanted.Contains(snapshot.Capability)).ToListAsync(cancellationToken);
        return snapshots
            .Select(snapshot => new ProviderTargetFact(
                snapshot.Capability,
                snapshot.ReasonCode,
                snapshot.LaunchKind,
                snapshot.ResolvedExecutablePath,
                snapshot.ResolvedScriptPath,
                snapshot.ObservedVersion))
            .ToList();
    }
}
