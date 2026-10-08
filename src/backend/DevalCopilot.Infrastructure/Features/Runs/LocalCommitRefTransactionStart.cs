namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>The result of starting and preparing one reference transaction. <paramref name="Transaction"/> is present only when
/// <c>prepare: ok</c> arrived. Otherwise the owner has already closed, terminated and reaped its child;
/// <paramref name="NoMutationProven"/> is true only when <c>commit</c> was never sent and the child ended by itself without being
/// terminated (so Git released its own locks), and <paramref name="Reason"/> names the phase and fault.</summary>
internal sealed record LocalCommitRefTransactionStart(
    LocalCommitRefTransaction? Transaction, string Reason, bool NoMutationProven);
