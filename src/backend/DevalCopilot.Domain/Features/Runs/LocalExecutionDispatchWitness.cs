namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// ADR-0065: the immutable dispatch witness of one linked local execution operation, committed atomically with that operation's existing
/// single-use dispatch marker and only for an exact operation linked to a local execution sequence. It pins the sequence, run, project,
/// workspace and the complete step tuple (kind, ordinal, pass, recipe membership, predecessor, source checkpoint and fingerprint, report
/// and correction subject), the operation identity of exactly one family and its contract (Agent response contract or verification recipe),
/// the consumed queued admission when the cycle is queued, the committed marker time, the original host launch identity and a private
/// single-invocation nonce. It is provenance of the original dispatch, never a lease, never proof that a process is running or stopped and
/// never reconstructed, updated, replaced, backfilled or deleted.
/// </summary>
public sealed class LocalExecutionDispatchWitness
{
    public const int CurrentVersion = 1;

    private LocalExecutionDispatchWitness()
    {
        SourceFingerprintSha256 = string.Empty;
    }

    /// <summary>Creates the witness of one Agent attempt that is the claimed operation of <paramref name="step"/>.</summary>
    public static LocalExecutionDispatchWitness RecordAgent(
        Guid id,
        LocalExecutionSequence sequence,
        LocalExecutionSequenceStep step,
        Guid attemptId,
        AgentResponseContract contract,
        Guid? admissionId,
        DateTimeOffset dispatchedAtUtc,
        Guid hostLaunchId,
        Guid nonce)
    {
        if (!LocalExecutionSequencePolicy.IsAgentKind(step.Kind) || LocalExecutionSequencePolicy.ContractOf(step.Kind) != contract
            || step.RecipeOrdinal is not null)
        {
            throw new ArgumentException("The Agent contract does not match the step kind.", nameof(contract));
        }

        return Create(
            id, sequence, step, LocalExecutionOperationFamily.Agent, attemptId, contract, null, admissionId, dispatchedAtUtc, hostLaunchId, nonce);
    }

    /// <summary>Creates the witness of one verification execution that is the claimed operation of the verification
    /// <paramref name="step"/>, pinned to its recipe command identity.</summary>
    public static LocalExecutionDispatchWitness RecordVerification(
        Guid id,
        LocalExecutionSequence sequence,
        LocalExecutionSequenceStep step,
        Guid executionId,
        Guid recipeCommandId,
        Guid? admissionId,
        DateTimeOffset dispatchedAtUtc,
        Guid hostLaunchId,
        Guid nonce)
    {
        if (step.Kind != LocalExecutionStepKind.Verification || step.RecipeOrdinal is null || recipeCommandId == Guid.Empty)
        {
            throw new ArgumentException("A verification witness requires a verification step and its recipe.", nameof(step));
        }

        return Create(
            id, sequence, step, LocalExecutionOperationFamily.Verification, executionId, null, recipeCommandId, admissionId, dispatchedAtUtc,
            hostLaunchId, nonce);
    }

    private static LocalExecutionDispatchWitness Create(
        Guid id,
        LocalExecutionSequence sequence,
        LocalExecutionSequenceStep step,
        LocalExecutionOperationFamily family,
        Guid operationId,
        AgentResponseContract? contract,
        Guid? recipeCommandId,
        Guid? admissionId,
        DateTimeOffset dispatchedAtUtc,
        Guid hostLaunchId,
        Guid nonce)
    {
        if (id == Guid.Empty || operationId == Guid.Empty || hostLaunchId == Guid.Empty || nonce == Guid.Empty || admissionId == Guid.Empty)
        {
            throw new ArgumentException("A dispatch witness requires durable identifiers.");
        }

        if (step.SequenceId != sequence.Id || step.State != LocalExecutionSequenceStepState.Claimed || step.OperationId != operationId)
        {
            throw new ArgumentException("The witness must name the exact claimed step and operation of its sequence.", nameof(step));
        }

        // A queued cycle's step consumed exactly one admission; an immediate cycle has none. Never inferred from the other.
        if ((sequence.AdmissionMode == LocalExecutionAdmissionMode.WaitForAdmission) != admissionId.HasValue)
        {
            throw new ArgumentException("A queued sequence names its consumed admission and an immediate one names none.", nameof(admissionId));
        }

        if (dispatchedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(dispatchedAtUtc));
        }

        return new LocalExecutionDispatchWitness
        {
            Id = id,
            Version = CurrentVersion,
            SequenceId = sequence.Id,
            RunId = sequence.RunId,
            ProjectId = sequence.ProjectId,
            WorkspaceId = sequence.WorkspaceId,
            StepId = step.Id,
            OperationFamily = family,
            AgentAttemptId = family == LocalExecutionOperationFamily.Agent ? operationId : null,
            VerificationExecutionId = family == LocalExecutionOperationFamily.Verification ? operationId : null,
            Kind = step.Kind,
            Ordinal = step.Ordinal,
            PassNumber = step.PassNumber,
            RecipeOrdinal = step.RecipeOrdinal,
            PredecessorStepId = step.PredecessorStepId,
            SourceCheckpointId = step.SourceCheckpointId,
            SourceFingerprintSha256 = step.SourceFingerprintSha256,
            ReportMessageId = step.ReportMessageId,
            SubjectAttemptId = step.SubjectAttemptId,
            AgentResponseContract = contract,
            RecipeCommandId = recipeCommandId,
            AdmissionId = admissionId,
            DispatchedAtUtc = dispatchedAtUtc,
            HostLaunchId = hostLaunchId,
            Nonce = nonce,
        };
    }

    public Guid Id { get; private set; }

    public int Version { get; private set; }

    public Guid SequenceId { get; private set; }

    public Guid RunId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public Guid StepId { get; private set; }

    public LocalExecutionOperationFamily OperationFamily { get; private set; }

    /// <summary>The Agent attempt; set exactly when <see cref="OperationFamily"/> is Agent.</summary>
    public Guid? AgentAttemptId { get; private set; }

    /// <summary>The verification execution; set exactly when <see cref="OperationFamily"/> is Verification.</summary>
    public Guid? VerificationExecutionId { get; private set; }

    public LocalExecutionStepKind Kind { get; private set; }

    public int Ordinal { get; private set; }

    public int PassNumber { get; private set; }

    public int? RecipeOrdinal { get; private set; }

    public Guid? PredecessorStepId { get; private set; }

    public Guid SourceCheckpointId { get; private set; }

    public string SourceFingerprintSha256 { get; private set; }

    public Guid? ReportMessageId { get; private set; }

    public Guid? SubjectAttemptId { get; private set; }

    /// <summary>The pinned Agent response contract (Agent family only).</summary>
    public AgentResponseContract? AgentResponseContract { get; private set; }

    /// <summary>The pinned verification recipe command identity (Verification family only).</summary>
    public Guid? RecipeCommandId { get; private set; }

    /// <summary>The consumed queued admission; null exactly for an immediate sequence.</summary>
    public Guid? AdmissionId { get; private set; }

    /// <summary>The committed dispatch marker time of the operation, pinned so a later reading proves it is the same dispatch.</summary>
    public DateTimeOffset DispatchedAtUtc { get; private set; }

    /// <summary>The identity of the one host launch that committed the dispatch. Private provenance, never exposed.</summary>
    public Guid HostLaunchId { get; private set; }

    /// <summary>The private single-invocation nonce held in memory by the original host only. Never exposed.</summary>
    public Guid Nonce { get; private set; }

    /// <summary>The operation identity of the named family.</summary>
    public Guid OperationId => OperationFamily == LocalExecutionOperationFamily.Agent ? AgentAttemptId!.Value : VerificationExecutionId!.Value;
}
