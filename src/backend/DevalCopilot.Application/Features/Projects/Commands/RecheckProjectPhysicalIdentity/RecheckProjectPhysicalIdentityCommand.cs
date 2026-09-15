using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Commands.RecheckProjectPhysicalIdentity;

public sealed record RecheckProjectPhysicalIdentityCommand(Guid ProjectId)
    : ICommand<Result<RecheckProjectPhysicalIdentityCommandResult>>;
