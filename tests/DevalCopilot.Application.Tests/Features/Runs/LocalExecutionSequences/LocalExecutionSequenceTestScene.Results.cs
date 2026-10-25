using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;
using DevalCopilot.Application.Features.Projects.Commands.RecordLocalExecutionSequenceVerificationDispatchRefusal;
using DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionResult;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordLocalExecutionSequenceDispatchRefusal;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationReviewResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordReviewCorrectionResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordVerificationDiagnosisResult;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// Dispatch markers and result recording of a claimed operation. Each goes through the REAL production handler the supervisors send
/// (the dispatch marker handlers and the Record*Result handlers), over a context of its own, so the durable shapes (Implemented attempt,
/// ExecutionReport, result checkpoint, clean-exit evidence, review approval or findings, diagnosis findings or escalation, correction
/// RevisionResponses and report chain, verification result and sealed outputs) are exactly the production ones.
/// </summary>
internal sealed partial class LocalExecutionSequenceTestScene
{
    private static readonly AgentProcessEvidence FailedExit = new(ProcessExecutionOutcome.Exited, 1, TimeSpan.FromMilliseconds(900));

    /// <summary>The real Agent dispatch marker (one of the four Agent supervisors' first write).</summary>
    public async Task<Result<MarkAgentAttemptDispatchedCommandResult>> DispatchAgentAsync(Guid attemptId)
    {
        // The implementation supervisor projects the Human plan authorization its attempt consumed (ADR-0016) and the dispatch marker
        // requires it exactly: the same fresh classification the feed performs.
        await using var read = Fixture.CreateContext();
        var fact = PlanningImplementationAuthorizationAttempt.ToFact(
            await PlanningImplementationAuthorizationAttempt.ClassifyFreshAsync(read, RunId, attemptId, CancellationToken.None));
        var dispatched = await PipelineAsync(context => new MarkAgentAttemptDispatchedCommandHandler(context, Clock, HostLaunch).HandleAsync(
            new MarkAgentAttemptDispatchedCommand(RunId, attemptId, null, fact), CancellationToken.None));
        if (dispatched.IsSuccess)
        {
            RememberTicket(attemptId, dispatched.Value.Ticket);
        }

        return dispatched;
    }

    /// <summary>The EF transaction behavior every plain <c>ICommand</c> gets in the host: a transaction is opened before the handler, and
    /// the save and commit follow a successful result (a failure rolls back).</summary>
    public async Task<Result<T>> PipelineAsync<T>(Func<DevalCopilot.Infrastructure.Persistence.DevalCopilotDbContext, Task<Result<T>>> handle)
    {
        await using var context = Fixture.CreateContext();
        await using var transaction = await context.BeginTransactionAsync(CancellationToken.None);
        var result = await handle(context);
        if (result.IsFailure)
        {
            await transaction.RollbackAsync();
            return result;
        }

        await context.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync();
        return result;
    }

    /// <summary>The same transaction behavior for a plain command that returns a bare <see cref="Result"/>.</summary>
    public async Task<Result> PipelineAsync(Func<DevalCopilot.Infrastructure.Persistence.DevalCopilotDbContext, Task<Result>> handle)
    {
        await using var context = Fixture.CreateContext();
        await using var transaction = await context.BeginTransactionAsync(CancellationToken.None);
        var result = await handle(context);
        if (result.IsFailure)
        {
            await transaction.RollbackAsync();
            return result;
        }

        await context.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync();
        return result;
    }

    /// <summary>The real verification dispatch marker.</summary>
    public async Task<Result> DispatchVerificationAsync(Guid executionId)
    {
        var execution = await ReadExecutionAsync(executionId);
        await using var context = Fixture.CreateContext();
        var dispatched = await new MarkVerificationExecutionDispatchedCommandHandler(context, Clock, HostLaunch).HandleAsync(
            new MarkVerificationExecutionDispatchedCommand(executionId, VerificationDispatchSnapshot.Of(execution)), CancellationToken.None);
        if (dispatched.IsSuccess)
        {
            RememberTicket(executionId, dispatched.Value.Ticket);
        }

        return dispatched.IsSuccess ? Result.Success() : Result.Failure(dispatched.Errors[0]);
    }

    /// <summary>The real dedicated Agent dispatch-refusal recorder a supervisor sends after a refused dispatch marker.</summary>
    public async Task<Result> RecordAgentDispatchRefusalAsync(Guid attemptId)
    {
        await using var context = Fixture.CreateContext();
        return await new RecordLocalExecutionSequenceDispatchRefusalCommandHandler(context, Clock).HandleAsync(
            new RecordLocalExecutionSequenceDispatchRefusalCommand(RunId, attemptId), CancellationToken.None);
    }

    /// <summary>The real dedicated verification dispatch-refusal recorder.</summary>
    public async Task<Result> RecordVerificationDispatchRefusalAsync(Guid executionId)
    {
        await using var context = Fixture.CreateContext();
        return await new RecordLocalExecutionSequenceVerificationDispatchRefusalCommandHandler(context, Clock).HandleAsync(
            new RecordLocalExecutionSequenceVerificationDispatchRefusalCommand(ProjectId, executionId), CancellationToken.None);
    }

    /// <summary>A mediator over the scene's fakes whose composed claims run on contexts of the given factory (a fault-injecting
    /// decorator, for instance).</summary>
    public LocalExecutionTestMediator MediatorWith(Func<DevalCopilot.Application.Data.IDevalCopilotDbContext> contextFactory) =>
        new(contextFactory, Evidence, Artifacts, Clock, AttemptProbe, StepProbe);

    /// <summary>Dispatches (asserting success) either kind of operation by its step kind.</summary>
    public async Task DispatchAsync(LocalExecutionStepKind kind, Guid operationId)
    {
        if (kind == LocalExecutionStepKind.Verification)
        {
            var verification = await DispatchVerificationAsync(operationId);
            Assert.True(verification.IsSuccess, verification.IsFailure ? verification.Errors[0].Code : null);
            return;
        }

        var agent = await DispatchAgentAsync(operationId);
        Assert.True(agent.IsSuccess, agent.IsFailure ? agent.Errors[0].Code : null);
    }

    /// <summary>The recorded outcome of an implementation, review, diagnosis or correction attempt, or verification execution.</summary>
    public async Task RecordAsync(LocalExecutionStepContext step, ScriptedResultKind kind)
    {
        switch (step.Kind)
        {
            case LocalExecutionStepKind.Implementation:
                await RecordImplementationAsync(step.OperationId, kind);
                break;
            case LocalExecutionStepKind.ReviewCorrection:
            case LocalExecutionStepKind.DiagnosisCorrection:
                await RecordCorrectionAsync(step.OperationId, kind);
                break;
            case LocalExecutionStepKind.Verification:
                await RecordVerificationAsync(step.OperationId, kind);
                break;
            case LocalExecutionStepKind.CodeReview:
                await RecordReviewAsync(step.OperationId, kind, step.FindingCount);
                break;
            default:
                await RecordDiagnosisAsync(step.OperationId, kind, step.FindingCount);
                break;
        }
    }

    public async Task RecordImplementationAsync(Guid attemptId, ScriptedResultKind kind)
    {
        var attempt = await ReadAttemptAsync(attemptId);
        var succeeded = kind is ScriptedResultKind.Implemented or ScriptedResultKind.InvalidOutput;
        var changed = kind == ScriptedResultKind.Implemented;
        var fingerprint = changed ? NextFingerprint() : attempt.AgentCheckpointFingerprintSha256!;
        var paths = PathsFor(changed, attempt.AttemptNumber);
        var command = new RecordImplementationResultCommand(
            RunId, attemptId, succeeded, Head, fingerprint, paths, [],
            changed ? Report(paths) : null, null, null, null, EvidenceFor(kind))
        { DispatchTicket = TicketOf(attemptId) };
        var result = await PipelineAsync(context => new RecordImplementationResultCommandHandler(context, Clock).HandleAsync(command, CancellationToken.None));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        if (changed)
        {
            Evidence.Fingerprint = fingerprint;
        }
    }

    public async Task RecordCorrectionAsync(Guid attemptId, ScriptedResultKind kind)
    {
        var attempt = await ReadAttemptAsync(attemptId);
        var inputs = await ReadInputsAsync(attemptId);
        var succeeded = kind is ScriptedResultKind.CorrectionApplied or ScriptedResultKind.InvalidOutput;
        var changed = kind == ScriptedResultKind.CorrectionApplied;
        var fingerprint = changed ? NextFingerprint() : attempt.AgentCheckpointFingerprintSha256!;
        var paths = PathsFor(changed, attempt.AttemptNumber);
        var correction = changed
            ? ValidatedReviewCorrection.Create(
                inputs.Skip(1).Select((finding, index) => new ValidatedRevisionResponse(
                    finding, "Addressed", $"The change for finding {index + 1} was made.", $"Edited the source for finding {index + 1}.")).ToArray(),
                Report(paths))
            : null;
        var command = new RecordReviewCorrectionResultCommand(
            RunId, attemptId, succeeded, Head, fingerprint, paths, [], correction, null, EvidenceFor(kind))
        { DispatchTicket = TicketOf(attemptId) };
        var result = await PipelineAsync(context => new RecordReviewCorrectionResultCommandHandler(context, Clock).HandleAsync(command, CancellationToken.None));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        if (changed)
        {
            Evidence.Fingerprint = fingerprint;
        }
    }

    public async Task RecordReviewAsync(Guid attemptId, ScriptedResultKind kind, int findings)
    {
        var attempt = await ReadAttemptAsync(attemptId);
        var (outcome, review, evidence) = kind switch
        {
            ScriptedResultKind.ReviewApproved => (
                AgentOutcome.ReviewApproved,
                ValidatedImplementationReview.CreateApproved("The implementation is approved.", "It satisfies the plan.", "No residual risk."),
                TestProcessEvidence.ReportedCleanExit),
            ScriptedResultKind.ReviewChangesRequested => (
                AgentOutcome.ReviewChangesRequested,
                ValidatedImplementationReview.CreateChangesRequested("Changes are required.", Findings(findings)),
                TestProcessEvidence.ReportedCleanExit),
            ScriptedResultKind.InvalidOutput => (AgentOutcome.InvalidStructuredOutput, (ValidatedImplementationReview?)null, TestProcessEvidence.ReportedCleanExit),
            ScriptedResultKind.ProviderFailedNoExit => (AgentOutcome.ProviderInvocationFailed, null, (AgentProcessEvidence?)null),
            ScriptedResultKind.ProviderTimedOutTerminated or ScriptedResultKind.ProviderCancelledTerminated
                or ScriptedResultKind.ProviderTimedOutUnobserved => (AgentOutcome.ProviderInvocationFailed, null, EvidenceFor(kind)),
            _ => (AgentOutcome.ProviderInvocationFailed, null, FailedExit),
        };
        var result = await PipelineAsync(context => new RecordImplementationReviewResultCommandHandler(context, Clock).HandleAsync(
            new RecordImplementationReviewResultCommand(
                RunId, attemptId, outcome, attempt.AgentCheckpointFingerprintSha256, [], review, null, evidence)
            { DispatchTicket = TicketOf(attemptId) },
            CancellationToken.None));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
    }

    public async Task RecordDiagnosisAsync(Guid attemptId, ScriptedResultKind kind, int findings)
    {
        var attempt = await ReadAttemptAsync(attemptId);
        var (outcome, diagnosis, evidence) = kind switch
        {
            ScriptedResultKind.DiagnosisFindings => (
                AgentOutcome.DiagnosisFindingsRecorded,
                ValidatedVerificationDiagnosis.CreateFindings("The failing recipe shows a defect.", Findings(findings)),
                TestProcessEvidence.ReportedCleanExit),
            ScriptedResultKind.DiagnosisEscalated => (
                AgentOutcome.DiagnosisEscalated,
                ValidatedVerificationDiagnosis.CreateEscalation(
                    "The failure needs a human decision.",
                    new ValidatedDiagnosisEscalation(
                        "Decide whether the local tool version may change.", "Keep or change the tool.",
                        "The verification keeps failing.", "The same failure repeats on every run.", "Change the tool.")),
                TestProcessEvidence.ReportedCleanExit),
            ScriptedResultKind.InvalidOutput => (AgentOutcome.InvalidStructuredOutput, (ValidatedVerificationDiagnosis?)null, TestProcessEvidence.ReportedCleanExit),
            ScriptedResultKind.ProviderFailedNoExit => (AgentOutcome.ProviderInvocationFailed, null, (AgentProcessEvidence?)null),
            ScriptedResultKind.ProviderTimedOutTerminated or ScriptedResultKind.ProviderCancelledTerminated
                or ScriptedResultKind.ProviderTimedOutUnobserved => (AgentOutcome.ProviderInvocationFailed, null, EvidenceFor(kind)),
            _ => (AgentOutcome.ProviderInvocationFailed, null, FailedExit),
        };
        var result = await PipelineAsync(context => new RecordVerificationDiagnosisResultCommandHandler(context, Clock).HandleAsync(
            new RecordVerificationDiagnosisResultCommand(
                RunId, attemptId, outcome, attempt.AgentCheckpointFingerprintSha256, [], diagnosis, null, evidence)
            { DispatchTicket = TicketOf(attemptId) },
            CancellationToken.None));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
    }

    /// <summary>Records a verification result of the claimed, dispatched execution through the real recorder. A failed result seals
    /// real stdout and stderr (served back by <see cref="Artifacts"/>) exactly as the verification supervisor leaves them.</summary>
    public async Task RecordVerificationAsync(Guid executionId, ScriptedResultKind kind)
    {
        var execution = await ReadExecutionAsync(executionId);
        var (outcome, exitCode) = kind switch
        {
            ScriptedResultKind.VerificationPassed => (VerificationExecutionOutcome.Exited, (int?)0),
            ScriptedResultKind.VerificationFailed => (VerificationExecutionOutcome.Exited, 1),
            ScriptedResultKind.VerificationTimedOut or ScriptedResultKind.VerificationTimedOutTerminated => (VerificationExecutionOutcome.TimedOut, null),
            ScriptedResultKind.VerificationCancelledTerminated => (VerificationExecutionOutcome.Cancelled, null),
            ScriptedResultKind.VerificationSourceChanged => (VerificationExecutionOutcome.Exited, (int?)0),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a verification result."),
        };
        var sealedOutputs = new List<SealedVerificationOutputArtifact>();
        if (kind == ScriptedResultKind.VerificationFailed)
        {
            var stdout = Artifacts.RegisterSealedOutput("stdout failure text"u8.ToArray());
            var stderr = Artifacts.RegisterSealedOutput("stderr failure text"u8.ToArray());
            sealedOutputs.Add(new SealedVerificationOutputArtifact(VerificationOutputPurpose.StandardOutput, stdout.Path, stdout.Length, stdout.Hash, false));
            sealedOutputs.Add(new SealedVerificationOutputArtifact(VerificationOutputPurpose.StandardError, stderr.Path, stderr.Length, stderr.Hash, false));
        }

        var result = await PipelineAsync(context => new RecordVerificationExecutionResultCommandHandler(context, Clock).HandleAsync(
            new RecordVerificationExecutionResultCommand(
                executionId, outcome, exitCode,
                kind == ScriptedResultKind.VerificationSourceChanged ? new string('e', 64) : execution.CheckpointFingerprintSha256, sealedOutputs)
            {
                DispatchTicket = TicketOf(executionId),
                Termination = kind is ScriptedResultKind.VerificationTimedOutTerminated or ScriptedResultKind.VerificationCancelledTerminated
                    ? TerminatedAfterTimeout
                    : null,
                ProcessDuration = kind is ScriptedResultKind.VerificationTimedOutTerminated or ScriptedResultKind.VerificationCancelledTerminated
                    ? TimeSpan.FromMilliseconds(5000)
                    : null,
            },
            CancellationToken.None));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
    }

    private static IReadOnlyList<GitWorkspaceChangedPath> PathsFor(bool changed, int attemptNumber) =>
        changed ? [new GitWorkspaceChangedPath($"src/Ledger{attemptNumber}.cs", null, " ", "M")] : [];

    private static ValidatedImplementationReport Report(IReadOnlyList<GitWorkspaceChangedPath> paths) => ValidatedImplementationReport.Create(
        "Added the ledger table and its query.", paths.Select(path => path.Path).ToArray(), "Added the table and the query.", string.Empty,
        string.Empty, "dotnet test");

    private static IReadOnlyList<ValidatedReviewFinding> Findings(int count) => Enumerable.Range(1, count)
        .Select(index => new ValidatedReviewFinding(
            "high", "correctness", $"Finding {index} summary.", $"The evidence {index} shows the filter is absent.",
            $"Add the filter {index}.", null))
        .ToArray();

    /// <summary>The physical termination fact the original host observed after a timeout or cancellation request: the exit code the
    /// operating system reported for the killed process and the elapsed time at the successful wait.</summary>
    internal static readonly ProcessTerminationObservation TerminatedAfterTimeout = new(137, TimeSpan.FromMilliseconds(4900));

    private static AgentProcessEvidence? EvidenceFor(ScriptedResultKind kind) => kind switch
    {
        ScriptedResultKind.ProviderFailedNoExit => null,
        ScriptedResultKind.ProviderTimedOutTerminated =>
            new AgentProcessEvidence(ProcessExecutionOutcome.TimedOut, null, TimeSpan.FromMilliseconds(5000)) { Termination = TerminatedAfterTimeout },
        ScriptedResultKind.ProviderCancelledTerminated =>
            new AgentProcessEvidence(ProcessExecutionOutcome.Cancelled, null, TimeSpan.FromMilliseconds(5000)) { Termination = TerminatedAfterTimeout },
        ScriptedResultKind.ProviderTimedOutUnobserved => new AgentProcessEvidence(ProcessExecutionOutcome.TimedOut, null, TimeSpan.FromMilliseconds(5000)),
        ScriptedResultKind.ProviderFailedWithExit => FailedExit,
        _ => TestProcessEvidence.ReportedCleanExit,
    };
}
