using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// The one process boundary of a hosted local execution sequence test, serving both process users like production does: a Claude CLI
/// request (it carries standard input) advances the Git evidence stage and answers with the Claude JSON envelope built from the
/// operation actually running (an implementation report, or the correction answering exactly the findings that attempt was given), and
/// a verification request (no standard input) writes its scripted output into the sink paths and reports its scripted outcome. Nothing is
/// ever started; every request is recorded, so a test proves exact invocation counts.
/// </summary>
internal sealed class LocalExecutionSequenceProcessDouble(
    LocalExecutionSequenceStagedEvidence evidence, Func<DevalCopilotDbContext> openContext) : IProcessExecutionAdapter
{
    public const string RecipeArgumentPrefix = "--recipe-";

    private readonly Dictionary<int, int> recipeCalls = [];
    private int order;

    public List<ProcessExecutionRequest> Requests { get; } = [];

    public List<LocalExecutionSequenceVerificationCall> VerificationCalls { get; } = [];

    public List<Guid> ClaudeAttemptIds { get; } = [];

    /// <summary>The behavior of one verification invocation, decided from its recipe number and that recipe's call count.</summary>
    public Func<LocalExecutionSequenceVerificationCall, LocalExecutionSequenceVerificationBehavior> Verification { get; set; } =
        _ => LocalExecutionSequenceVerificationBehavior.Passes;

    /// <summary>Answers the next Claude call with this exact text instead of the computed one (an invalid or foreign answer).</summary>
    public Queue<string> ClaudeAnswers { get; } = new();

    /// <summary>When set, a Claude call waits for it and ignores cancellation: a provider process the host can no longer reach.</summary>
    public TaskCompletionSource? ClaudeHold { get; set; }

    /// <summary>Raised when a Claude call has started and is holding.</summary>
    public SemaphoreSlim ClaudeEntered { get; } = new(0);

    /// <summary>Raised when a verification call that holds has started.</summary>
    public SemaphoreSlim VerificationEntered { get; } = new(0);

    public int ClaudeInvocations
    {
        get
        {
            lock (Requests)
            {
                return Requests.Count(request => request.StandardInput is not null);
            }
        }
    }

    public int VerificationInvocations
    {
        get
        {
            lock (VerificationCalls)
            {
                return VerificationCalls.Count;
            }
        }
    }

    public async Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        return request.StandardInput is null
            ? await VerifyAsync(request)
            : await ClaudeAsync();
    }

    private async Task<ProcessExecutionResult> VerifyAsync(ProcessExecutionRequest request)
    {
        var argument = request.Arguments.Single(candidate => candidate.StartsWith(RecipeArgumentPrefix, StringComparison.Ordinal));
        var number = int.Parse(argument[RecipeArgumentPrefix.Length..], System.Globalization.CultureInfo.InvariantCulture);
        LocalExecutionSequenceVerificationCall call;
        lock (VerificationCalls)
        {
            recipeCalls[number] = recipeCalls.GetValueOrDefault(number) + 1;
            call = new LocalExecutionSequenceVerificationCall(number, recipeCalls[number], ++order, DateTimeOffset.UtcNow);
            VerificationCalls.Add(call);
        }

        var behavior = Verification(call);
        if (behavior.Hold is { } hold)
        {
            VerificationEntered.Release();
            await hold.Task;
        }

        if (behavior.Throws)
        {
            throw new InvalidOperationException("The process double refused to start the recipe.");
        }

        if (request.StandardOutputSinkPath is { } stdoutSink && request.StandardErrorSinkPath is { } stderrSink)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stdoutSink)!);
            await File.WriteAllTextAsync(stdoutSink, behavior.Stdout);
            await File.WriteAllTextAsync(stderrSink, behavior.Stderr);
        }

        return new ProcessExecutionResult
        {
            Outcome = behavior.Outcome,
            ExitCode = behavior.ExitCode,
            StandardOutput = string.Empty,
            StandardOutputTruncated = false,
            StandardError = string.Empty,
            StandardErrorTruncated = false,
            Duration = TimeSpan.FromMilliseconds(5),
            Termination = behavior.Termination,
        };
    }

    /// <summary>ADR-0065: the next Claude call ends with this process outcome (a timeout or cancellation) and optionally the physical
    /// termination the adapter observed, instead of a computed answer.</summary>
    public Queue<(ProcessExecutionOutcome Outcome, ProcessTerminationObservation? Termination)> ClaudeEnds { get; } = new();

    private async Task<ProcessExecutionResult> ClaudeAsync()
    {
        if (ClaudeEnds.TryDequeue(out var ended))
        {
            // A process that ended without a result changed nothing the staged Git evidence would show.
            return new ProcessExecutionResult
            {
                Outcome = ended.Outcome,
                ExitCode = null,
                StandardOutput = string.Empty,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromMilliseconds(5),
                Termination = ended.Termination,
            };
        }

        var finalResponse = await ComputeClaudeAnswerAsync();
        if (ClaudeHold is { } hold)
        {
            ClaudeEntered.Release();
            await hold.Task;
        }

        evidence.Stage++;
        return new ProcessExecutionResult
        {
            Outcome = ProcessExecutionOutcome.Exited,
            ExitCode = 0,
            StandardOutput = JsonSerializer.Serialize(new { is_error = false, result = finalResponse, session_id = (string?)null }),
            StandardOutputTruncated = false,
            StandardError = string.Empty,
            StandardErrorTruncated = false,
            Duration = TimeSpan.FromMilliseconds(5),
        };
    }

    /// <summary>The answer of the one running Claude operation: an implementation report, or the correction of exactly its recorded
    /// findings. Read from the database, never guessed.</summary>
    private async Task<string> ComputeClaudeAnswerAsync()
    {
        await using var context = openContext();
        var running = await context.Attempts.AsNoTracking()
            .Where(attempt => attempt.Kind == AttemptKind.Agent && attempt.Status == AttemptStatus.Running
                && (attempt.AgentResponseContract == AgentResponseContract.ImplementationReport
                    || attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection))
            .OrderByDescending(attempt => attempt.AttemptNumber)
            .FirstAsync();
        lock (ClaudeAttemptIds)
        {
            ClaudeAttemptIds.Add(running.Id);
        }

        lock (ClaudeAnswers)
        {
            if (ClaudeAnswers.Count > 0)
            {
                return ClaudeAnswers.Dequeue();
            }
        }

        if (running.AgentResponseContract == AgentResponseContract.ImplementationReport)
        {
            return LocalExecutionSequenceResponses.ImplementationReport();
        }

        var findings = await (
            from input in context.AttemptInputMessages.AsNoTracking()
            join message in context.CollaborationMessages.AsNoTracking() on input.CollaborationMessageId equals message.Id
            where input.AttemptId == running.Id && message.Type == CollaborationMessageType.ReviewFinding
            orderby input.Sequence
            select message.Id).ToListAsync();
        return LocalExecutionSequenceResponses.Correction(findings);
    }
}
