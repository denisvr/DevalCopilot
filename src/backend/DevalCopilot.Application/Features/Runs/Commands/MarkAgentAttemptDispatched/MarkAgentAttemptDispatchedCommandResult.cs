using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

namespace DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;

/// <summary>
/// The committed single-use dispatch marker of one Agent attempt. <see cref="Ticket"/> is the opaque in-memory invocation ticket of
/// ADR-0065, present only when the attempt is the exact linked operation of a local execution sequence and its dispatch witness
/// committed atomically with the marker; the original host's supervisor carries it to the ordinary result recorder. It is never present
/// for a refused or uncertain commit and never for an unlinked attempt.
/// </summary>
public sealed record MarkAgentAttemptDispatchedCommandResult(DateTimeOffset DispatchedAtUtc, LocalExecutionDispatchTicket? Ticket = null);
