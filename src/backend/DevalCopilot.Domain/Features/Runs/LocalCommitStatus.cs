namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The durable lifecycle of one explicit local-commit operation (ADR-0029). <see cref="Prepared"/> records the immutable
/// facts that precede any branch mutation; <see cref="Executing"/> is the single-use execution marker written before the first
/// ref or index change. <see cref="Completed"/>, <see cref="Failed"/> and <see cref="Interrupted"/> are terminal for the
/// operation; <see cref="NeedsAttention"/> is the ambiguous state that is never a success, a failure or a retry.
/// </summary>
public enum LocalCommitStatus
{
    Prepared = 0,
    Executing = 1,
    Completed = 2,
    Failed = 3,
    Interrupted = 4,
    NeedsAttention = 5,
}
