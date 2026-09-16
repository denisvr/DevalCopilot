using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Commands.RecoverInterruptedVerificationOutputArtifacts;

public sealed record RecoverInterruptedVerificationOutputArtifactsCommand : IManualTransactionCommand<Result<int>>;
