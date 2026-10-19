namespace DevalCopilot.Application.Features.Runs.Commands.HeartbeatAgentSupervision;

/// <summary>The revision after the heartbeat, and whether this write recorded the observation's first attention.</summary>
public sealed record HeartbeatAgentSupervisionCommandResult(long Revision, bool AttentionRecorded);
