using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Commands.RegisterProject;

public sealed record RegisterProjectCommand(string Name, string RequestedPath) : ICommand<Result<RegisterProjectCommandResult>>;
