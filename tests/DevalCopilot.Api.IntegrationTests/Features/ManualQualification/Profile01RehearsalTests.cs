using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;
using DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexAccountAllowance;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>
/// The complete lot B composition (production host, authenticated routes, the new evidence facts, the admission boundary around every
/// host-issued and launcher-issued process and the durable counters) rehearsed against the journey's owned provider doubles only. It
/// resolves the exact process counts of the unchanged production flow, shows the closed inventory admits all of it, and shows an extra,
/// unlisted or exhausted process stopping the session. It is not a provider result.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class Profile01RehearsalTests : QualificationTestBase
{
    private const int LauncherId = 100;
    private static readonly DateTime Epoch = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ProcessEntry Explorer = new(10, 1, "explorer.exe", Epoch);
    private static readonly ProcessEntry Launcher = new(LauncherId, 10, "ManualQualification.exe", Epoch.AddMinutes(1));

    private static readonly QualificationLimits RehearsalLimits = new(
        Readiness: TimeSpan.FromSeconds(60),
        Setup: TimeSpan.FromSeconds(60),
        Submission: TimeSpan.FromSeconds(30),
        Read: TimeSpan.FromSeconds(20),
        PollInterval: TimeSpan.FromMilliseconds(200),
        Stage: TimeSpan.FromSeconds(120),
        Shutdown: TimeSpan.FromSeconds(60),
        Overall: TimeSpan.FromMinutes(8));

    /// <summary>The exact per-class counts the unchanged production flow issues for one Proposal and one review, resolved by this
    /// rehearsal. The three evidence classes and the launcher snapshots are deterministic; discovery is checked against its ceiling.</summary>
    private static readonly (string Name, int Count)[] ResolvedCounts =
    [
        ("role.codex.exec", 1), ("role.claude.print", 1),
        ("launcher.git.init", 1), ("launcher.git.config", 1), ("launcher.git.add", 1), ("launcher.git.commit", 1),
        ("launcher.git.head", 10), ("launcher.git.headref", 9), ("launcher.git.refs", 9), ("launcher.git.index", 9),
        ("git.revparse.gitdir", 3), ("git.revparse.bare", 2), ("git.revparse.toplevel", 2), ("git.revparse.commondir", 4),
        ("git.symbolicref", 4), ("git.revparse.head", 4), ("git.status.porcelain", 2), ("git.worktree.add", 1),
        ("git.evidence.revparse", 14), ("git.evidence.status", 14), ("git.evidence.diff", 14), ("git.evidence.hashobject", 8),
        ("git.evidence.lsfiles.tracked", 4), ("git.evidence.lsfiles.others", 4),
    ];

    [Fact]
    public async Task The_complete_flow_is_admitted_by_the_closed_inventory_with_the_resolved_counts_and_no_refusal()
    {
        using var rehearsal = await RehearseAsync();

        Assert.True(rehearsal.Report.Qualified, rehearsal.Summary);
        AssertReviewedInventory(rehearsal);
    }

    [Fact]
    public async Task One_claude_response_that_persists_two_challenges_is_verified_as_a_set_through_both_evidence_routes()
    {
        using var rehearsal = await RehearseAsync(
            objective: FixtureRepository.Objective + " " + ManifestInfo.ReportedTwoChallengesMarker);

        Assert.True(rehearsal.Report.Qualified, rehearsal.Summary);
        Assert.Equal("Challenge", rehearsal.Report.Verdict);
        var proposal = rehearsal.Report.Planner!.SoleMessage!.Id;
        var reviewer = rehearsal.Report.Reviewer!;
        Assert.Equal("Challenged", reviewer.Outcome);
        Assert.Equal(2, reviewer.MessageCount);
        Assert.Equal(2, reviewer.Messages.Count);
        Assert.Equal(2, reviewer.Messages.Select(message => message.Id).Distinct().Count());
        Assert.All(reviewer.Messages, message =>
        {
            Assert.Equal("Challenge", message.Type);
            Assert.Equal(proposal, message.InReplyToMessageId);
            Assert.Equal(reviewer.AttemptId, message.AttemptId);
            Assert.Equal("ClaudeCode.CriticalReviewer", message.Actor);
            Assert.Equal("ProviderObserved", message.Provenance);
            Assert.Equal(EvidenceRouteComparisonStatus.Agrees, message.EvidenceRouteComparison);
            Assert.Equal(EvidenceRouteComparisonStatus.Agrees, message.PersistedMetadataComparison);
        });
        Assert.Equal(EvidenceRouteComparisonStatus.Agrees, reviewer.MessageSetComparison);
        Assert.True(reviewer.AttemptEvidenceRouteAgrees);
        Assert.True(reviewer.StatusRouteAgrees);
        Assert.Equal([proposal], reviewer.InputMessageIds);

        // One start per role and the reviewed inventory: the two messages cost no process of their own.
        Assert.Equal(1, rehearsal.Report.Admission.Admitted["role.codex.exec"]);
        Assert.Equal(1, rehearsal.Report.Admission.Admitted["role.claude.print"]);
        AssertReviewedInventory(rehearsal);
        using var summary = JsonDocument.Parse(rehearsal.Summary);
        Assert.Equal(4, summary.RootElement.GetProperty("schemaVersion").GetInt32());
        var listed = summary.RootElement.GetProperty("reviewer").GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(2, listed.Length);
        Assert.All(listed, message => Assert.Equal("Agrees", message.GetProperty("evidenceRouteComparison").GetString()));
        Assert.Equal("Agrees", summary.RootElement.GetProperty("reviewer").GetProperty("messageSetComparison").GetString());
        Assert.DoesNotContain("duplicates the arithmetic", rehearsal.Summary, StringComparison.Ordinal);
    }

    private static readonly string TwoChallengesObjective = FixtureRepository.Objective + " " + ManifestInfo.ReportedTwoChallengesMarker;

    [Fact]
    public async Task A_failing_later_reply_ends_the_review_with_a_closed_failure_and_asks_for_nothing_more()
    {
        var routes = new RouteHandler(before: (_, ordinal) => ordinal == 3 ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : null);

        using var rehearsal = await RehearseAsync(objective: TwoChallengesObjective, handler: () => routes);

        Assert.Equal(QualificationResult.Failed, rehearsal.Report.Result);
        Assert.Equal("ReviewerEvidenceRouteUnavailable", rehearsal.Report.Code);
        // The Proposal, the first Challenge, and the second Challenge whose request failed: three requests for the whole session.
        Assert.Equal(3, routes.MessageEvidenceRequests.Count);
        Assert.Equal(3, routes.MessageEvidenceRequests.Distinct().Count());
        var reviewer = rehearsal.Report.Reviewer!;
        Assert.Equal(2, reviewer.MessageCount);
        Assert.Equal(
            [EvidenceRouteComparisonStatus.Agrees, EvidenceRouteComparisonStatus.Unavailable],
            reviewer.Messages.Select(message => message.EvidenceRouteComparison));
        Assert.Equal(EvidenceRouteComparisonStatus.Unavailable, reviewer.EvidenceRouteComparison);
        Assert.Equal(1, rehearsal.Report.Admission.Admitted["role.claude.print"]);
        Assert.Equal(0, rehearsal.Report.Admission.TotalRefused);
        using var summary = JsonDocument.Parse(rehearsal.Summary);
        var listed = summary.RootElement.GetProperty("reviewer").GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal("Agrees", listed[0].GetProperty("evidenceRouteComparison").GetString());
        Assert.Equal("Unavailable", listed[1].GetProperty("evidenceRouteComparison").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_retried_read_of_the_review_stage_never_restarts_the_message_evidence_requests(bool laterReplyFails)
    {
        var routes = new RouteHandler(
            before: (_, ordinal) => laterReplyFails && ordinal == 3 ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : null);
        ForcedRetryEnvironment? retrying = null;

        using var rehearsal = await RehearseAsync(
            objective: TwoChallengesObjective,
            handler: () => routes,
            decorate: inner => retrying = new ForcedRetryEnvironment(inner, AllowanceRole.Reviewer));

        // The first terminal read of the review failed after its requests; the session read it again, and the second read asked for nothing.
        Assert.True(retrying!.ReviewReads >= 2, "The review stage was not read again.");
        Assert.Equal(3, routes.MessageEvidenceRequests.Count);
        Assert.Equal(3, routes.MessageEvidenceRequests.Distinct().Count());
        if (laterReplyFails)
        {
            Assert.Equal("ReviewerEvidenceRouteUnavailable", rehearsal.Report.Code);
        }
        else
        {
            Assert.True(rehearsal.Report.Qualified, rehearsal.Summary);
            Assert.Equal(2, rehearsal.Report.Reviewer!.Messages.Count);
        }

        Assert.Equal(1, rehearsal.Report.Admission.Admitted["role.claude.print"]);
    }

    [Theory]
    [InlineData("inputMessagesStatus", "Invalid")]
    [InlineData("inputMessagesStatus", "Empty")]
    [InlineData("inputMessagesOmitted", true)]
    [InlineData("inputMessageTotalCount", 2)]
    public async Task A_malformed_input_envelope_on_a_later_reply_whose_listed_ids_match_is_a_disagreement(string member, object value)
    {
        var routes = new RouteHandler(
            after: (request, ordinal, response) => ordinal == 3
                ? RewriteJsonAsync(response, node => node[member] = Scalar(value))
                : Task.FromResult(response));

        using var rehearsal = await RehearseAsync(objective: TwoChallengesObjective, handler: () => routes);

        Assert.Equal(QualificationResult.Failed, rehearsal.Report.Result);
        Assert.Equal("ReviewerEvidenceRouteDisagrees", rehearsal.Report.Code);
        var reviewer = rehearsal.Report.Reviewer!;
        Assert.Equal(
            [EvidenceRouteComparisonStatus.Agrees, EvidenceRouteComparisonStatus.Disagrees],
            reviewer.Messages.Select(message => message.EvidenceRouteComparison));
        Assert.Equal([rehearsal.Report.Planner!.SoleMessage!.Id], reviewer.InputMessageIds);
        Assert.Equal(3, routes.MessageEvidenceRequests.Count);
    }

    [Fact]
    public async Task A_planner_message_evidence_without_a_coherent_empty_envelope_stops_the_session_before_the_review()
    {
        var routes = new RouteHandler(
            after: (_, ordinal, response) => ordinal == 1
                ? RewriteJsonAsync(response, node => node["inputMessagesStatus"] = "Recorded")
                : Task.FromResult(response));

        using var rehearsal = await RehearseAsync(handler: () => routes);

        Assert.Equal(QualificationResult.Failed, rehearsal.Report.Result);
        Assert.Equal("PlannerEvidenceRouteDisagrees", rehearsal.Report.Code);
        Assert.False(Ledger.IsConsumed(AllowanceRole.Reviewer));
        Assert.Single(routes.MessageEvidenceRequests);
    }

    [Fact]
    public async Task An_observed_empty_timeline_beside_stored_replies_is_a_disagreement_not_an_unattempted_comparison()
    {
        var routes = new RouteHandler(
            after: (request, _, response) => request.RequestUri!.AbsolutePath.EndsWith("/collaboration-timeline", StringComparison.Ordinal)
                ? RewriteJsonAsync(response, node =>
                {
                    var kept = node.AsArray().Where(entry => entry!["type"]!.GetValue<string>() != "Challenge").Select(entry => entry!.DeepClone()).ToArray();
                    var array = node.AsArray();
                    array.Clear();
                    foreach (var entry in kept)
                    {
                        array.Add(entry);
                    }
                })
                : Task.FromResult(response));

        using var rehearsal = await RehearseAsync(objective: TwoChallengesObjective, handler: () => routes);

        Assert.Equal(QualificationResult.Failed, rehearsal.Report.Result);
        Assert.Equal("ReviewerMessageNotRecorded", rehearsal.Report.Code);
        var reviewer = rehearsal.Report.Reviewer!;
        Assert.Equal(0, reviewer.MessageCount);
        Assert.Empty(reviewer.Messages);
        Assert.Equal(EvidenceRouteComparisonStatus.Disagrees, reviewer.MessageSetComparison);
        Assert.Single(routes.MessageEvidenceRequests);
        using var summary = JsonDocument.Parse(rehearsal.Summary);
        var stage = summary.RootElement.GetProperty("reviewer");
        Assert.Equal(0, stage.GetProperty("messageCount").GetInt32());
        Assert.Equal(0, stage.GetProperty("messages").GetArrayLength());
        Assert.Equal("Disagrees", stage.GetProperty("messageSetComparison").GetString());
        Assert.False(stage.GetProperty("lineage").GetProperty("repliesToThePersistedProposal").GetBoolean());
    }

    private static JsonNode Scalar(object value) => value switch
    {
        string text => JsonValue.Create(text),
        bool flag => JsonValue.Create(flag),
        int number => JsonValue.Create(number),
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static async Task<HttpResponseMessage> RewriteJsonAsync(HttpResponseMessage response, Action<JsonNode> edit)
    {
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        edit(node);
        var rewritten = new HttpResponseMessage(response.StatusCode)
        {
            Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        response.Dispose();
        return rewritten;
    }

    private static void AssertReviewedInventory(Rehearsal rehearsal)
    {
        var facts = rehearsal.Report.Admission;
        Assert.Equal(0, facts.TotalRefused);
        foreach (var (name, count) in ResolvedCounts)
        {
            Assert.True(facts.Admitted.TryGetValue(name, out var admitted), $"{name} was never admitted. {rehearsal.Summary}");
            Assert.True(count == admitted, $"{name}: expected {count} admitted {admitted}. {string.Join(", ", facts.Admitted.Select(pair => pair.Key + "=" + pair.Value))}");
        }

        var discovery = facts.Admitted.Where(pair => pair.Key.StartsWith("discovery.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(7, discovery.Length);
        // The launcher's own bounded version observation of the approved Git (before its first fixture mutation) is one more
        // discovery.git start beside the host's own probe of it: at least two, still inside the eight-start ceiling.
        Assert.InRange(facts.Admitted["discovery.git"], 2, Profile01Inventory.DiscoveryPerCapability);
        Assert.All(discovery, pair => Assert.InRange(pair.Value, 1, Profile01Inventory.DiscoveryPerCapability));
        Assert.Equal(80, facts.Admitted.Where(pair => pair.Key.StartsWith("git.", StringComparison.Ordinal)).Sum(pair => pair.Value));
        Assert.Equal(41, facts.Admitted.Where(pair => pair.Key.StartsWith("launcher.git.", StringComparison.Ordinal)).Sum(pair => pair.Value));
        Assert.Equal(facts.TotalAdmitted, rehearsal.Counters.Consumed()[AdmissionLedger.TotalName]);
        Assert.Equal(ResolvedCounts.Length + 7, facts.Admitted.Count);
        Assert.False(facts.Admitted.ContainsKey("codex.appserver"));
    }

    [Fact]
    public async Task The_real_reader_carries_the_recorded_contract_io_and_evidence_route_facts_of_both_accepted_attempts()
    {
        using var rehearsal = await RehearseAsync();

        Assert.True(rehearsal.Report.Qualified, rehearsal.Summary);
        Assert.Equal(QualificationContracts.PlannerAdapter, rehearsal.Report.Planner!.AdapterContractVersion);
        Assert.Equal(QualificationContracts.ReviewerAdapter, rehearsal.Report.Reviewer!.AdapterContractVersion);
        Assert.All(new[] { rehearsal.Report.Planner, rehearsal.Report.Reviewer }, reading =>
        {
            Assert.True(reading.RequiredIoComplete);
            Assert.True(reading.AttemptEvidenceRouteAgrees);
            Assert.Equal("NotRecorded", reading.InvocationDiagnosticState);
            Assert.Null(reading.InvocationDiagnosticCategory);
        });
        using var summary = JsonDocument.Parse(rehearsal.Summary);
        Assert.Equal(4, summary.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(InvocationLedger.Authorization, summary.RootElement.GetProperty("authorization").GetString());
        Assert.True(summary.RootElement.GetProperty("reviewer").GetProperty("ioCompletion").GetProperty("requiredComplete").GetBoolean());
        Assert.Equal(0, summary.RootElement.GetProperty("processAdmission").GetProperty("totalRefused").GetInt32());
    }

    [Fact]
    public async Task A_failed_claude_attempt_with_no_message_is_read_through_the_real_routes_and_reported_with_its_closed_diagnostic()
    {
        using var rehearsal = await RehearseAsync(
            objective: FixtureRepository.Objective + " " + ManifestInfo.ReportedAuthenticationErrorMarker);

        Assert.Equal(QualificationResult.Failed, rehearsal.Report.Result);
        Assert.StartsWith("ReviewerAttemptFailed", rehearsal.Report.Code, StringComparison.Ordinal);
        var reviewer = rehearsal.Report.Reviewer!;
        Assert.Equal(QualificationContracts.ReviewerAdapter, reviewer.AdapterContractVersion);
        Assert.Equal(0, reviewer.MessageCount);
        Assert.Empty(reviewer.Messages);
        Assert.Equal("Exited", reviewer.ProcessOutcome);
        Assert.Equal(1, reviewer.ExitCode);
        Assert.True(reviewer.RequiredIoComplete);
        Assert.True(reviewer.AttemptEvidenceRouteAgrees);
        Assert.Equal("Recorded", reviewer.InvocationDiagnosticState);
        Assert.Equal("AuthenticationRequired", reviewer.InvocationDiagnosticCategory);
        Assert.Equal("agent-invocation-diagnostic-v1", reviewer.InvocationDiagnosticVersion);
        Assert.Equal(0, rehearsal.Report.Admission.TotalRefused);
        Assert.Equal(1, rehearsal.Report.Admission.Admitted["role.claude.print"]);
        Assert.DoesNotContain(ClaudeRole.ReportedErrorNarrative, rehearsal.Summary, StringComparison.Ordinal);
        using var summary = JsonDocument.Parse(rehearsal.Summary);
        var diagnostic = summary.RootElement.GetProperty("reviewer").GetProperty("invocationDiagnostic");
        Assert.Equal("AuthenticationRequired", diagnostic.GetProperty("category").GetString());
        Assert.Equal("Failed", summary.RootElement.GetProperty("result").GetString());
    }

    [Fact]
    public async Task A_lowered_ceiling_stops_the_real_flow_at_the_first_extra_process_without_a_second_role_or_a_refund()
    {
        using var rehearsal = await RehearseAsync(ceilings: new Dictionary<string, int> { ["git.evidence.diff"] = 2 });

        Assert.NotEqual(QualificationResult.Qualified, rehearsal.Report.Result);
        Assert.Equal("ProcessAdmissionRefused", rehearsal.Report.Code);
        Assert.True(rehearsal.Report.Admission.Refused.ContainsKey("ClassLimit.git.evidence.diff"), rehearsal.Summary);
        Assert.Equal(2, rehearsal.Report.Admission.Admitted["git.evidence.diff"]);
        Assert.False(rehearsal.Report.Admission.Admitted.ContainsKey("role.claude.print"));
        Assert.DoesNotContain("role.claude.print", rehearsal.Counters.Consumed().Keys);
    }

    [Fact]
    public async Task An_unlisted_discovery_probe_in_the_real_flow_is_refused_before_its_process_and_blocks_before_any_allowance()
    {
        using var rehearsal = await RehearseAsync(withoutClasses: ["discovery.docker"]);

        Assert.Equal(QualificationResult.Blocked, rehearsal.Report.Result);
        Assert.Equal("ProcessAdmissionRefused", rehearsal.Report.Code);
        Assert.Equal(1, rehearsal.Report.Admission.Refused["Unlisted"]);
        Assert.False(rehearsal.Report.PlannerConsumed);
        Assert.False(Ledger.IsConsumed(AllowanceRole.Planner));
        Assert.False(rehearsal.Report.Admission.Admitted.ContainsKey("role.codex.exec"));
    }

    [Fact]
    public async Task Without_the_judged_pins_no_host_git_or_role_shape_is_listed_and_the_session_is_blocked_with_nothing_spent()
    {
        using var rehearsal = await RehearseAsync(neverPin: true);

        Assert.NotEqual(QualificationResult.Qualified, rehearsal.Report.Result);
        Assert.Equal("ProcessAdmissionRefused", rehearsal.Report.Code);
        Assert.False(Ledger.IsConsumed(AllowanceRole.Planner));
        Assert.False(rehearsal.Report.Admission.Admitted.ContainsKey("role.codex.exec"));
    }

    [Fact]
    public async Task A_duplicate_entry_over_the_spent_counters_starts_nothing_at_all()
    {
        using var first = await RehearseAsync();
        Assert.True(first.Report.Qualified, first.Summary);
        var rootsBefore = Directory.GetDirectories(Path.GetTempPath(), OwnedRootGuard.RootPrefix + "manual-*");

        using var second = await RehearseAsync();

        Assert.Equal(QualificationResult.SessionSpent, second.Report.Result);
        Assert.Equal(0, second.Report.Admission.TotalAdmitted);
        Assert.Equal(rootsBefore, Directory.GetDirectories(Path.GetTempPath(), OwnedRootGuard.RootPrefix + "manual-*"));
    }

    /// <summary>Observes the host routes the real reader calls and, for the rehearsal only, answers or rewrites some of them. The
    /// ordinal counts the message-evidence requests of the whole session, in order: the Proposal first, then the replies.</summary>
    private sealed class RouteHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage?>? before = null,
        Func<HttpRequestMessage, int, HttpResponseMessage, Task<HttpResponseMessage>>? after = null) : DelegatingHandler(new HttpClientHandler())
    {
        private readonly List<string> _messageEvidence = [];

        public IReadOnlyList<string> MessageEvidenceRequests
        {
            get
            {
                lock (_messageEvidence)
                {
                    return [.. _messageEvidence];
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var ordinal = 0;
            if (path.Contains("/collaboration-messages/", StringComparison.Ordinal) && path.EndsWith("/evidence", StringComparison.Ordinal))
            {
                lock (_messageEvidence)
                {
                    _messageEvidence.Add(path);
                    ordinal = _messageEvidence.Count;
                }
            }

            if (before?.Invoke(request, ordinal) is { } answered)
            {
                return answered;
            }

            var response = await base.SendAsync(request, cancellationToken);
            return after is null ? response : await after(request, ordinal, response);
        }
    }

    /// <summary>Makes the first terminal read of one role fail after the reader finished it, as any later failure of a poll would, so
    /// the session reads that stage again. It forwards everything else unchanged and never disposes the real environment.</summary>
    private sealed class ForcedRetryEnvironment(IQualificationEnvironment inner, AllowanceRole role) : IQualificationEnvironment
    {
        private bool _failed;

        public int ReviewReads { get; private set; }

        public IReadOnlyList<string> ForbiddenFragments => inner.ForbiddenFragments;

        public ProcessAdmissionFacts AdmissionFacts => inner.AdmissionFacts;

        public Task PrepareFixtureAsync(CancellationToken cancellationToken) => inner.PrepareFixtureAsync(cancellationToken);

        public SourceSnapshot SnapshotSource() => inner.SnapshotSource();

        public Task StartHostAsync(CancellationToken cancellationToken) => inner.StartHostAsync(cancellationToken);

        public Task<TargetJudgement> ObserveTargetsAsync(TimeSpan bound, CancellationToken cancellationToken) =>
            inner.ObserveTargetsAsync(bound, cancellationToken);

        public Task<SetupFacts> RegisterAndPrepareAsync(CancellationToken cancellationToken) => inner.RegisterAndPrepareAsync(cancellationToken);

        public SourceSnapshot SnapshotWorkspace() => inner.SnapshotWorkspace();

        public Task<SubmissionResult> SubmitAsync(AllowanceRole submitted, Guid? proposalMessageId, CancellationToken cancellationToken) =>
            inner.SubmitAsync(submitted, proposalMessageId, cancellationToken);

        public async Task<StageReading> ReadStageAsync(AllowanceRole read, CancellationToken cancellationToken)
        {
            var reading = await inner.ReadStageAsync(read, cancellationToken);
            if (read != role)
            {
                return reading;
            }

            ReviewReads++;
            if (reading.IsTerminal && !_failed)
            {
                _failed = true;
                throw new InvalidOperationException("A forced failure after a complete read.");
            }

            return reading;
        }

        public Task<ShutdownReport> StopHostAsync(CancellationToken cancellationToken) => inner.StopHostAsync(cancellationToken);

        public CleanupReport CleanUp() => inner.CleanUp();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DoubleGitVersion(GitVersionObservation observation) : IGitVersionObserver
    {
        public int Calls { get; private set; }

        public IReadOnlyList<string> EnvironmentNames => [];

        public GitVersionObservation Observe(string gitPath, string workingDirectory, TimeSpan bound)
        {
            Calls++;
            return observation;
        }
    }

    [Theory]
    [InlineData("git version 2.31.1", "GitVersionTooOld")]
    [InlineData("git version", "GitVersionUnknown")]
    [InlineData("git version 2.50.0.windows.1\n", null)]
    public async Task The_real_flow_observes_the_approved_git_once_before_any_fixture_mutation_and_refuses_an_unsupported_one(string output, string? code)
    {
        var observer = new DoubleGitVersion(new GitVersionObservation(true, 0, false, false, output));

        using var rehearsal = await RehearseAsync(gitVersions: observer);

        Assert.Equal(1, observer.Calls);
        if (code is null)
        {
            Assert.True(rehearsal.Report.Qualified, rehearsal.Summary);
            return;
        }

        Assert.Equal(QualificationResult.Blocked, rehearsal.Report.Result);
        Assert.Equal(code, rehearsal.Report.Code);
        Assert.False(Ledger.IsConsumed(AllowanceRole.Planner));
        // No fixture Git command ran: the observation is the launcher's only Git start, and it is the discovery.git class.
        Assert.Equal(1, rehearsal.Report.Admission.Admitted["discovery.git"]);
        Assert.DoesNotContain(rehearsal.Report.Admission.Admitted.Keys, name => name.StartsWith("launcher.git.", StringComparison.Ordinal));
        Assert.Equal(0, rehearsal.Report.Admission.TotalRefused);
    }

    [Fact]
    public async Task The_three_codex_app_server_observers_are_refused_and_counted_never_launched_and_never_an_invented_readiness()
    {
        var (admission, _) = Profile01Composition.CreateAdmission(Path.Combine(LedgerDirectory, "observers"));
        var observers = new AdmissionRefusingCodexObservers(admission);

        var allowance = await ((ICodexAccountAllowanceAdapter)observers).ObserveAsync(@"C:\x\codex.exe", null, CancellationToken.None);
        var catalog = await ((ICodexModelCatalogAdapter)observers).ObserveAsync(@"C:\x\codex.exe", @"C:\x\codex.js", CancellationToken.None);
        var usage = await ((IAccountUsageObserver)observers).ObserveAsync(@"C:\x\codex.exe", null, CancellationToken.None);

        Assert.Same(CodexAccountAllowanceObservation.Unknown, allowance);
        Assert.Same(CodexModelCatalogObservation.Unknown, catalog);
        Assert.Same(AccountUsageObservation.Unavailable, usage);
        // The first call closes the gate with its own reason; the other two are refused by the closed gate, never launched.
        Assert.Equal(1, admission.Facts.Refused["Unlisted"]);
        Assert.Equal(2, admission.Facts.Refused[ProcessAdmission.ClosedCode]);
        Assert.Equal(3, admission.Facts.TotalRefused);
        Assert.Equal("Unlisted", admission.Facts.ClosedBy);
        Assert.Equal(0, admission.Facts.TotalAdmitted);
        Assert.False(Directory.Exists(admission.Ledger.LedgerDirectory));
    }

    [Fact]
    public async Task An_approved_identity_that_is_not_the_launched_file_stops_the_real_flow_at_the_first_probe_before_any_allowance()
    {
        using var rehearsal = await RehearseAsync(approval: set => WithClaudeDigest(set, new string('0', 64)));

        Assert.Equal(QualificationResult.Blocked, rehearsal.Report.Result);
        Assert.Equal("ProcessAdmissionRefused", rehearsal.Report.Code);
        Assert.True(rehearsal.Report.Admission.Refused.ContainsKey("FileIdentityDrift"), rehearsal.Summary);
        Assert.Equal("FileIdentityDrift", rehearsal.Report.Admission.ClosedBy);
        Assert.False(Ledger.IsConsumed(AllowanceRole.Planner));
        Assert.False(rehearsal.Report.Admission.Admitted.ContainsKey("role.codex.exec"));
        Assert.False(rehearsal.Report.Cleanup!.Removed);
        Assert.Equal("InspectionRequired", rehearsal.Report.Cleanup.Reason);
    }

    [Fact]
    public async Task The_host_resolving_another_launch_than_the_approved_tuple_blocks_the_session_with_nothing_spent()
    {
        using var rehearsal = await RehearseAsync(approval: set => WithClaudePath(set, Path.Combine(Path.GetTempPath(), "never-the-claude-double.exe")));

        Assert.NotEqual(QualificationResult.Qualified, rehearsal.Report.Result);
        Assert.False(Ledger.IsConsumed(AllowanceRole.Planner));
        Assert.False(rehearsal.Report.Admission.Admitted.ContainsKey("role.codex.exec"));
    }

    private static ApprovedLaunchSet WithClaudeDigest(ApprovedLaunchSet set, string digest) =>
        Rebuild(set, claude => claude with { Executable = claude.Executable with { Sha256 = digest } });

    private static ApprovedLaunchSet WithClaudePath(ApprovedLaunchSet set, string path) =>
        Rebuild(set, claude => claude with { Executable = claude.Executable with { Path = path } });

    private static ApprovedLaunchSet Rebuild(ApprovedLaunchSet set, Func<ApprovedLaunch, ApprovedLaunch> changeClaude) =>
        ApprovedLaunchSet.TryCreate(
            [.. set.Launches.Select(launch => launch.Capability == ApprovedLaunchSet.Claude ? changeClaude(launch) : launch)]).Set!;

    /// <summary>The approved identities of the rehearsal: the owned doubles installed in the owned root and the real toolchain the
    /// production resolver finds in the same order (this process' search path, then the catalog's fixed directories). Owned stand-ins
    /// for the owner's manifest; nothing here is an installed provider.</summary>
    private static ApprovedLaunchSet RehearsalApprovals(OwnedRootGuard root)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string? Locate(string name, params string[] fallbacks)
        {
            var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Concat(fallbacks.Select(fallback => Path.Combine(programFiles, fallback)));
            return directories.Select(directory => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
        }

        ApprovedLaunch? Launch(string capability, string? path) =>
            path is null
                ? null
                : new ApprovedLaunch(
                    capability,
                    new ApprovedFile(
                        Path.GetFullPath(path),
                        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))),
                    null);

        var launches = new[]
        {
            Launch(ApprovedLaunchSet.Git, Locate("git.exe", @"Git\cmd", @"Git\bin")),
            Launch(ApprovedLaunchSet.DotNet, Locate("dotnet.exe", "dotnet")),
            Launch(ApprovedLaunchSet.Node, Locate("node.exe", "nodejs")),
            Launch(ApprovedLaunchSet.GitHub, Path.Combine(root.Bin, "gh.exe")),
            Launch(ApprovedLaunchSet.Docker, Locate("docker.exe", @"Docker\Docker\resources\bin")),
            Launch(ApprovedLaunchSet.Codex, Path.Combine(root.Bin, "codex.exe")),
            Launch(ApprovedLaunchSet.Claude, Path.Combine(root.Bin, "claude.exe")),
        }.OfType<ApprovedLaunch>().ToArray();
        var (set, refusal) = ApprovedLaunchSet.TryCreate(launches);
        return set ?? throw new InvalidOperationException("The rehearsal approvals are incomplete: " + refusal);
    }

    private async Task<Rehearsal> RehearseAsync(
        string? objective = null,
        IReadOnlyDictionary<string, int>? ceilings = null,
        IReadOnlyCollection<string>? withoutClasses = null,
        bool neverPin = false,
        Func<ApprovedLaunchSet, ApprovedLaunchSet>? approval = null,
        IGitVersionObserver? gitVersions = null,
        Func<HttpMessageHandler>? handler = null,
        Func<IQualificationEnvironment, IQualificationEnvironment>? decorate = null)
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var table = new ScriptedProcessTable([Explorer, Launcher], [Explorer, Launcher]);
        var judged = new JudgedTargets();
        var (entries, total) = Profile01Inventory.Create(judged);
        var adjusted = entries
            .Where(entry => withoutClasses?.Contains(entry.Name) != true)
            .Select(entry => ceilings is not null && ceilings.TryGetValue(entry.Name, out var limit) ? entry with { Limit = limit } : entry)
            .ToArray();
        var counters = new AdmissionLedger(LedgerDirectory, InvocationLedger.Authorization, TimeProvider.System);
        var admission = new ProcessAdmission(adjusted, total, counters, judged.Revalidate) { LauncherGit = () => judged.LauncherGitPath };
        var environment = new ProductionQualificationEnvironment(
            RehearsalLimits,
            Path.GetTempPath(),
            (root, _) => facts => AcceptOnlyOwnedDoubles(root, facts),
            root =>
            {
                FixtureInstaller.Install(root, AppContext.BaseDirectory);
                Environment.SetEnvironmentVariable("PATH", root.Bin + Path.PathSeparator + originalPath);
                var approved = RehearsalApprovals(root);
                judged.Approve(approval is null ? approved : approval(approved));
            },
            table,
            LauncherId,
            admission,
            neverPin ? null : judged,
            objective,
            gitVersions,
            handler);

        QualificationReport report;
        try
        {
            await using (environment)
            {
                report = await new QualificationSession(
                        Ledger, decorate is null ? environment : decorate(environment), RehearsalLimits, "profile01-rehearsal")
                    .RunAsync(CancellationToken.None);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }

        return new Rehearsal(report, SafeSummary.Render(report, environment.ForbiddenFragments), counters, environment.OwnedRoot);
    }

    private static TargetJudgement AcceptOnlyOwnedDoubles(string root, IReadOnlyList<ProviderTargetFact> facts)
    {
        var bin = Path.Combine(root, "bin");
        var reports = new[] { (Capability.CodexCli, "codex", "codex.exe"), (Capability.ClaudeCli, "claude", "claude.exe") }
            .Select(expected =>
            {
                var fact = facts.FirstOrDefault(candidate => candidate.Capability == expected.Item1);
                var owned = fact is { Reason: CapabilityProbeReason.None, LaunchKind: CapabilityLaunchKind.DirectExecutable }
                    && string.Equals(fact.ExecutablePath, Path.Combine(bin, expected.Item3), StringComparison.OrdinalIgnoreCase);
                return new TargetReport(expected.Item2, owned, owned ? "RehearsalDouble" : "NotTheOwnedDouble", fact?.LaunchKind?.ToString(), owned ? fact!.Version : null);
            })
            .ToArray();
        return new TargetJudgement(reports.All(report => report.Real), reports);
    }

    private sealed class Rehearsal(QualificationReport report, string summary, AdmissionLedger counters, OwnedRootReference? root) : IDisposable
    {
        public QualificationReport Report => report;

        public string Summary => summary;

        public AdmissionLedger Counters => counters;

        public void Dispose()
        {
            // Offline fixture teardown of these rehearsals only: the exact root and token the environment held since creation, never a
            // discovered one, and only after the production host stopped and every process of the rehearsal was a short owned double or a
            // completed version probe. A preserved root of a failing rehearsal would otherwise stay in the temp directory.
            if (root is not null && Directory.Exists(root.Path) && report.Shutdown is { HostStopped: true })
            {
                OwnedRootCleaner.Remove(root.Path, root.OwnerToken);
            }
        }
    }
}
