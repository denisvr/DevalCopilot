namespace DevalCopilot.Application.Data;

/// <summary>
/// Resolves whether an Attempt is durably persisted after a claim boundary step's outcome became
/// ambiguous to the caller (a commit or save that threw, or was cancelled, without the caller
/// knowing whether the database actually applied it first). Never answered from the same
/// <see cref="IDevalCopilotDbContext"/> whose own transaction just produced that ambiguity: that
/// connection's transaction state is exactly what is uncertain, and a best-effort rollback that
/// itself failed can leave it in a state where a query against it is not a reliable answer. An
/// implementation must use an independent connection, and must bound how long it waits for one.
/// </summary>
public interface IAttemptDurabilityProbe
{
    Task<AttemptDurabilityCheckResult> CheckAsync(Guid attemptId, CancellationToken cancellationToken);
}

/// <summary>
/// The three-way outcome of an <see cref="IAttemptDurabilityProbe"/> check. <see cref="Unresolved"/>
/// is not a fallback for "probably not persisted" — it means the independent probe itself could not
/// establish an answer within its own bound, and callers must treat that exactly like an unknown: no
/// cleanup, no success claim.
/// </summary>
public enum AttemptDurabilityCheckResult
{
    Persisted,
    NotPersisted,
    Unresolved,
}
