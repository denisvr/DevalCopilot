using Devalente.Shared.Cqrs;
using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetDueHostCapabilityProbes;

public sealed record GetDueHostCapabilityProbesQuery : IQuery<IReadOnlyList<Capability>>;
