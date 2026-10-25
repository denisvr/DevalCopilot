using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

namespace DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;

/// <summary>
/// The committed single-use dispatch marker of one verification execution. <see cref="Ticket"/> is the opaque in-memory invocation ticket
/// of ADR-0065, present only when the execution is the exact linked operation of a local execution sequence and its dispatch witness
/// committed atomically with the marker; the original host's supervisor carries it to the ordinary result recorder. It is never present
/// for a refused or uncertain commit and never for an unlinked execution.
/// </summary>
public sealed record MarkVerificationExecutionDispatchedCommandResult(LocalExecutionDispatchTicket? Ticket = null);
