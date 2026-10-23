namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;

/// <summary>
/// One outstanding candidate admission as the global statement read it, repeated on every row of its ledger. <see cref="StateCode"/> is 1
/// to 5 for a known admission state and 0 otherwise; <see cref="Number"/> is null unless the stored value is an exact positive Int64.
/// </summary>
internal sealed record LocalExecutionQueueInspectionCandidate(Guid? Id, Guid? SequenceId, long? Number, int StateCode);
