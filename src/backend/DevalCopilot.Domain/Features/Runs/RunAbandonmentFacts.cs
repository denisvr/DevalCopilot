namespace DevalCopilot.Domain.Features.Runs;

/// <summary>The persisted facts of one run and every <see cref="RunEventType.RunAbandoned"/> event it owns, the input of
/// <see cref="RunAbandonmentPolicy.IsCoherent"/>. Participant checks are carried as booleans decided by the reader in the database,
/// so a malformed stored participant is simply not coherent and reading it never throws; a recorded time that could not be read as
/// a time is <see langword="null"/>.</summary>
public sealed record RunAbandonmentFacts(
    RunLifecycle Lifecycle,
    RunExecutionMode ExecutionMode,
    string? Reason,
    DateTimeOffset? AbandonedAtUtc,
    DateTimeOffset LastAdvancedAtUtc,
    bool HasNoActiveParticipant,
    IReadOnlyList<RunAbandonmentEventFacts> AbandonedEvents);
