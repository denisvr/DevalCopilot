using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionStepInspection;

/// <summary>The proved verification facts of a step and the admitted metadata of its two sealed streams, with the exact Recorded,
/// NotRecorded and Unavailable semantics of ADR-0052. A bad stream never hides its sibling or the verification facts.</summary>
public sealed record LocalExecutionVerificationInspection(
    LocalExecutionVerificationFacts Facts,
    RecordedVerificationOutputDescriptor StandardOutput,
    RecordedVerificationOutputDescriptor StandardError);
