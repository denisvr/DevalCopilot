namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One durable objective against an initial project, progressing through the bounded
/// deterministic simulated-agent-collaboration sequence for the walking-skeleton slice.
/// </summary>
public sealed class Run
{
    private Run()
    {
    }

    /// <summary>The fixed default reserved-time policy every new Run is assigned by
    /// <see cref="RecordIntent"/> — see the run-wide Agent invocation-time budget ADR. Unlike
    /// <see cref="MaximumAgentAttempts"/>'s historical backfill (ADR-0012), a historical Run
    /// predating that decision is never assigned this value retroactively: only rows constructed
    /// through this factory ever receive it, so a Run persisted directly (e.g. by an additive
    /// migration backfill) keeps <see cref="MaximumAgentInvocationTime"/> truthfully
    /// <see langword="null"/>.</summary>
    public static readonly TimeSpan DefaultMaximumAgentInvocationTime = TimeSpan.FromMinutes(120);

    public static Run RecordIntent(
        Guid id,
        Guid projectId,
        int executionNumber,
        string objective,
        DateTimeOffset nowUtc,
        int maximumReviewCorrectionAttempts = 2,
        int maximumAgentAttempts = 16,
        TimeSpan? maximumAgentInvocationTime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objective);

        if (executionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(executionNumber));
        }

        if (maximumReviewCorrectionAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumReviewCorrectionAttempts));
        }

        if (maximumAgentAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAgentAttempts));
        }

        if (maximumAgentInvocationTime is { } requestedInvocationTime && requestedInvocationTime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAgentInvocationTime), requestedInvocationTime, "Must be a positive, bounded reservation ceiling.");
        }

        return new Run
        {
            Id = id,
            ProjectId = projectId,
            ExecutionNumber = executionNumber,
            Objective = objective,
            Lifecycle = RunLifecycle.Created,
            Stage = RunStage.Intake,
            ActiveParticipantKind = ParticipantKind.None,
            CreatedAtUtc = nowUtc,
            LastAdvancedAtUtc = nowUtc,
            AccumulatedAutonomousSeconds = 0,
            MaximumReviewCorrectionAttempts = maximumReviewCorrectionAttempts,
            MaximumAgentAttempts = maximumAgentAttempts,
            MaximumAgentInvocationTime = maximumAgentInvocationTime ?? DefaultMaximumAgentInvocationTime,
        };
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public int ExecutionNumber { get; private set; }

    public string Objective { get; private set; } = string.Empty;

    public RunLifecycle Lifecycle { get; private set; }

    public RunStage Stage { get; private set; }

    public ParticipantKind ActiveParticipantKind { get; private set; }

    public AgentRole? ActiveAgentRole { get; private set; }

    public AgentProvider? ActiveAgentProvider { get; private set; }

    public ParticipantIdentity ActiveParticipant =>
        ParticipantIdentity.FromParts(ActiveParticipantKind, ActiveAgentRole, ActiveAgentProvider);

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset LastAdvancedAtUtc { get; private set; }

    /// <summary>
    /// Total time accumulated while <see cref="Lifecycle"/> was <see cref="RunLifecycle.Running"/>.
    /// Excludes intent-recorded (not yet claimed) and terminal time.
    /// </summary>
    public double AccumulatedAutonomousSeconds { get; private set; }

    /// <summary>
    /// Maximum number of claimed review-correction attempts for this run. Claims consume this
    /// immutable-per-run policy value even when the provider later fails or the attempt is
    /// interrupted.
    /// </summary>
    public int MaximumReviewCorrectionAttempts { get; private set; }

    /// <summary>
    /// Maximum number of claimed Agent attempts for this run, across every role, provider,
    /// dispatch outcome, and interruption. Every claimed Agent attempt — regardless of the six
    /// distinct claim paths that create one — consumes exactly one permanent slot of this
    /// immutable-per-run policy value, even when the provider later fails or the attempt is
    /// interrupted. Simulated and Process attempts never consume this budget. Unlike
    /// <see cref="MaximumReviewCorrectionAttempts"/>, exhaustion of this budget has no human
    /// override: it is a hard ceiling for the run.
    /// </summary>
    public int MaximumAgentAttempts { get; private set; }

    /// <summary>
    /// Maximum total reserved Agent invocation time for this run, independent of and enforced
    /// alongside <see cref="MaximumAgentAttempts"/> — see the run-wide Agent invocation-time
    /// budget ADR. Every claimed Agent attempt permanently reserves its own configured
    /// <see cref="Attempt.AgentTimeout"/>, whether later dispatched, failed, or interrupted;
    /// Simulated and Process attempts never consume it. <see langword="null"/> means this Run has
    /// no time-budget policy at all — truthfully distinguishing a historical Run that predates
    /// this decision (never assigned a fabricated value) from a Run that carries a real, bounded
    /// policy. Unlike <see cref="MaximumAgentAttempts"/>, no historical Run's value is ever
    /// migrated or backfilled: only <see cref="RecordIntent"/> ever assigns a non-null value, to a
    /// newly created Run.
    /// </summary>
    public TimeSpan? MaximumAgentInvocationTime { get; private set; }

    /// <summary>The most limit-id-style bounded length this Run trusts for a requested Codex
    /// model or reasoning-effort identifier — mirrors <c>Attempt</c>'s own assignment-identifier
    /// bound so a value that later flows into a claimed <c>Attempt</c>'s immutable assignment
    /// fields can never itself be rejected there.</summary>
    private const int MaxRequestedAssignmentIdentifierLength = 128;

    /// <summary>
    /// The owner's current, explicit, run-scoped request for future Codex Planner, Challenge
    /// Resolver, and Code Reviewer attempts — never an effective, observed, or guaranteed value.
    /// <see langword="null"/> means no explicit request is set (the default for every historical
    /// and newly created Run): future Codex claims pass no override and the shared Codex invoker
    /// preserves its exact existing argument list. Changing this while an attempt is already
    /// claimed never affects that already-claimed attempt's own immutable assignment — only a
    /// later claim reads the new value.
    /// </summary>
    public string? RequestedCodexModel { get; private set; }

    /// <summary>The owner's current, explicit, run-scoped requested reasoning effort, paired with
    /// <see cref="RequestedCodexModel"/>. Always <see langword="null"/> when
    /// <see cref="RequestedCodexModel"/> is <see langword="null"/> — an effort is never requested
    /// without a requested model.</summary>
    public string? RequestedCodexEffort { get; private set; }

    /// <summary>
    /// Sets or clears the owner's explicit, run-scoped Codex model/effort request for future
    /// Planner, Challenge Resolver, and Code Reviewer claims. This method only enforces the
    /// structural invariants an <c>Attempt</c>'s own assignment fields already require (bounded
    /// length, an effort never present without a model) — the business rule that a non-null model
    /// must be one visible, freshly observed catalog id (and a non-null effort one of that
    /// model's own known supported efforts) is validated by the calling Application handler
    /// against a fresh catalog observation, before this method is ever called, because Domain
    /// never performs I/O. Permitted while <see cref="Lifecycle"/> is <see cref="RunLifecycle.Created"/>
    /// or <see cref="RunLifecycle.Running"/> — a terminal Run accepts no further preference change.
    /// </summary>
    public void SetRequestedCodexAssignment(string? requestedModel, string? requestedEffort)
    {
        if (Lifecycle is not (RunLifecycle.Created or RunLifecycle.Running))
        {
            throw new InvalidOperationException($"Cannot change the requested Codex assignment for a run whose lifecycle is {Lifecycle}.");
        }

        if (!IsValidRequestedAssignmentIdentifier(requestedModel))
        {
            throw new ArgumentException("A requested Codex model must be blank or at most 128 characters.", nameof(requestedModel));
        }

        if (!IsValidRequestedAssignmentIdentifier(requestedEffort))
        {
            throw new ArgumentException("A requested Codex effort must be blank or at most 128 characters.", nameof(requestedEffort));
        }

        if (requestedModel is null && requestedEffort is not null)
        {
            throw new ArgumentException("A requested Codex effort requires a requested model.", nameof(requestedEffort));
        }

        RequestedCodexModel = requestedModel;
        RequestedCodexEffort = requestedEffort;
    }

    /// <summary>
    /// The owner's current, explicit, run-scoped request for a Claude CLI model alias
    /// (<see cref="ClaudeModelAlias"/>) for future CriticalReviewer, Implementer, and
    /// ReviewCorrection attempts — never an observed, effective, or account-eligible model.
    /// <see langword="null"/> means no override (the default for every historical and newly created
    /// Run): future Claude claims snapshot no request and the adapters pass no <c>--model</c>
    /// argument. Changing this never affects an already-claimed attempt's own immutable snapshot.
    /// </summary>
    public string? RequestedClaudeModel { get; private set; }

    /// <summary>
    /// The owner's current, explicit, run-scoped request for a Claude CLI effort level
    /// (<see cref="ClaudeEffortLevel"/>), valid only together with a requested <c>sonnet</c> or
    /// <c>opus</c> alias (see <see cref="ClaudeModelRequest"/>). A request for the CLI's
    /// <c>--effort</c> argument, never an observed or effective effort: the provider may reject or
    /// adjust it. <see langword="null"/> means no effort argument.
    /// </summary>
    public string? RequestedClaudeEffort { get; private set; }

    /// <summary>Sets or clears the run-scoped Claude model/effort request as one pair. Only a pair
    /// accepted by <see cref="ClaudeModelRequest.IsValid"/> is stored; permitted while
    /// <see cref="Lifecycle"/> is Created or Running.</summary>
    public void SetRequestedClaudeModelRequest(string? requestedModel, string? requestedEffort)
    {
        if (Lifecycle is not (RunLifecycle.Created or RunLifecycle.Running))
        {
            throw new InvalidOperationException($"Cannot change the requested Claude model for a run whose lifecycle is {Lifecycle}.");
        }

        if (!ClaudeModelRequest.IsValid(requestedModel, requestedEffort))
        {
            throw new ArgumentException(
                "A requested Claude model must be a supported alias, and an effort is valid only with sonnet or opus.",
                nameof(requestedModel));
        }

        RequestedClaudeModel = requestedModel;
        RequestedClaudeEffort = requestedEffort;
    }

    /// <summary>The largest warning threshold this Run accepts (10^12 reported token-activity
    /// units). A bound, not a policy: it keeps every threshold comparable with the bounded sums
    /// the cockpit projects and safely representable everywhere.</summary>
    public const long MaxTokenWarningThreshold = 1_000_000_000_000L;

    /// <summary>
    /// The owner's optional advisory warning threshold, in Codex-reported token-activity units
    /// (validated <c>inputTokens + outputTokens</c> of concluded, dispatched Codex attempts), for
    /// this Run. <see langword="null"/> means none is configured (the default for every historical
    /// and newly created Run). Advisory only: never a budget or an eligibility rule, and never read
    /// by any claim, dispatch, or adapter path.
    /// </summary>
    public long? CodexTokenWarningThreshold { get; private set; }

    /// <summary>The same advisory threshold for Claude Code (validated <c>inputTokens +
    /// cacheCreationInputTokens + cacheReadInputTokens + outputTokens</c>). Independent of
    /// <see cref="CodexTokenWarningThreshold"/>: the two are never combined.</summary>
    public long? ClaudeTokenWarningThreshold { get; private set; }

    /// <summary>Sets or clears one provider's advisory token-activity warning threshold, leaving
    /// the other provider's untouched. A non-null value must be positive and at most
    /// <see cref="MaxTokenWarningThreshold"/>; permitted while <see cref="Lifecycle"/> is Created
    /// or Running.</summary>
    public void SetTokenWarningThreshold(AgentProvider provider, long? threshold)
    {
        if (Lifecycle is not (RunLifecycle.Created or RunLifecycle.Running))
        {
            throw new InvalidOperationException($"Cannot change a token warning threshold for a run whose lifecycle is {Lifecycle}.");
        }

        if (provider is not (AgentProvider.Codex or AgentProvider.ClaudeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(provider), provider, "A token warning threshold applies only to Codex or Claude Code.");
        }

        if (threshold is { } value && (value < 1 || value > MaxTokenWarningThreshold))
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "A token warning threshold must be between 1 and the supported maximum.");
        }

        if (provider == AgentProvider.Codex)
        {
            CodexTokenWarningThreshold = threshold;
        }
        else
        {
            ClaudeTokenWarningThreshold = threshold;
        }
    }

    private static bool IsValidRequestedAssignmentIdentifier(string? value) =>
        value is null || (!string.IsNullOrWhiteSpace(value) && value.Length <= MaxRequestedAssignmentIdentifierLength);

    /// <summary>
    /// The hosted supervisor claims recorded intent and starts the simulated attempt.
    /// This is the transition that must happen outside the command that recorded intent.
    /// </summary>
    public void Claim(DateTimeOffset nowUtc)
    {
        if (Lifecycle != RunLifecycle.Created)
        {
            throw new InvalidOperationException($"Cannot claim a run whose lifecycle is {Lifecycle}.");
        }

        Lifecycle = RunLifecycle.Running;
        SetActiveParticipant(ParticipantIdentity.ForOrchestrator());
        LastAdvancedAtUtc = nowUtc;
    }

    public void AdvanceStage(RunStage nextStage, ParticipantIdentity participant, DateTimeOffset nowUtc)
    {
        if (Lifecycle != RunLifecycle.Running)
        {
            throw new InvalidOperationException($"Cannot advance a run whose lifecycle is {Lifecycle}.");
        }

        if (nextStage <= Stage)
        {
            throw new InvalidOperationException(
                $"Stage must advance forward: cannot move from {Stage} to {nextStage}.");
        }

        AccumulateAutonomousTime(nowUtc);
        Stage = nextStage;
        SetActiveParticipant(participant);
        LastAdvancedAtUtc = nowUtc;
    }

    public void Complete(DateTimeOffset nowUtc)
    {
        if (Lifecycle != RunLifecycle.Running)
        {
            throw new InvalidOperationException($"Cannot complete a run whose lifecycle is {Lifecycle}.");
        }

        AccumulateAutonomousTime(nowUtc);
        Lifecycle = RunLifecycle.Completed;
        Stage = RunStage.Completed;
        SetActiveParticipant(ParticipantIdentity.None());
        LastAdvancedAtUtc = nowUtc;
    }

    /// <summary>
    /// Its owning attempt ended without succeeding. <see cref="Stage"/> is left as-is: a
    /// failure does not represent forward progress to a later stage.
    /// </summary>
    public void Fail(DateTimeOffset nowUtc)
    {
        if (Lifecycle != RunLifecycle.Running)
        {
            throw new InvalidOperationException($"Cannot fail a run whose lifecycle is {Lifecycle}.");
        }

        AccumulateAutonomousTime(nowUtc);
        Lifecycle = RunLifecycle.Failed;
        SetActiveParticipant(ParticipantIdentity.None());
        LastAdvancedAtUtc = nowUtc;
    }

    /// <summary>
    /// The restart-reconciliation transition, applied atomically alongside the owning
    /// attempt's own <see cref="Attempt.Interrupt"/>. <see cref="Stage"/> is left as-is —
    /// this is not forward progress, and a later intentional re-run is a new run.
    /// </summary>
    public void MarkInterrupted(DateTimeOffset nowUtc)
    {
        if (Lifecycle != RunLifecycle.Running)
        {
            throw new InvalidOperationException($"Cannot interrupt a run whose lifecycle is {Lifecycle}.");
        }

        AccumulateAutonomousTime(nowUtc);
        Lifecycle = RunLifecycle.Interrupted;
        SetActiveParticipant(ParticipantIdentity.None());
        LastAdvancedAtUtc = nowUtc;
    }

    private void AccumulateAutonomousTime(DateTimeOffset nowUtc)
    {
        var elapsed = (nowUtc - LastAdvancedAtUtc).TotalSeconds;

        if (elapsed > 0)
        {
            AccumulatedAutonomousSeconds += elapsed;
        }
    }

    private void SetActiveParticipant(ParticipantIdentity participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ActiveParticipantKind = participant.Kind;
        ActiveAgentRole = participant.Role;
        ActiveAgentProvider = participant.Provider;
    }
}
