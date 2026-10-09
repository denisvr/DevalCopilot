using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Every durable fact a refused or rolled-back abandonment must leave exactly as it found it, taken by
/// <see cref="AbandonmentScene.SnapshotAsync"/> through an independent context.</summary>
internal sealed record AbandonmentSnapshot(
    string Lifecycle,
    string Stage,
    double AccumulatedSeconds,
    DateTimeOffset LastAdvancedAtUtc,
    string? Reason,
    DateTimeOffset? AbandonedAtUtc,
    ParticipantKind ActiveParticipant,
    int ProjectEvents,
    int AbandonedEvents,
    int Attempts,
    int Workspaces,
    int Runs,
    int NextExecutionNumber);
