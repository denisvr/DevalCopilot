using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Commands.DeleteVerificationCommand;

/// <summary>
/// Explicit hard deletion for unused command configuration. Execution evidence will snapshot
/// its configuration in a later slice, so deleting a recipe cannot erase completed evidence.
/// </summary>
public sealed record DeleteVerificationCommandCommand(Guid ProjectId, Guid VerificationCommandId) : ICommand<Result>;
