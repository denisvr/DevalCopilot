using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Commands.MarkHostCapabilityProbeDispatched;

/// <summary>
/// The in-flight claim for one capability's current probe cycle: committed, in one short
/// transaction, before the discovery adapter is ever invoked. Exists for resource hygiene —
/// preventing two overlapping probes for the same capability across polls — not because
/// repeating a read-only version probe would itself be unsafe.
/// </summary>
public sealed record MarkHostCapabilityProbeDispatchedCommand(Capability Capability) : ICommand<Result<DateTimeOffset>>;
