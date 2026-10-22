namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One pending or reserved admission of a queued local execution cycle (ADR-0062), the feature-owned record of one eventual operation
/// step. Its exact kind, pass, recipe position, predecessor, source, report and correction subject are pinned when it is queued, so no
/// later latest input is ever selected. Waiting creates no operation step or preparation token and owns no workspace or global slot.
/// Reservation links its one ordinary Reserved step; the claim's own save consumes it. <see cref="AdmissionNumber"/> is a database
/// orderable Int64 that gives first-in-first-out fairness among queued steps; every successor receives a fresh, later number.
/// <see cref="Revision"/> is the concurrency token every transition advances.
/// </summary>
public sealed class LocalExecutionAdmission
{
    private LocalExecutionAdmission()
    {
        SourceFingerprintSha256 = string.Empty;
        WaitingReason = LocalExecutionWaitingReason.AwaitingAdmissionCheck;
    }

    public static LocalExecutionAdmission Enqueue(
        Guid id,
        Guid sequenceId,
        long admissionNumber,
        int ordinal,
        LocalExecutionStepKind kind,
        int passNumber,
        int? recipeOrdinal,
        Guid? predecessorStepId,
        Guid sourceCheckpointId,
        string sourceFingerprintSha256,
        Guid? reportMessageId,
        Guid? subjectAttemptId,
        DateTimeOffset enqueuedAtUtc)
    {
        if (id == Guid.Empty || sequenceId == Guid.Empty || sourceCheckpointId == Guid.Empty)
        {
            throw new ArgumentException("An admission requires durable identifiers.");
        }

        if (admissionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(admissionNumber));
        }

        if (ordinal is < 0 or >= 102 || !Enum.IsDefined(kind) || passNumber < 0 || passNumber > LocalExecutionSequencePolicy.MaximumPasses)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!LocalExecutionSequencePolicy.IsLowercaseSha256(sourceFingerprintSha256))
        {
            throw new ArgumentException("The source fingerprint must be a lowercase hexadecimal SHA-256 value.", nameof(sourceFingerprintSha256));
        }

        var isImplementation = kind == LocalExecutionStepKind.Implementation;
        var isVerification = kind == LocalExecutionStepKind.Verification;
        var isCorrection = LocalExecutionSequencePolicy.IsCorrectionKind(kind);
        if (isImplementation != (passNumber == 0)
            || isImplementation != (ordinal == 0)
            || isVerification != recipeOrdinal.HasValue
            || recipeOrdinal is < 0 or >= LocalExecutionSequencePolicy.MaximumRecipes
            || isImplementation != (predecessorStepId is null)
            || isImplementation != (reportMessageId is null)
            || isCorrection != subjectAttemptId.HasValue)
        {
            throw new ArgumentException("The admission shape does not match its kind.");
        }

        if (enqueuedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(enqueuedAtUtc));
        }

        return new LocalExecutionAdmission
        {
            Id = id,
            SequenceId = sequenceId,
            AdmissionNumber = admissionNumber,
            Ordinal = ordinal,
            Kind = kind,
            PassNumber = passNumber,
            RecipeOrdinal = recipeOrdinal,
            PredecessorStepId = predecessorStepId,
            SourceCheckpointId = sourceCheckpointId,
            SourceFingerprintSha256 = sourceFingerprintSha256,
            ReportMessageId = reportMessageId,
            SubjectAttemptId = subjectAttemptId,
            State = LocalExecutionAdmissionState.Waiting,
            WaitingReason = LocalExecutionWaitingReason.AwaitingAdmissionCheck,
            WaitingReasonRecordedAtUtc = enqueuedAtUtc,
            EnqueuedAtUtc = enqueuedAtUtc,
            Revision = 1,
            UpdatedAtUtc = enqueuedAtUtc,
        };
    }

    public Guid Id { get; private set; }

    public Guid SequenceId { get; private set; }

    /// <summary>The database-orderable, strictly increasing queue order. Never reused, wrapped or coerced.</summary>
    public long AdmissionNumber { get; private set; }

    /// <summary>The ordinal the operation step will have; unique per sequence for the whole lifetime of the cycle.</summary>
    public int Ordinal { get; private set; }

    public LocalExecutionStepKind Kind { get; private set; }

    public int PassNumber { get; private set; }

    public int? RecipeOrdinal { get; private set; }

    public Guid? PredecessorStepId { get; private set; }

    public Guid SourceCheckpointId { get; private set; }

    public string SourceFingerprintSha256 { get; private set; }

    public Guid? ReportMessageId { get; private set; }

    public Guid? SubjectAttemptId { get; private set; }

    public LocalExecutionAdmissionState State { get; private set; }

    /// <summary>The one ordinary Reserved step this admission created; null while Waiting or Retired without reservation.</summary>
    public Guid? StepId { get; private set; }

    /// <summary>The last durably recorded admission observation. It is never present worker, process or slot availability.</summary>
    public string WaitingReason { get; private set; }

    public DateTimeOffset WaitingReasonRecordedAtUtc { get; private set; }

    public DateTimeOffset EnqueuedAtUtc { get; private set; }

    public string? ReasonCode { get; private set; }

    public long Revision { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>A reservation or unresolved reservation: the admission owns its workspace and possibly a global unit.</summary>
    public bool IsHeld => State is LocalExecutionAdmissionState.Reserved or LocalExecutionAdmissionState.Unresolved;

    /// <summary>Records a changed waiting observation. A repeat of the recorded reason is not a change and records nothing.</summary>
    public bool RecordWaitingReason(string reason, DateTimeOffset nowUtc)
    {
        if (State != LocalExecutionAdmissionState.Waiting)
        {
            throw new InvalidOperationException($"A {State} admission is not waiting.");
        }

        if (!LocalExecutionWaitingReason.IsKnown(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), "Not a fixed waiting reason.");
        }

        if (string.Equals(reason, WaitingReason, StringComparison.Ordinal))
        {
            return false;
        }

        WaitingReason = reason;
        WaitingReasonRecordedAtUtc = nowUtc;
        Move(State, null, nowUtc);
        return true;
    }

    /// <summary>The reservation committed with the exact Reserved step it created.</summary>
    public void Reserve(Guid stepId, DateTimeOffset nowUtc)
    {
        if (State != LocalExecutionAdmissionState.Waiting)
        {
            throw new InvalidOperationException($"A {State} admission cannot be reserved.");
        }

        if (stepId == Guid.Empty)
        {
            throw new ArgumentException("A reservation requires its step.", nameof(stepId));
        }

        StepId = stepId;
        Move(LocalExecutionAdmissionState.Reserved, null, nowUtc);
    }

    /// <summary>The claim's own save transfers ownership to the operation.</summary>
    public void Consume(DateTimeOffset nowUtc)
    {
        if (State != LocalExecutionAdmissionState.Reserved)
        {
            throw new InvalidOperationException($"A {State} admission cannot be consumed.");
        }

        Move(LocalExecutionAdmissionState.Consumed, null, nowUtc);
    }

    /// <summary>Closes a Waiting admission, or a Reserved one whose unused shape the caller proved under the write lock.</summary>
    public void Retire(string reasonCode, DateTimeOffset nowUtc)
    {
        if (State is not (LocalExecutionAdmissionState.Waiting or LocalExecutionAdmissionState.Reserved))
        {
            throw new InvalidOperationException($"A {State} admission cannot be retired.");
        }

        if (!LocalExecutionSequenceReason.IsKnown(reasonCode))
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCode), "Not a fixed local execution sequence reason.");
        }

        Move(LocalExecutionAdmissionState.Retired, reasonCode, nowUtc);
    }

    /// <summary>A reservation that could not be proved unused or claimed stays held; nothing is retried or released.</summary>
    public void MarkUnresolved(string reasonCode, DateTimeOffset nowUtc)
    {
        if (State != LocalExecutionAdmissionState.Reserved)
        {
            throw new InvalidOperationException($"A {State} admission cannot become unresolved.");
        }

        if (!LocalExecutionSequenceReason.IsKnown(reasonCode))
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCode), "Not a fixed local execution sequence reason.");
        }

        Move(LocalExecutionAdmissionState.Unresolved, reasonCode, nowUtc);
    }

    private void Move(LocalExecutionAdmissionState next, string? reasonCode, DateTimeOffset nowUtc)
    {
        if (nowUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(nowUtc));
        }

        State = next;
        if (reasonCode is not null)
        {
            ReasonCode = reasonCode;
        }

        Revision++;
        UpdatedAtUtc = nowUtc;
    }
}
