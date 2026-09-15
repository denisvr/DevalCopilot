using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Queries.GetGitCheckpointChangedFiles;

public sealed record GetGitCheckpointChangedFilesQuery(Guid ProjectId, Guid CheckpointId)
    : IQuery<Result<IReadOnlyList<GitCheckpointChangedFileQueryResult>>>;

public sealed record GitCheckpointChangedFileQueryResult(
    string Path, string? PreviousPath, string IndexStatus, string WorkTreeStatus);
