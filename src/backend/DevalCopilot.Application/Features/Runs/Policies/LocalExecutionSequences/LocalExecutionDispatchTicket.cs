using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// ADR-0065: the opaque, in-memory invocation ticket of one dispatched linked local execution operation. It exists only after the original
/// dispatch commit (the existing single-use marker together with its dispatch witness) succeeded, is carried by the original host's own
/// supervisor to its ordinary result recorder, and is never serialized, logged, persisted, reconstructed after a restart or supplied by a
/// client, provider or environment. Its constructor is internal to the Application, so no other assembly can mint one; it is not a
/// credential, only the possession a recorder must show before it may add the physical termination observation of that exact invocation.
/// A refused or uncertain commit returns no ticket and invokes nothing.
/// </summary>
public sealed class LocalExecutionDispatchTicket
{
    internal LocalExecutionDispatchTicket(
        Guid witnessId,
        LocalExecutionOperationFamily family,
        Guid operationId,
        Guid stepId,
        Guid sequenceId,
        Guid hostLaunchId,
        Guid nonce,
        DateTimeOffset dispatchedAtUtc)
    {
        WitnessId = witnessId;
        Family = family;
        OperationId = operationId;
        StepId = stepId;
        SequenceId = sequenceId;
        HostLaunchId = hostLaunchId;
        Nonce = nonce;
        DispatchedAtUtc = dispatchedAtUtc;
    }

    internal Guid WitnessId { get; }

    internal LocalExecutionOperationFamily Family { get; }

    internal Guid OperationId { get; }

    internal Guid StepId { get; }

    internal Guid SequenceId { get; }

    internal Guid HostLaunchId { get; }

    internal Guid Nonce { get; }

    internal DateTimeOffset DispatchedAtUtc { get; }

    /// <summary>Never prints the ticket's identities or nonce.</summary>
    public override string ToString() => nameof(LocalExecutionDispatchTicket);
}
