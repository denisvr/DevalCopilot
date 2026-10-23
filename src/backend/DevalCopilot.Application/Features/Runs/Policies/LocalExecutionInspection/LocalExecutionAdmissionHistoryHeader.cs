using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>The stored bookkeeping of the queued sequence an admission history belongs to (ADR-0063): never effective live control, a
/// final approval or commit proof, a preparation owner token, a settings snapshot or a digest. <see cref="AdvancementDeadlineUtc"/> is
/// the recorded absolute two-hour deadline from arming, proved equal to Domain's own derivation.</summary>
public sealed record LocalExecutionAdmissionHistoryHeader(
    Guid SequenceId,
    LocalExecutionAdmissionMode AdmissionMode,
    LocalExecutionSequenceState RecordedState,
    string? ReasonCode,
    long SequenceRevision,
    DateTimeOffset ArmedAtUtc,
    DateTimeOffset AdvancementDeadlineUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? EndedAtUtc);
