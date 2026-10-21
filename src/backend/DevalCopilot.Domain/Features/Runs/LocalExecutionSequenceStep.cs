namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One operation step of a <see cref="LocalExecutionSequence"/> (ADR-0060). Its unique (SequenceId, Ordinal), fixed kind, pass,
/// predecessor and expected source identity are decided before any external work; its <see cref="ReservedOperationId"/> is chosen once
/// at reservation, so a claim that committed is recognized by that exact operation row (an Agent attempt or a verification execution),
/// never by a search for a latest one. The operation's own ordered inputs and evidence remain the single authoritative copy.
/// </summary>
public sealed class LocalExecutionSequenceStep
{
    private LocalExecutionSequenceStep()
    {
        SourceFingerprintSha256 = string.Empty;
    }

    public static LocalExecutionSequenceStep Reserve(
        Guid id,
        Guid sequenceId,
        int ordinal,
        LocalExecutionStepKind kind,
        int passNumber,
        int? recipeOrdinal,
        Guid? predecessorStepId,
        Guid sourceCheckpointId,
        string sourceFingerprintSha256,
        Guid? reportMessageId,
        Guid? subjectAttemptId,
        Guid reservedOperationId,
        Guid preparationOwnerToken,
        DateTimeOffset reservedAtUtc)
    {
        if (id == Guid.Empty || sequenceId == Guid.Empty || sourceCheckpointId == Guid.Empty
            || reservedOperationId == Guid.Empty || preparationOwnerToken == Guid.Empty)
        {
            throw new ArgumentException("A local execution step requires durable identifiers.");
        }

        if (ordinal is < 0 or >= 102)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        if (!Enum.IsDefined(kind))
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
        if (passNumber < 0 || passNumber > LocalExecutionSequencePolicy.MaximumPasses || isImplementation != (passNumber == 0)
            || isImplementation != (ordinal == 0)
            || isVerification != recipeOrdinal.HasValue
            || recipeOrdinal is < 0 or >= LocalExecutionSequencePolicy.MaximumRecipes
            || isImplementation != (predecessorStepId is null)
            || isImplementation != (reportMessageId is null)
            || isCorrection != subjectAttemptId.HasValue)
        {
            throw new ArgumentException("The step shape does not match its kind.");
        }

        if (reservedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(reservedAtUtc));
        }

        return new LocalExecutionSequenceStep
        {
            Id = id,
            SequenceId = sequenceId,
            Ordinal = ordinal,
            Kind = kind,
            PassNumber = passNumber,
            RecipeOrdinal = recipeOrdinal,
            PredecessorStepId = predecessorStepId,
            SourceCheckpointId = sourceCheckpointId,
            SourceFingerprintSha256 = sourceFingerprintSha256,
            ReportMessageId = reportMessageId,
            SubjectAttemptId = subjectAttemptId,
            State = LocalExecutionSequenceStepState.Reserved,
            ReservedOperationId = reservedOperationId,
            PreparationOwnerToken = preparationOwnerToken,
            PreparationReservedAtUtc = reservedAtUtc,
            PreparationDeadlineUtc = LocalExecutionSequencePolicy.PreparationDeadlineOf(reservedAtUtc),
            Revision = 1,
            UpdatedAtUtc = reservedAtUtc,
        };
    }

    public Guid Id { get; private set; }

    public Guid SequenceId { get; private set; }

    public int Ordinal { get; private set; }

    public LocalExecutionStepKind Kind { get; private set; }

    /// <summary>Zero for the implementation; otherwise the verification pass (one to three) the step belongs to or follows.</summary>
    public int PassNumber { get; private set; }

    /// <summary>The position of the pinned recipe a verification step runs; null for every other kind.</summary>
    public int? RecipeOrdinal { get; private set; }

    public Guid? PredecessorStepId { get; private set; }

    /// <summary>The checkpoint this step operates on: the starting checkpoint, or the exact result checkpoint of the linked report.</summary>
    public Guid SourceCheckpointId { get; private set; }

    public string SourceFingerprintSha256 { get; private set; }

    /// <summary>The exact linked ExecutionReport this step verifies, reviews, diagnoses or corrects; null only for the implementation.</summary>
    public Guid? ReportMessageId { get; private set; }

    /// <summary>For a correction: the exact linked review or diagnosis attempt whose whole finding set it applies.</summary>
    public Guid? SubjectAttemptId { get; private set; }

    public LocalExecutionSequenceStepState State { get; private set; }

    /// <summary>The operation identity chosen at reservation. A committed claim is exactly the operation row with this id.</summary>
    public Guid ReservedOperationId { get; private set; }

    /// <summary>Set once, in the same transaction as the operation, and equal to <see cref="ReservedOperationId"/>.</summary>
    public Guid? OperationId { get; private set; }

    public Guid PreparationOwnerToken { get; private set; }

    public DateTimeOffset PreparationReservedAtUtc { get; private set; }

    public DateTimeOffset PreparationDeadlineUtc { get; private set; }

    public DateTimeOffset? ClaimedAtUtc { get; private set; }

    /// <summary>Claim time plus the actual timeout plus the grace, capped by the advancement deadline. Set with the claim.</summary>
    public DateTimeOffset? ResultDeadlineUtc { get; private set; }

    public string? ReasonCode { get; private set; }

    public long Revision { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool IsAgent => LocalExecutionSequencePolicy.IsAgentKind(Kind);

    public bool IsOutstanding => State is LocalExecutionSequenceStepState.Reserved or LocalExecutionSequenceStepState.Claimed;

    public void MarkClaimed(Guid operationId, DateTimeOffset claimedAtUtc, DateTimeOffset resultDeadlineUtc)
    {
        if (State != LocalExecutionSequenceStepState.Reserved)
        {
            throw new InvalidOperationException($"A {State} step cannot be claimed.");
        }

        if (operationId != ReservedOperationId)
        {
            throw new ArgumentException("Only the reserved operation identity can be linked.", nameof(operationId));
        }

        OperationId = operationId;
        ClaimedAtUtc = claimedAtUtc;
        ResultDeadlineUtc = resultDeadlineUtc;
        Move(LocalExecutionSequenceStepState.Claimed, reasonCode: null, claimedAtUtc);
    }

    public void Conclude(DateTimeOffset nowUtc)
    {
        if (State != LocalExecutionSequenceStepState.Claimed)
        {
            throw new InvalidOperationException($"A {State} step cannot be concluded.");
        }

        Move(LocalExecutionSequenceStepState.Concluded, reasonCode: null, nowUtc);
    }

    public void Revoke(string reasonCode, DateTimeOffset nowUtc)
    {
        if (State != LocalExecutionSequenceStepState.Reserved)
        {
            throw new InvalidOperationException($"A {State} step cannot be revoked.");
        }

        Move(LocalExecutionSequenceStepState.Revoked, reasonCode, nowUtc);
    }

    public void MarkUnresolved(string reasonCode, DateTimeOffset nowUtc)
    {
        if (State != LocalExecutionSequenceStepState.Reserved)
        {
            throw new InvalidOperationException($"A {State} step cannot become unresolved.");
        }

        Move(LocalExecutionSequenceStepState.Unresolved, reasonCode, nowUtc);
    }

    private void Move(LocalExecutionSequenceStepState next, string? reasonCode, DateTimeOffset nowUtc)
    {
        if (nowUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(nowUtc));
        }

        if (reasonCode is not null && !LocalExecutionSequenceReason.IsKnown(reasonCode))
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCode), "Not a fixed local execution sequence reason.");
        }

        State = next;
        ReasonCode = reasonCode;
        Revision++;
        UpdatedAtUtc = nowUtc;
    }
}
