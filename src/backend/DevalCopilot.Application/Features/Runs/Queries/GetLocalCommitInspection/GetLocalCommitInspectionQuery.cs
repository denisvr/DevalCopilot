using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalCommitInspection;

/// <summary>
/// The recorded local-commit operation of one run (ADR-0070), proved from one bounded database statement over the registered run and
/// project, the operation associated with the run and that operation's named workspace and checkpoint. There is no eligibility, no live
/// status, no Git, artifact or external read and no write. An unknown run and a run whose registered project is absent fail with the
/// fixed <c>local_commit_inspection.not_found</c>; a run without an operation is NotRecorded and an associated operation whose facts
/// cannot be proved is Unavailable, never absent.
/// </summary>
public sealed record GetLocalCommitInspectionQuery(Guid RunId) : IQuery<Result<GetLocalCommitInspectionQueryResult>>;
