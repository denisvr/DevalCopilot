using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static DevalCopilot.Application.Tests.Features.Runs.RepairTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// A real, file-backed run for the verification-diagnosis tests (ADR-0018): the <see cref="RepairTestScene"/> run, workspace,
/// lease and capabilities, a completed initial or corrected Implementer result with its ExecutionReport, and a chosen set of
/// enabled verification commands whose latest executions are bound to the review checkpoint — each Passed, Failed (with
/// sealed stdout/stderr served by <see cref="DiagnosisArtifactStore"/>), or one of the non-diagnosable terminal or running
/// states. Every executable path and argument is a distinctive sentinel so a leak into a manifest is detectable. Builders
/// also seed completed diagnosis attempts exactly as production records them. Nothing is hidden: tests change the world with
/// independent contexts, exactly as a concurrent request would.
/// </summary>
internal sealed class DiagnosisTestScene
{
    public const string ExecutableSentinel = "sentinel-tool-path";
    public const string ArgumentSentinel = "--sentinel-argument";

    public enum Kind
    {
        Passed,
        Failed,
        TimedOut,
        Cancelled,
        Interrupted,
        SourceChanged,
        StartFailure,
        Running,
    }

    /// <summary>One enabled-or-disabled command and the latest execution it gets for the review checkpoint.</summary>
    public sealed record Spec(
        Kind Kind,
        bool Enabled = true,
        int ExitCode = 1,
        byte[]? Stdout = null,
        byte[]? Stderr = null,
        bool? Truncated = false,
        VerificationOutputCaptureOutcome Capture = VerificationOutputCaptureOutcome.CapturedWithKnownTruncation,
        bool WithOutputs = true,
        string? Name = null)
    {
        public static Spec Passed(bool enabled = true) => new(Kind.Passed, enabled);

        public static Spec Failed(string? stdout = null, string? stderr = null) => new(
            Kind.Failed, Stdout: Encoding.UTF8.GetBytes(stdout ?? "stdout failure text"), Stderr: Encoding.UTF8.GetBytes(stderr ?? "stderr failure text"));
    }

    private DiagnosisTestScene(
        RepairTestScene scene, ImplementationScene implementation, DiagnosisArtifactStore store, List<VerificationCommand> commands,
        List<VerificationExecution> executions)
    {
        Scene = scene;
        Implementation = implementation;
        Store = store;
        Commands = commands;
        Executions = executions;
    }

    public RepairTestScene Scene { get; }

    public ImplementationScene Implementation { get; }

    public DiagnosisArtifactStore Store { get; }

    public List<VerificationCommand> Commands { get; }

    /// <summary>The seeded executions, parallel to <see cref="Commands"/> (null where a spec seeded none).</summary>
    public List<VerificationExecution> Executions { get; }

    public SqliteDatabaseFixture Fixture => Scene.Fixture;

    public DevalCopilotDbContext Db => Scene.Db;

    public Run Run => Scene.Run;

    public Guid ReportId => Implementation.ExecutionReport.Id;

    public static async Task<DiagnosisTestScene> CreateAsync(
        SqliteDatabaseFixture fixture,
        IReadOnlyList<Spec>? specs = null,
        PlanForm form = PlanForm.AcceptedRoot,
        bool corrected = false,
        int maximumAgentAttempts = 16,
        TimeSpan? maximumAgentInvocationTime = null)
    {
        var scene = await RepairTestScene.CreateAsync(fixture, maximumAgentAttempts, maximumAgentInvocationTime);
        var implementation = corrected ? scene.AddCorrectedImplementation(form) : scene.AddInitialImplementation(form);
        await scene.SaveAsync();
        // The scene numbers its checkpoints by hand; the workspace's own counter must agree so a later recorded result checkpoint
        // gets the next number.
        int checkpointCount;
        await using (var count = fixture.CreateContext())
        {
            checkpointCount = await count.GitCheckpoints.CountAsync(c => c.WorkspaceId == scene.Workspace.Id);
        }

        for (var reserved = 0; reserved < checkpointCount; reserved++)
        {
            scene.Workspace.ReserveCheckpointNumber();
        }

        await scene.SaveAsync();
        var diagnosis = new DiagnosisTestScene(scene, implementation, new DiagnosisArtifactStore(), [], []);
        await diagnosis.SeedVerificationAsync(specs ?? [Spec.Passed(), Spec.Failed()]);
        return diagnosis;
    }

    public async Task SeedVerificationAsync(IReadOnlyList<Spec> specs)
    {
        for (var index = 0; index < specs.Count; index++)
        {
            var spec = specs[index];
            var command = VerificationCommand.Configure(
                Guid.NewGuid(), Run.ProjectId, Commands.Count + 1, spec.Name ?? $"Verification {Commands.Count + 1}",
                $@"C:\{ExecutableSentinel}-{Commands.Count + 1}\runner.exe", [$"{ArgumentSentinel}-{Commands.Count + 1}"], 300, spec.Enabled, Now);
            Db.VerificationCommands.Add(command);
            Commands.Add(command);
        }

        await Db.SaveChangesAsync(CancellationToken.None);
        var firstNew = Commands.Count - specs.Count;
        for (var index = 0; index < specs.Count; index++)
        {
            var execution = BuildExecution(
                Db, Commands[firstNew + index], specs[index], Implementation.ReviewCheckpoint, NextExecutionNumber(Db), Store);
            Executions.Add(execution);
            await Db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static int NextExecutionNumber(DevalCopilotDbContext db) =>
        db.VerificationExecutions.Local.Select(e => e.ExecutionNumber).DefaultIfEmpty(0).Max() + 1;

    private int _independentNumber;

    /// <summary>Builds one execution (and, for a failed one with outputs, its two sealed rows) and adds it to
    /// <paramref name="db"/>. Does not save.</summary>
    private static VerificationExecution BuildExecution(
        DevalCopilotDbContext db, VerificationCommand command, Spec spec, GitCheckpoint checkpoint, int number, DiagnosisArtifactStore store)
    {
        var workspace = db.GitWorkspaces.Local.FirstOrDefault(w => w.Id == checkpoint.WorkspaceId)
            ?? db.GitWorkspaces.AsNoTracking().Single(w => w.Id == checkpoint.WorkspaceId);
        var execution = VerificationExecution.Claim(Guid.NewGuid(), command.ProjectId, number, workspace, checkpoint, command, Now);
        var fingerprint = checkpoint.FingerprintSha256;
        switch (spec.Kind)
        {
            case Kind.Passed:
                execution.MarkDispatched(Now);
                execution.Complete(VerificationExecutionOutcome.Exited, 0, fingerprint, Now);
                break;
            case Kind.Failed:
                execution.MarkDispatched(Now);
                execution.Complete(VerificationExecutionOutcome.Exited, spec.ExitCode, fingerprint, Now);
                break;
            case Kind.TimedOut:
                execution.MarkDispatched(Now);
                execution.Complete(VerificationExecutionOutcome.TimedOut, null, fingerprint, Now);
                break;
            case Kind.Cancelled:
                execution.MarkDispatched(Now);
                execution.Complete(VerificationExecutionOutcome.Cancelled, null, fingerprint, Now);
                break;
            case Kind.Interrupted:
                execution.MarkDispatched(Now);
                execution.Interrupt(Now);
                break;
            case Kind.SourceChanged:
                execution.MarkDispatched(Now);
                execution.Complete(VerificationExecutionOutcome.Exited, 0, new string('f', 64), Now);
                break;
            case Kind.StartFailure:
                execution.MarkDispatched(Now);
                execution.Complete(VerificationExecutionOutcome.Failed, null, fingerprint, Now);
                break;
            case Kind.Running:
                execution.MarkDispatched(Now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(spec));
        }

        db.VerificationExecutions.Add(execution);
        if (spec.WithOutputs && spec.Kind is Kind.Failed or Kind.TimedOut or Kind.Cancelled or Kind.StartFailure)
        {
            AddOutputs(db, store, execution, spec);
        }

        return execution;
    }

    private static void AddOutputs(DevalCopilotDbContext db, DiagnosisArtifactStore store, VerificationExecution execution, Spec spec)
    {
        foreach (var (purpose, bytes) in new[]
        {
            (VerificationOutputPurpose.StandardOutput, spec.Stdout ?? Encoding.UTF8.GetBytes("stdout failure text")),
            (VerificationOutputPurpose.StandardError, spec.Stderr ?? Encoding.UTF8.GetBytes("stderr failure text")),
        })
        {
            var registered = store.RegisterSealedOutput(bytes);
            db.VerificationOutputArtifacts.Add(VerificationOutputArtifact.Record(
                Guid.NewGuid(), execution.Id, purpose, registered.Path, registered.Hash, registered.Length,
                spec.Capture == VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption ? null : spec.Truncated,
                spec.Capture, Now));
        }
    }

    /// <summary>Adds, through an independent context (a concurrent commit), a new latest execution for the command at index
    /// <paramref name="commandIndex"/>, bound to <paramref name="checkpoint"/> (the review checkpoint by default).</summary>
    public async Task<VerificationExecution> AddExecutionAsync(
        int commandIndex, Spec spec, GitCheckpoint? checkpoint = null)
    {
        await using var other = Fixture.CreateContext();
        var number = (await other.VerificationExecutions.AsNoTracking().MaxAsync(e => (int?)e.ExecutionNumber) ?? 0) + 1;
        var command = await other.VerificationCommands.AsNoTracking().SingleAsync(c => c.Id == Commands[commandIndex].Id);
        var target = checkpoint ?? Implementation.ReviewCheckpoint;
        var execution = BuildExecution(other, command, spec, target, number, Store);
        await other.SaveChangesAsync(CancellationToken.None);
        _independentNumber = number;
        return execution;
    }

    /// <summary>Sets a command's enabled flag through an independent context.</summary>
    public async Task SetEnabledAsync(int commandIndex, bool enabled)
    {
        await using var other = Fixture.CreateContext();
        var command = await other.VerificationCommands.SingleAsync(c => c.Id == Commands[commandIndex].Id);
        command.Update(command.Name, command.ExecutablePath, command.Arguments.ToArray(), command.TimeoutSeconds, enabled, Now);
        await other.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Adds a new enabled command (without an execution) through an independent context.</summary>
    public async Task<VerificationCommand> AddEnabledCommandAsync()
    {
        await using var other = Fixture.CreateContext();
        var number = (await other.VerificationCommands.AsNoTracking().MaxAsync(c => (int?)c.CommandNumber) ?? 0) + 1;
        var command = VerificationCommand.Configure(
            Guid.NewGuid(), Run.ProjectId, number, $"Late verification {number}", $@"C:\{ExecutableSentinel}-late\runner.exe", ["late"], 300, true, Now);
        other.VerificationCommands.Add(command);
        await other.SaveChangesAsync(CancellationToken.None);
        return command;
    }

    /// <summary>Adds a new (current) checkpoint through an independent context — the workspace moved on.</summary>
    public async Task<GitCheckpoint> AddCheckpointAsync()
    {
        await using var other = Fixture.CreateContext();
        var number = await other.GitCheckpoints.AsNoTracking().Where(c => c.WorkspaceId == Scene.Workspace.Id).MaxAsync(c => c.CheckpointNumber) + 1;
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), Scene.Workspace.Id, number, Now, new string('d', 40), new string('d', 64), []);
        other.GitCheckpoints.Add(checkpoint);
        await other.SaveChangesAsync(CancellationToken.None);
        return checkpoint;
    }

    /// <summary>Releases the active lease through an independent context.</summary>
    public async Task ReleaseLeaseAsync()
    {
        await using var other = Fixture.CreateContext();
        var lease = await other.RepositoryMutationLeases.SingleAsync(l => l.WorkspaceId == Scene.Workspace.Id && l.Status == LeaseStatus.Active);
        lease.Release(Now);
        await other.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Writes one column of the diagnosed report's row directly (a tampered or foreign report).</summary>
    public async Task ReplyReportToAsync(Guid parentMessageId)
    {
        await using var other = Fixture.CreateContext();
        await other.CollaborationMessages.Where(m => m.Id == ReportId)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.InReplyToMessageId, (Guid?)parentMessageId));
    }

    public RepairEvidenceReader Reader() => RepairEvidenceReader.Matching(Implementation.ReviewFingerprint);

    public CreateVerificationDiagnosisAttemptCommandHandler ClaimHandler(
        IDevalCopilotDbContext? db = null, RepairEvidenceReader? reader = null, DiagnosisArtifactStore? store = null,
        IAttemptDurabilityProbe? probe = null) =>
        new(db ?? Db, reader ?? Reader(), store ?? Store, new FixedTimeProvider(Now), probe ?? new AttemptDurabilityProbe(Fixture.Options));

    /// <summary>Runs the claim on <paramref name="db"/> or, by default, on a fresh context that sees the database as it is now.</summary>
    public async Task<Devalente.Shared.Results.Result<CreateVerificationDiagnosisAttemptCommandResult>> ClaimAsync(
        IDevalCopilotDbContext? db = null, RepairEvidenceReader? reader = null, DiagnosisArtifactStore? store = null,
        Guid? reportId = null, Guid? runId = null)
    {
        await using var fresh = Fixture.CreateContext();
        return await ClaimHandler(db ?? fresh, reader, store).HandleAsync(
            new CreateVerificationDiagnosisAttemptCommand(runId ?? Run.Id, reportId ?? ReportId), CancellationToken.None);
    }

    /// <summary>Executes test-owned SQL through an independent context (a concurrent writer).</summary>
    public async Task SqlAsync(string sql, params object[] parameters)
    {
        await using var other = Fixture.CreateContext();
#pragma warning disable EF1002
        await other.Database.ExecuteSqlRawAsync(sql, parameters);
#pragma warning restore EF1002
    }

    /// <summary>Row counts that a refused claim must leave unchanged.</summary>
    public async Task<(int Attempts, int Artifacts, int Inputs, int Evidence, int Messages, int Events)> CountsAsync()
    {
        await using var other = Fixture.CreateContext();
        return (
            await other.Attempts.CountAsync(), await other.Artifacts.CountAsync(), await other.AttemptInputMessages.CountAsync(),
            await other.AttemptVerificationEvidence.CountAsync(), await other.CollaborationMessages.CountAsync(), await other.Events.CountAsync());
    }

    /// <summary>The diagnosis-origin correction handler on <paramref name="db"/> or a fresh context.</summary>
    public CreateDiagnosisCorrectionAttemptCommandHandler CorrectionHandler(
        IDevalCopilotDbContext db, RepairEvidenceReader? reader = null, DiagnosisArtifactStore? store = null, IRunEventNotifier? notifier = null,
        IAttemptDurabilityProbe? probe = null) =>
        new(db, reader ?? Reader(), store ?? Store, new FixedTimeProvider(Now), probe ?? new AttemptDurabilityProbe(Fixture.Options), notifier);

    public async Task<Devalente.Shared.Results.Result<CreateDiagnosisCorrectionAttemptCommandResult>> ClaimCorrectionAsync(
        Guid diagnosisAttemptId, IDevalCopilotDbContext? db = null, RepairEvidenceReader? reader = null, DiagnosisArtifactStore? store = null,
        IRunEventNotifier? notifier = null, Guid? runId = null, string? guidance = null)
    {
        await using var fresh = Fixture.CreateContext();
        return await CorrectionHandler(db ?? fresh, reader, store, notifier).HandleAsync(
            new CreateDiagnosisCorrectionAttemptCommand(runId ?? Run.Id, diagnosisAttemptId, guidance), CancellationToken.None);
    }

    // ---- seeded diagnosis attempts -------------------------------------------------------------------------------

    /// <summary>The production snapshot digest of an arbitrary persisted (command, execution) pair, as a claim would pin it.</summary>
    internal static async Task<string> SnapshotAsync(IDevalCopilotDbContext db, Guid commandId, Guid executionId)
    {
        var command = await db.VerificationCommands.AsNoTracking().SingleAsync(candidate => candidate.Id == commandId);
        var execution = await db.VerificationExecutions.AsNoTracking().SingleAsync(candidate => candidate.Id == executionId);
        var rows = await db.VerificationOutputArtifacts.AsNoTracking().Where(output => output.VerificationExecutionId == executionId).ToListAsync();
        VerificationDiagnosisEvidence.Output? Pick(VerificationOutputPurpose purpose) => rows.Where(row => row.Purpose == purpose)
            .Select(row => new VerificationDiagnosisEvidence.Output(row.Id, row.RelativeStoragePath, row.ByteLength, row.ContentHash, row.Truncated, row.CaptureOutcome))
            .SingleOrDefault();
        var failed = execution.Status == VerificationExecutionStatus.Failed;
        return VerificationDiagnosisSnapshot.Compute(new VerificationDiagnosisEvidence.Entry(
            command, execution, failed, failed ? Pick(VerificationOutputPurpose.StandardOutput) : null, failed ? Pick(VerificationOutputPurpose.StandardError) : null));
    }

    /// <summary>The ordered (command, execution) pairs of the current selection: each enabled command with its latest
    /// review-checkpoint execution, in command order — what a claim pins.</summary>
    public async Task<List<(Guid CommandId, Guid ExecutionId)>> CurrentPairsAsync()
    {
        await using var other = Fixture.CreateContext();
        var commands = await other.VerificationCommands.AsNoTracking().Where(c => c.ProjectId == Run.ProjectId && c.IsEnabled).OrderBy(c => c.CommandNumber).ToListAsync();
        var checkpointId = Implementation.ReviewCheckpoint.Id;
        var executions = await other.VerificationExecutions.AsNoTracking().Where(e => e.GitCheckpointId == checkpointId).ToListAsync();
        return commands
            .Select(c => (c.Id, executions.Where(e => e.VerificationCommandId == c.Id).OrderByDescending(e => e.ExecutionNumber).First().Id))
            .ToList();
    }

    public sealed record SeededDiagnosis(Attempt Attempt, IReadOnlyList<CollaborationMessage> Messages);

    /// <summary>A diagnosis attempt of the current report exactly as production leaves it: claimed, then — for an outcome —
    /// dispatched and completed with clean-exit evidence, its sequence-0 report input, its pinned ordered selection, and (for
    /// findings or an escalation) its messages replying to the report. Saves.</summary>
    public async Task<SeededDiagnosis> AddDiagnosisAsync(
        AgentOutcome? outcome, int findingCount = 2, IReadOnlyList<(Guid CommandId, Guid ExecutionId)>? pairs = null,
        bool dispatched = true, string? fingerprint = null, Guid? checkpointId = null)
    {
        var number = await NextAttemptNumberAsync();
        var attemptFingerprint = fingerprint ?? Implementation.ReviewFingerprint;
        var attempt = Attempt.ClaimAgentVerificationDiagnosis(
            Guid.NewGuid(), Run.Id, number, Scene.Workspace.Id, checkpointId ?? Implementation.ReviewCheckpoint.Id, attemptFingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now, null, null, number);
        if (dispatched || outcome is not null)
        {
            attempt.MarkAgentDispatched(Now);
        }

        var messages = new List<CollaborationMessage>();
        if (outcome is AgentOutcome.DiagnosisFindingsRecorded or AgentOutcome.DiagnosisEscalated)
        {
            attempt.CompleteAgent(outcome.Value, attemptFingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        }
        else if (outcome is { } other)
        {
            attempt.CompleteAgent(other, outcome == AgentOutcome.InvalidStructuredOutput ? attemptFingerprint : null, Now,
                processEvidence: other is AgentOutcome.InvalidStructuredOutput ? TestProcessEvidence.CleanExit : null);
        }

        Db.Attempts.Add(attempt);
        Db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, ReportId, 0));
        var pinned = pairs ?? await CurrentPairsAsync();
        for (var index = 0; index < pinned.Count; index++)
        {
            Db.AttemptVerificationEvidence.Add(AttemptVerificationEvidence.RecordDiagnosisSnapshot(
                Guid.NewGuid(), attempt.Id, pinned[index].CommandId, pinned[index].ExecutionId, index,
                await SnapshotAsync(Db, pinned[index].CommandId, pinned[index].ExecutionId)));
        }

        if (outcome == AgentOutcome.DiagnosisFindingsRecorded)
        {
            for (var index = 0; index < findingCount; index++)
            {
                var message = CollaborationMessage.RecordAgent(
                    attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
                    CollaborationMessageType.ReviewFinding, ReportId, $"Diagnosis finding {index + 1}.",
                    JsonSerializer.Serialize(new
                    {
                        severity = "high",
                        category = "correctness",
                        evidence = $"The failing assertion {index + 1} shows the filter is absent.",
                        requiredChange = $"Add the filter {index + 1}.",
                    }),
                    Now);
                Db.CollaborationMessages.Add(message);
                messages.Add(message);
                await Db.SaveChangesAsync(CancellationToken.None);
            }
        }
        else if (outcome == AgentOutcome.DiagnosisEscalated)
        {
            var message = CollaborationMessage.RecordAgent(
                attempt, Guid.NewGuid(), ParticipantIdentity.ForHuman(), CollaborationMessageType.Escalation, ReportId,
                "The failure needs a human decision.",
                JsonSerializer.Serialize(new
                {
                    unresolvedDecision = "Decide whether the local tool version may change.",
                    options = "Keep or change the tool.",
                    consequences = "The verification keeps failing.",
                    evidence = "The same failure repeats on every run.",
                    recommendedChoice = "Change the tool.",
                }),
                Now);
            Db.CollaborationMessages.Add(message);
            messages.Add(message);
        }

        await Db.SaveChangesAsync(CancellationToken.None);
        return new SeededDiagnosis(attempt, messages);
    }

    /// <summary>An ordinary Codex implementation review of the current report that requested changes, with findings replying to
    /// the report - the source of an ORDINARY review correction. Saves.</summary>
    public async Task<SeededDiagnosis> AddOrdinaryChangesRequestedReviewAsync(int findingCount = 2)
    {
        var number = await NextAttemptNumberAsync();
        var review = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), Run.Id, number, Scene.Workspace.Id, Implementation.ReviewCheckpoint.Id, Implementation.ReviewFingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now, number);
        review.MarkAgentDispatched(Now);
        review.CompleteAgent(AgentOutcome.ReviewChangesRequested, Implementation.ReviewFingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        Db.Attempts.Add(review);
        Db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), review.Id, ReportId, 0));
        await Db.SaveChangesAsync(CancellationToken.None);
        var messages = new List<CollaborationMessage>();
        for (var index = 0; index < findingCount; index++)
        {
            var message = CollaborationMessage.RecordAgent(
                review, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
                CollaborationMessageType.ReviewFinding, ReportId, $"Review finding {index + 1}.",
                JsonSerializer.Serialize(new
                {
                    severity = "high",
                    category = "correctness",
                    evidence = $"Review evidence {index + 1}.",
                    requiredChange = $"Review change {index + 1}.",
                }),
                Now);
            Db.CollaborationMessages.Add(message);
            messages.Add(message);
            await Db.SaveChangesAsync(CancellationToken.None);
        }

        return new SeededDiagnosis(review, messages);
    }

    public CreateReviewCorrectionAttemptCommandHandler OrdinaryCorrectionHandler(
        IDevalCopilotDbContext db, RepairEvidenceReader? reader = null, DiagnosisArtifactStore? store = null) =>
        new(db, reader ?? Reader(), store ?? Store, new FixedTimeProvider(Now));

    /// <summary>The next attempt number and budget slot, read from the database so attempts a handler claimed are counted.</summary>
    public async Task<int> NextAttemptNumberAsync()
    {
        await SyncAttemptNumbersAsync();
        return Scene.Lineage.ReserveAttemptNumber();
    }

    private async Task SyncAttemptNumbersAsync()
    {
        await using var other = Fixture.CreateContext();
        var next = await other.Attempts.CountAsync(a => a.RunId == Run.Id) + 1;
        while (Scene.Lineage.NextAttemptNumber < next)
        {
            Scene.Lineage.ReserveAttemptNumber();
        }
    }

    /// <summary>An unrelated, valid Planner root Proposal of the run (a foreign plan a tampered report can point at). Saves.</summary>
    public async Task<CollaborationMessage> AddUnrelatedRootAsync()
    {
        await SyncAttemptNumbersAsync();
        var (_, proposal) = Scene.Lineage.AddRoot();
        await Scene.SaveAsync();
        return proposal;
    }

    /// <summary>The ORDINARY extra-correction grant of a review: its escalation (message plus row) and one available human
    /// authorization, exactly the rows the ordinary review-correction authorization flow leaves. Saves.</summary>
    public async Task<(ReviewCorrectionEscalation Escalation, ReviewCorrectionAuthorization Authorization)> AddOrdinaryGrantAsync(Attempt review)
    {
        var escalationMessage = CollaborationMessage.Record(
            Guid.NewGuid(), Run.Id, null, CollaborationMessage.ProtocolVersionOne, ParticipantIdentity.ForOrchestrator(),
            ParticipantIdentity.ForHuman(), CollaborationMessageType.Escalation, ReportId, "Ordinary correction needs a human decision.",
            "{\"unresolvedDecision\":\"a\",\"options\":\"b\",\"consequences\":\"c\",\"evidence\":\"d\",\"recommendedChoice\":\"e\"}",
            CollaborationMessageProvenance.HostConstructed, Now);
        var escalation = ReviewCorrectionEscalation.Record(Guid.NewGuid(), Run.Id, review.Id, escalationMessage.Id, Now);
        var instruction = CollaborationMessage.RecordHumanInstruction(
            Guid.NewGuid(), Run.Id, escalationMessage.Id, ReviewCorrectionGuidance.BuildStructuredContentJson(ReviewCorrectionGuidance.DefaultRationale), Now);
        var authorization = ReviewCorrectionAuthorization.Create(Guid.NewGuid(), Run.Id, escalation.Id, instruction.Id, Now);
        Db.CollaborationMessages.AddRange(escalationMessage, instruction);
        await Db.SaveChangesAsync(CancellationToken.None);
        Db.ReviewCorrectionEscalations.Add(escalation);
        Db.ReviewCorrectionAuthorizations.Add(authorization);
        await Db.SaveChangesAsync(CancellationToken.None);
        return (escalation, authorization);
    }

    /// <summary>History: <paramref name="count"/> completed (failed) ReviewCorrection attempts of the run, each consuming one
    /// slot of the shared correction allowance and of the run-wide agent budget. Saves.</summary>
    public async Task AddCorrectionHistoryAsync(int count)
    {
        for (var index = 0; index < count; index++)
        {
            var number = await NextAttemptNumberAsync();
            var correction = Attempt.ClaimAgentReviewCorrection(
                Guid.NewGuid(), Run.Id, number, Scene.Workspace.Id, Implementation.ReviewCheckpoint.Id, Implementation.ReviewFingerprint,
                Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, Now, number);
            correction.MarkAgentDispatched(Now);
            correction.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
            Db.Attempts.Add(correction);
            await Db.SaveChangesAsync(CancellationToken.None);
        }
    }

    public Task SaveAsync() => Scene.SaveAsync();

    /// <summary>Rewrites stored columns of one execution directly - the only way to persist the contradictory or
    /// malformed rows the Domain itself refuses to record. Test-owned assignment text; the tracked context is untouched.</summary>
    public async Task CorruptExecutionAsync(Guid executionId, string assignments)
    {
        await using var other = Fixture.CreateContext();
#pragma warning disable EF1002
        await other.Database.ExecuteSqlRawAsync($"UPDATE verification_executions SET {assignments} WHERE Id = {{0}}", executionId);
#pragma warning restore EF1002
    }
}
