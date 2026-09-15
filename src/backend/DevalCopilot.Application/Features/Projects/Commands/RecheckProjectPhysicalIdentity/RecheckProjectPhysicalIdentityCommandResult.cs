using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Commands.RecheckProjectPhysicalIdentity;

public sealed record RecheckProjectPhysicalIdentityCommandResult(PhysicalIdentityStatus Status);
