using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Commands.DeleteVerificationCommand;

/// <summary>
/// Explicit hard deletion for unused command configuration. Execution evidence snapshots its
/// configuration and restricts deletion while that evidence still references the recipe.
/// </summary>
public sealed record DeleteVerificationCommandCommand(Guid ProjectId, Guid VerificationCommandId) : ICommand<Result>;
