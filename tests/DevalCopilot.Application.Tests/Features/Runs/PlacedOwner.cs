namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>One owner placed by <see cref="ExecutionCapacitySeed"/>: its family, the exact row identifier of its own table, and the
/// project, run, workspace and checkpoint it lives in (each owner has its own independent repository).</summary>
internal sealed record PlacedOwner(OwnerFamily Family, Guid Id, Guid ProjectId, Guid RunId, Guid WorkspaceId, Guid CheckpointId);
