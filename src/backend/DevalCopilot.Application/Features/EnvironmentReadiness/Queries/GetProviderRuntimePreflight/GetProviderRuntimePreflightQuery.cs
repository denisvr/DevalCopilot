using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetProviderRuntimePreflight;

public sealed record GetProviderRuntimePreflightQuery : IQuery<IReadOnlyList<ProviderRuntimePreflightQueryResult>>;
