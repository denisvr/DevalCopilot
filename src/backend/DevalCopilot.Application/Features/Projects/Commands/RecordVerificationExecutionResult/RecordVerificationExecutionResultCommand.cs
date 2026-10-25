using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionResult;

public sealed record RecordVerificationExecutionResultCommand(
    Guid VerificationExecutionId,
    VerificationExecutionOutcome Outcome,
    int? ExitCode,
    string CompletionFingerprintSha256,
    IReadOnlyList<SealedVerificationOutputArtifact> SealedArtifacts) : ICommand<Result<VerificationExecutionStatus>>
{
    /// <summary>ADR-0065: the opaque in-memory ticket the original host received from the committed dispatch marker of a linked local
    /// execution verification. It is carried unchanged, cannot be minted outside the Application, is never serialized and is absent for
    /// every unlinked execution.</summary>
    public LocalExecutionDispatchTicket? DispatchTicket { get; init; }

    /// <summary>ADR-0065: the optional host-produced physical termination fact of the actual owned process. It never changes
    /// <see cref="Outcome"/> or <see cref="ExitCode"/>; absent evidence preserves the ordinary recording exactly.</summary>
    public ProcessTerminationObservation? Termination { get; init; }

    /// <summary>The returned process result's duration, supplied with <see cref="Termination"/> so the observation can retain the matching
    /// process tuple. It is not otherwise stored or interpreted.</summary>
    public TimeSpan? ProcessDuration { get; init; }
}
