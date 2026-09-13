using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Commands.RequestHostCapabilityRefresh;

/// <summary>
/// The user-facing "Refresh now" intention: pulls one capability's next probe forward to now.
/// A no-op, not a failure, while a probe for it is already in flight. Host-scoped — refreshing
/// from any one project's view updates the shared observation every project sees.
/// </summary>
public sealed record RequestHostCapabilityRefreshCommand(Capability Capability) : ICommand<Result<DateTimeOffset>>;
