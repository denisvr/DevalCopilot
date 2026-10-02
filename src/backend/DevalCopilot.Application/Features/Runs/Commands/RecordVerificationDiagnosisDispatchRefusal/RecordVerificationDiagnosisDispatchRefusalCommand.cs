using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordVerificationDiagnosisDispatchRefusal;

/// <summary>
/// Records, for a still-undispatched verification-diagnosis attempt, the dispatch-gate loss the supervisor observed. The
/// handler never trusts the caller's claim: it independently re-derives the loss from the database before mutating the
/// attempt, and records the closed outcome with no process evidence (the provider was never invoked).
/// </summary>
public sealed record RecordVerificationDiagnosisDispatchRefusalCommand(
    Guid RunId, Guid AttemptId, VerificationDiagnosisDispatchRefusal Refusal) : ICommand<Result>;
