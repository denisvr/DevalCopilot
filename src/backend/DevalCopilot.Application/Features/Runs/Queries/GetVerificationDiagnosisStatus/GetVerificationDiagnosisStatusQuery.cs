using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetVerificationDiagnosisStatus;

/// <summary>Bounded status, findings, escalation, correction, and budget facts for the most recent verification diagnosis on
/// a run, plus a truthful hint about whether the current verification can be diagnosed — never a path, prompt, transcript,
/// output, or credential. An unknown run fails with <c>runs.not_found</c>; a run with no diagnosis succeeds with an explicit
/// <c>HasAttempt: false</c>.</summary>
public sealed record GetVerificationDiagnosisStatusQuery(Guid RunId) : IQuery<Result<VerificationDiagnosisStatusQueryResult>>;
