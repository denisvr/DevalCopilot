namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// ADR-0065 and ADR-0066: the immutable Human release of one terminal local execution operation's retained ownership quarantine. It
/// names one unique dispatch witness and its one physical termination observation, the one appended Human run event that records the
/// decision, and the exact normalized confirmation the Human submitted (version, digest and the UTC expiry that was shown), so an
/// identical request replays this receipt even after the confirmation expired.
///
/// <para>
/// The same row also stores the exact confirmed tuple as typed columns (the <c>Intent</c> members): every canonical fact the Human
/// confirmed, including the concrete Agent outcome or verification completion fingerprint, the confirmation version and the exact expiry.
/// SQLite has no SHA-256 capability, so the digest alone cannot let the provider-neutral ownership expression or a database trigger prove
/// that the referenced rows still say what the Human confirmed; this relational copy can be compared field for field at every seam. It is
/// intent data, never a credential, a cached eligibility flag or a new source of process evidence, and it carries no host nonce, ticket,
/// process identifier, path, argument or output.
/// </para>
///
/// <para>
/// It changes no operation outcome, verification pass, artifact, sequence, admission, workspace status, lease, approval or budget and
/// grants no execution authority: it only stops the original operation from owning its workspace and global mutating capacity, so
/// already authorized work may pass its own fresh gates afterwards. It is not proof about descendants and is never updated, replaced or
/// deleted.
/// </para>
/// </summary>
public sealed class LocalExecutionOwnershipRelease
{
    public const int CurrentVersion = 1;

    /// <summary>The version of the typed intent snapshot stored beside the receipt.</summary>
    public const int CurrentIntentVersion = 1;

    private LocalExecutionOwnershipRelease()
    {
        ConfirmationSha256 = string.Empty;
        IntentSourceFingerprintSha256 = string.Empty;
        IntentOperationStatus = string.Empty;
    }

    /// <summary>
    /// Records the release from the fresh, complete proof under the Human command's final write lock. The caller has already compared the
    /// candidate digest and expiry the Human confirmed with the proof; this transition independently requires that the supplied digest is
    /// exactly the canonical digest of <paramref name="facts"/> and <paramref name="confirmationExpiresAtUtc"/>, and that the facts name
    /// exactly this witness and observation, so the stored snapshot can never differ from what was confirmed.
    /// </summary>
    public static LocalExecutionOwnershipRelease Record(
        Guid id,
        LocalExecutionDispatchWitness witness,
        LocalExecutionTerminationObservation observation,
        Guid eventId,
        DateTimeOffset releasedAtUtc,
        int confirmationVersion,
        string confirmationSha256,
        DateTimeOffset confirmationExpiresAtUtc,
        LocalExecutionOwnershipReleaseFacts facts)
    {
        if (id == Guid.Empty || eventId == Guid.Empty || observation.WitnessId != witness.Id)
        {
            throw new ArgumentException("A release names its own witness, observation and event.");
        }

        if (releasedAtUtc == default || releasedAtUtc < observation.RecordedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(releasedAtUtc), "A release cannot precede the observation it relies on.");
        }

        if (confirmationVersion != LocalExecutionOwnershipReleaseConfirmation.CurrentVersion
            || !LocalExecutionSequencePolicy.IsLowercaseSha256(confirmationSha256)
            || confirmationExpiresAtUtc <= releasedAtUtc
            || confirmationExpiresAtUtc - releasedAtUtc > LocalExecutionOwnershipReleaseConfirmation.MaximumLifetime)
        {
            throw new ArgumentException("The release confirmation is not a current, unexpired, bounded confirmation.");
        }

        if (!FactsAreCoherent(witness, observation, facts)
            || !string.Equals(
                confirmationSha256, LocalExecutionOwnershipReleaseConfirmation.Digest(facts, confirmationExpiresAtUtc), StringComparison.Ordinal))
        {
            throw new ArgumentException("The confirmed facts are not the exact facts of this witness, observation and confirmation.");
        }

        return new LocalExecutionOwnershipRelease
        {
            Id = id,
            Version = CurrentVersion,
            WitnessId = witness.Id,
            ObservationId = observation.Id,
            StepId = witness.StepId,
            EventId = eventId,
            ReleasedAtUtc = releasedAtUtc,
            ConfirmationVersion = confirmationVersion,
            ConfirmationSha256 = confirmationSha256,
            ConfirmationExpiresAtUtc = confirmationExpiresAtUtc,
            IntentVersion = CurrentIntentVersion,
            IntentFamily = facts.Family,
            IntentOperationId = facts.OperationId,
            IntentSequenceId = facts.SequenceId,
            IntentRunId = facts.RunId,
            IntentProjectId = facts.ProjectId,
            IntentWorkspaceId = facts.WorkspaceId,
            IntentSequenceState = facts.SequenceState,
            IntentAdmissionMode = facts.AdmissionMode,
            IntentAdmissionId = facts.AdmissionId,
            IntentKind = facts.Kind,
            IntentOrdinal = facts.Ordinal,
            IntentPassNumber = facts.PassNumber,
            IntentRecipeOrdinal = facts.RecipeOrdinal,
            IntentPredecessorStepId = facts.PredecessorStepId,
            IntentSourceCheckpointId = facts.SourceCheckpointId,
            IntentSourceFingerprintSha256 = facts.SourceFingerprintSha256,
            IntentReportMessageId = facts.ReportMessageId,
            IntentSubjectAttemptId = facts.SubjectAttemptId,
            IntentAgentResponseContract = facts.AgentResponseContract,
            IntentRecipeCommandId = facts.RecipeCommandId,
            IntentOperationStatus = facts.OperationStatus,
            IntentAgentOutcome = facts.AgentOutcome,
            IntentCompletionFingerprintSha256 = facts.CompletionFingerprintSha256,
            IntentProcessOutcome = facts.ProcessOutcome,
            IntentSemanticExitCode = facts.SemanticExitCode,
            IntentProcessDuration = TimeSpan.FromTicks(facts.ProcessDurationTicks),
            IntentPhysicalExitCode = facts.PhysicalExitCode,
            IntentObservedElapsed = TimeSpan.FromTicks(facts.ObservedElapsedTicks),
            IntentDispatchedAtUtc = facts.DispatchedAtUtc,
            IntentObservationRecordedAtUtc = facts.RecordedAtUtc,
            IntentConfirmationVersion = confirmationVersion,
            IntentConfirmationExpiresAtUtc = confirmationExpiresAtUtc,
        };
    }

    /// <summary>Whether <paramref name="facts"/> are the closed, family-consistent facts of exactly this witness and observation: the
    /// identities and tuple the witness pinned, the physical facts the observation recorded, a timed-out or cancelled process with no
    /// semantic exit code, the concrete result value of the operation's own family and no value of the other family.</summary>
    private static bool FactsAreCoherent(
        LocalExecutionDispatchWitness witness, LocalExecutionTerminationObservation observation, LocalExecutionOwnershipReleaseFacts facts)
    {
        var agent = facts.Family == LocalExecutionOperationFamily.Agent;
        return facts.WitnessId == witness.Id
            && facts.ObservationId == observation.Id
            && facts.StepId == witness.StepId
            && facts.Family == witness.OperationFamily
            && facts.OperationId == witness.OperationId
            && facts.SequenceId == witness.SequenceId
            && facts.RunId == witness.RunId
            && facts.ProjectId == witness.ProjectId
            && facts.WorkspaceId == witness.WorkspaceId
            && facts.Kind == witness.Kind
            && facts.Ordinal == witness.Ordinal
            && facts.PassNumber == witness.PassNumber
            && facts.RecipeOrdinal == witness.RecipeOrdinal
            && facts.PredecessorStepId == witness.PredecessorStepId
            && facts.SourceCheckpointId == witness.SourceCheckpointId
            && string.Equals(facts.SourceFingerprintSha256, witness.SourceFingerprintSha256, StringComparison.Ordinal)
            && facts.ReportMessageId == witness.ReportMessageId
            && facts.SubjectAttemptId == witness.SubjectAttemptId
            && facts.AgentResponseContract == witness.AgentResponseContract
            && facts.RecipeCommandId == witness.RecipeCommandId
            && facts.AdmissionId == witness.AdmissionId
            && facts.DispatchedAtUtc == witness.DispatchedAtUtc
            && facts.RecordedAtUtc == observation.RecordedAtUtc
            && facts.ProcessOutcome == observation.ProcessOutcome
            && facts.ProcessOutcome != ProcessOutcome.Exited
            && facts.SemanticExitCode is null
            && observation.SemanticExitCode is null
            && facts.ProcessDurationTicks == observation.ProcessDuration.Ticks
            && facts.PhysicalExitCode == observation.PhysicalExitCode
            && facts.ObservedElapsedTicks == observation.ObservedElapsed.Ticks
            && facts.SequenceState is LocalExecutionSequenceState.Stopped or LocalExecutionSequenceState.NeedsAttention
                or LocalExecutionSequenceState.Interrupted
            && !string.IsNullOrWhiteSpace(facts.OperationStatus)
            && LocalExecutionSequencePolicy.IsLowercaseSha256(facts.SourceFingerprintSha256)
            && (agent
                ? facts.AgentOutcome is not null && facts.AgentResponseContract is not null && facts.CompletionFingerprintSha256 is null
                    && facts.RecipeCommandId is null
                : facts.AgentOutcome is null && facts.AgentResponseContract is null && facts.RecipeCommandId is not null
                    && LocalExecutionSequencePolicy.IsLowercaseSha256(facts.CompletionFingerprintSha256));
    }

    public Guid Id { get; private set; }

    public int Version { get; private set; }

    public Guid WitnessId { get; private set; }

    public Guid ObservationId { get; private set; }

    public Guid StepId { get; private set; }

    /// <summary>The identity of the one Human run event appended by the same commit.</summary>
    public Guid EventId { get; private set; }

    public DateTimeOffset ReleasedAtUtc { get; private set; }

    public int ConfirmationVersion { get; private set; }

    public string ConfirmationSha256 { get; private set; }

    public DateTimeOffset ConfirmationExpiresAtUtc { get; private set; }

    // ---- ADR-0066: the exact confirmed tuple, one typed column per canonical fact ------------------------------------------------------

    public int IntentVersion { get; private set; }

    public LocalExecutionOperationFamily IntentFamily { get; private set; }

    public Guid IntentOperationId { get; private set; }

    public Guid IntentSequenceId { get; private set; }

    public Guid IntentRunId { get; private set; }

    public Guid IntentProjectId { get; private set; }

    public Guid IntentWorkspaceId { get; private set; }

    public LocalExecutionSequenceState IntentSequenceState { get; private set; }

    public LocalExecutionAdmissionMode IntentAdmissionMode { get; private set; }

    public Guid? IntentAdmissionId { get; private set; }

    public LocalExecutionStepKind IntentKind { get; private set; }

    public int IntentOrdinal { get; private set; }

    public int IntentPassNumber { get; private set; }

    public int? IntentRecipeOrdinal { get; private set; }

    public Guid? IntentPredecessorStepId { get; private set; }

    public Guid IntentSourceCheckpointId { get; private set; }

    public string IntentSourceFingerprintSha256 { get; private set; }

    public Guid? IntentReportMessageId { get; private set; }

    public Guid? IntentSubjectAttemptId { get; private set; }

    public AgentResponseContract? IntentAgentResponseContract { get; private set; }

    public Guid? IntentRecipeCommandId { get; private set; }

    public string IntentOperationStatus { get; private set; }

    /// <summary>The concrete Agent outcome the Human confirmed (Agent family only).</summary>
    public AgentOutcome? IntentAgentOutcome { get; private set; }

    /// <summary>The concrete verification completion fingerprint the Human confirmed (verification family only).</summary>
    public string? IntentCompletionFingerprintSha256 { get; private set; }

    public ProcessOutcome IntentProcessOutcome { get; private set; }

    public int? IntentSemanticExitCode { get; private set; }

    public TimeSpan IntentProcessDuration { get; private set; }

    public int IntentPhysicalExitCode { get; private set; }

    public TimeSpan IntentObservedElapsed { get; private set; }

    public DateTimeOffset IntentDispatchedAtUtc { get; private set; }

    public DateTimeOffset IntentObservationRecordedAtUtc { get; private set; }

    /// <summary>The confirmation version and exact expiry again, so a drift of either stored value is a mismatch of the snapshot with
    /// itself, decided relationally without recomputing the digest.</summary>
    public int IntentConfirmationVersion { get; private set; }

    public DateTimeOffset IntentConfirmationExpiresAtUtc { get; private set; }
}
