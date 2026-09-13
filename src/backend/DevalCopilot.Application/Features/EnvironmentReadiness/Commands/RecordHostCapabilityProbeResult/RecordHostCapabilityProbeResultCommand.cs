using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.EnvironmentReadiness.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Commands.RecordHostCapabilityProbeResult;

/// <summary>
/// Records one capability's probe outcome — success or a closed failure reason — clears its
/// dispatch marker, and schedules its next probe. Never records raw process output: only the
/// closed <see cref="ToolDiscoveryResult.Reason"/> and, on success, a resolved path and parsed
/// version.
/// </summary>
public sealed record RecordHostCapabilityProbeResultCommand(Capability Capability, ToolDiscoveryResult Outcome)
    : ICommand<Result<CapabilityProbeReason>>;
