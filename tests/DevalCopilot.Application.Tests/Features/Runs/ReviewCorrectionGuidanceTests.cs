using System.Text.Json;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Commands.AuthorizeReviewCorrection;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Bounded human guidance for one authorized review correction: the bodyless authorization stays
/// exactly as it was, guidance is validated deterministically before persistence and persisted exactly in the linked
/// HumanInstruction, retries and races never report somebody else's authorization, and only a
/// coherent same-run, same-escalation chain reaches the sealed manifest — once, as advisory
/// human-submitted context, without touching the ordered ExecutionReport-then-findings inputs.
/// </summary>
public sealed partial class CreateReviewCorrectionAttemptCommandHandlerTests
{
    private const string LegacyBodylessInstructionJson =
        "{\"instruction\":\"Authorize one additional review-correction attempt.\",\"rationale\":\"Continue only after explicit human authorization.\"}";

    private const string GuidanceInvalidCode = "review_correction_authorizations.guidance_invalid";
    private const string GuidanceConflictCode = "review_correction_authorizations.guidance_conflict";
    private const string InstructionInvalidCode = "review_correction_authorizations.instruction_invalid";

    /// <summary>Delegates to a real test store but runs one injected action at the first seal — a
    /// point strictly after the claim read its authorization and strictly before its commit.</summary>
    private sealed class SealHookArtifactStore(TestArtifactStore inner, Func<Task> onFirstSeal) : IArtifactStore
    {
        private bool _hooked;

        public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => inner.GetPartialPath(runId, attemptId, purpose);

        public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) => inner.GetSealedRelativePath(runId, attemptId, purpose);

        public async Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken)
        {
            if (!_hooked)
            {
                _hooked = true;
                await onFirstSeal();
            }

            return await inner.SealAsync(runId, attemptId, purpose, cancellationToken);
        }

        public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => inner.HasSealedFile(runId, attemptId, purpose);

        public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => inner.HasPartialFile(runId, attemptId, purpose);

        public Task<SealedOutputFile?> DescribeSealedFileAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
            inner.DescribeSealedFileAsync(runId, attemptId, purpose, cancellationToken);

        public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => inner.DeleteOrphanedPartialFile(runId, attemptId, purpose);

        public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => inner.DeleteOrphanedSealedFile(runId, attemptId, purpose);

        public Task<PartialReadWindow> ReadPartialAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken) =>
            inner.ReadPartialAsync(runId, attemptId, purpose, fromOffset, maxBytes, cancellationToken);

        public Task<SealedReadWindow> VerifyAndReadSealedAsync(
            string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes,
            CancellationToken cancellationToken) =>
            inner.VerifyAndReadSealedAsync(relativeStoragePath, expectedByteLength, expectedContentHash, fromOffset, maxBytes, cancellationToken);
    }

    /// <summary>A run whose review-correction budget is exhausted and which already has its
    /// escalation: the state in which an authorization can actually be recorded and consumed.</summary>
    private async Task<(Seed Seed, Guid EscalationId)> SeedEscalatedAsync(
        DevalCopilotDbContext context, int maximumAgentAttempts = 16)
    {
        var seed = await SeedAsync(context, maximumAgentAttempts: maximumAgentAttempts, maximumAgentInvocationTime: TimeSpan.FromHours(24));
        for (var number = 5; number <= 6; number++)
        {
            var priorCorrection = Attempt.ClaimAgentReviewCorrection(
                Guid.NewGuid(), seed.Run.Id, number, seed.Workspace.Id, seed.Checkpoint!.Id, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 262144, 524288, Now, number);
            priorCorrection.MarkAgentDispatched(Now);
            priorCorrection.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
            context.Attempts.Add(priorCorrection);
        }

        await context.SaveChangesAsync(CancellationToken.None);
        var escalated = await Handler(context).HandleAsync(Command(seed), CancellationToken.None);
        var escalation = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.Escalated>(escalated.Value);
        return (seed, escalation.EscalationId);
    }

    private Task<Result<AuthorizeReviewCorrectionCommandResult>> AuthorizeAsync(
        DevalCopilotDbContext context, Seed seed, Guid escalationId, string? guidance, int minutes = 1) =>
        new AuthorizeReviewCorrectionCommandHandler(
                context, new RecordingEvidenceReader(seed.Evidence), new FixedTimeProvider(Now.AddMinutes(minutes)))
            .HandleAsync(new AuthorizeReviewCorrectionCommand(seed.Run.Id, escalationId, guidance), CancellationToken.None);

    private async Task<(CreateReviewCorrectionAttemptCommandResult.AttemptCreated Created, string Manifest)> ClaimAsync(
        DevalCopilotDbContext context, Seed seed, TestArtifactStore store)
    {
        var result = await new CreateReviewCorrectionAttemptCommandHandler(
                context, new RecordingEvidenceReader(seed.Evidence), store, new FixedTimeProvider(Now.AddMinutes(5)))
            .HandleAsync(Command(seed), CancellationToken.None);
        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        var manifest = await File.ReadAllTextAsync(store.GetPartialPath(seed.Run.Id, created.AttemptId, ArtifactPurpose.AgentContextManifest));
        return (created, manifest);
    }

    private static string[] TopLevelKeys(string manifest)
    {
        using var document = JsonDocument.Parse(manifest);
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    }

    private static string StructuredContentOf(DevalCopilotDbContext context, Guid messageId) =>
        context.CollaborationMessages.AsNoTracking().Single(message => message.Id == messageId).StructuredContentJson;

    [Fact]
    public async Task Bodyless_authorization_keeps_its_fixed_message_and_the_manifest_has_no_guidance()
    {
        await using var context = _fixture.CreateContext();
        var (seed, escalationId) = await SeedEscalatedAsync(context);

        var authorized = await AuthorizeAsync(context, seed, escalationId, guidance: null);

        Assert.True(authorized.IsSuccess);
        Assert.Equal(LegacyBodylessInstructionJson, StructuredContentOf(context, authorized.Value.HumanInstructionMessageId));
        var (created, manifest) = await ClaimAsync(context, seed, new TestArtifactStore());
        Assert.Equal(
            [
                "protocolVersion", "expectedResponseContract", "projectId", "runId", "workspaceId", "startingCheckpointId",
                "startingFingerprint", "objective", "instruction", "expectedOutputSchema", "untrustedEvidenceBoundary",
                "projectInstructionContextBoundary", "projectInstructionContext", "executionReport", "orderedFindings", "changeEvidence",
            ],
            TopLevelKeys(manifest));
        Assert.Equal(created.AttemptId, (await context.ReviewCorrectionAuthorizations.SingleAsync()).ConsumedByAttemptId);
    }

    [Fact]
    public async Task Guidance_is_normalized_once_persisted_exactly_and_never_echoed_in_metadata()
    {
        await using var context = _fixture.CreateContext();
        var (seed, escalationId) = await SeedEscalatedAsync(context);

        var authorized = await AuthorizeAsync(context, seed, escalationId, "  Keep the change small.\r\nPrefer   existing helpers.  \r\n");

        Assert.True(authorized.IsSuccess);
        const string expected = "Keep the change small.\nPrefer   existing helpers.";
        var message = await context.CollaborationMessages.AsNoTracking().SingleAsync(m => m.Id == authorized.Value.HumanInstructionMessageId);
        Assert.Equal(CollaborationMessageType.HumanInstruction, message.Type);
        Assert.Equal(CollaborationMessageProvenance.HumanSubmitted, message.Provenance);
        Assert.Equal(ParticipantIdentity.ForHuman(), message.Actor);
        Assert.Equal(ParticipantIdentity.ForOrchestrator(), message.Recipient);
        Assert.Null(message.AttemptId);
        var escalation = await context.ReviewCorrectionEscalations.AsNoTracking().SingleAsync(e => e.Id == escalationId);
        Assert.Equal(escalation.CollaborationMessageId, message.InReplyToMessageId);
        Assert.Equal(expected, ReviewCorrectionGuidance.TryReadRationale(message.StructuredContentJson));
        Assert.DoesNotContain("Keep the change", message.Summary, StringComparison.Ordinal);

        var events = await context.Events.AsNoTracking()
            .Where(e => e.RunId == seed.Run.Id && e.EventType == RunEventType.CollaborationMessageRecorded)
            .Select(e => e.PayloadJson)
            .ToListAsync();
        Assert.All(events, payload => Assert.DoesNotContain("Keep the change", payload, StringComparison.Ordinal));
        Assert.DoesNotContain("Keep the change", JsonSerializer.Serialize(authorized.Value), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_guided_claim_puts_the_exact_guidance_and_message_id_once_in_the_manifest_without_changing_input_identity()
    {
        await using var context = _fixture.CreateContext();
        var (seed, escalationId) = await SeedEscalatedAsync(context);
        const string guidance = "Please keep the fix minimal and preserve the public API.";
        var authorized = await AuthorizeAsync(context, seed, escalationId, guidance);
        Assert.True(authorized.IsSuccess);

        var (created, manifest) = await ClaimAsync(context, seed, new TestArtifactStore());

        var keys = TopLevelKeys(manifest);
        Assert.Equal("untrustedEvidenceBoundary", keys[10]);
        Assert.Equal("projectInstructionContextBoundary", keys[11]);
        Assert.Equal("projectInstructionContext", keys[12]);
        Assert.Equal("humanGuidanceBoundary", keys[13]);
        Assert.Equal("humanGuidance", keys[14]);
        Assert.Equal("executionReport", keys[15]);
        using (var document = JsonDocument.Parse(manifest))
        {
            var block = document.RootElement.GetProperty("humanGuidance");
            Assert.Equal(guidance, block.GetProperty("text").GetString());
            Assert.Equal(authorized.Value.HumanInstructionMessageId, block.GetProperty("messageId").GetGuid());
            Assert.Contains("advisory", document.RootElement.GetProperty("humanGuidanceBoundary").GetString(), StringComparison.Ordinal);
            Assert.Contains("cannot change the objective", document.RootElement.GetProperty("humanGuidanceBoundary").GetString(), StringComparison.Ordinal);
            Assert.Equal(
                ReviewCorrectionContextManifestBuilderInstruction,
                document.RootElement.GetProperty("instruction").GetString());
        }

        Assert.Equal(1, CountOccurrences(manifest, guidance));
        Assert.Equal(1, CountOccurrences(manifest, authorized.Value.HumanInstructionMessageId.ToString()));

        // ADR-0010: the ordered inputs remain exactly the ExecutionReport then the ReviewFindings.
        var inputs = await context.AttemptInputMessages.AsNoTracking()
            .Where(input => input.AttemptId == created.AttemptId)
            .OrderBy(input => input.Sequence)
            .Select(input => input.CollaborationMessageId)
            .ToListAsync();
        Assert.Equal([seed.ExecutionReport.Id, .. seed.Findings.Select(finding => finding.Id)], inputs);
        Assert.DoesNotContain(authorized.Value.HumanInstructionMessageId, inputs);
    }

    private const string ReviewCorrectionContextManifestBuilderInstruction =
        "Correct the reviewed implementation in the isolated working directory. Address every " +
        "finding exactly once, preserve the intended objective, and report only repository-relative " +
        "paths changed by this correction. Do not run Git, verification, package installation, or " +
        "network commands.";

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public async Task Guidance_at_the_maximum_length_is_accepted()
    {
        await using var context = _fixture.CreateContext();
        var (seed, escalationId) = await SeedEscalatedAsync(context);
        var guidance = new string('g', ReviewCorrectionGuidance.MaximumLength);

        var authorized = await AuthorizeAsync(context, seed, escalationId, guidance);

        Assert.True(authorized.IsSuccess);
        Assert.Equal(guidance, ReviewCorrectionGuidance.TryReadRationale(StructuredContentOf(context, authorized.Value.HumanInstructionMessageId)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t  ")]
    [InlineData("bell\u0007char")]
    [InlineData("the api key is here")]
    [InlineData("Read C:\\Users\\me\\notes")]
    [InlineData("send a Bearer token")]
    public async Task Invalid_guidance_is_rejected_without_recording_anything_or_echoing_it(string guidance)
    {
        await using var context = _fixture.CreateContext();
        var (seed, escalationId) = await SeedEscalatedAsync(context);

        var result = await AuthorizeAsync(context, seed, escalationId, guidance);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(GuidanceInvalidCode, error.Code);
        if (guidance.Trim().Length > 0)
        {
            Assert.DoesNotContain(guidance.Trim(), error.Description, StringComparison.Ordinal);
        }

        Assert.Empty(await context.ReviewCorrectionAuthorizations.ToListAsync());
        Assert.Empty(await context.CollaborationMessages.Where(m => m.Type == CollaborationMessageType.HumanInstruction).ToListAsync());
    }

    [Fact]
    public async Task Over_length_guidance_is_rejected_without_recording_anything()
    {
        await using var context = _fixture.CreateContext();
        var (seed, escalationId) = await SeedEscalatedAsync(context);

        var result = await AuthorizeAsync(context, seed, escalationId, new string('x', ReviewCorrectionGuidance.MaximumLength + 1));

        Assert.Equal(GuidanceInvalidCode, Assert.Single(result.Errors).Code);
        Assert.Empty(await context.ReviewCorrectionAuthorizations.ToListAsync());
    }

    [Fact]
    public async Task An_identical_retry_is_idempotent_even_when_only_the_normalization_differs()
    {
        await using var context = _fixture.CreateContext();
        var (seed, escalationId) = await SeedEscalatedAsync(context);
        var first = await AuthorizeAsync(context, seed, escalationId, "Keep it small.\nAvoid new files.");

        await using var retryContext = _fixture.CreateContext();
        var retry = await AuthorizeAsync(retryContext, seed, escalationId, "  Keep it small.\r\nAvoid new files.  ", minutes: 2);

        Assert.True(first.IsSuccess);
        Assert.True(retry.IsSuccess);
        Assert.Equal(first.Value.AuthorizationId, retry.Value.AuthorizationId);
        Assert.Equal(first.Value.HumanInstructionMessageId, retry.Value.HumanInstructionMessageId);
        await using var verify = _fixture.CreateContext();
        Assert.Single(await verify.ReviewCorrectionAuthorizations.ToListAsync());
        Assert.Single(await verify.CollaborationMessages.Where(m => m.Type == CollaborationMessageType.HumanInstruction).ToListAsync());
    }

    [Theory]
    [InlineData("first guidance", "second guidance")]
    [InlineData("first guidance", null)]
    [InlineData(null, "second guidance")]
    public async Task A_request_with_different_guidance_gets_a_safe_conflict_and_never_someone_elses_authorization(
        string? firstGuidance, string? secondGuidance)
    {
        await using var context = _fixture.CreateContext();
        var (seed, escalationId) = await SeedEscalatedAsync(context);
        var first = await AuthorizeAsync(context, seed, escalationId, firstGuidance);
        Assert.True(first.IsSuccess);

        await using var otherContext = _fixture.CreateContext();
        var second = await AuthorizeAsync(otherContext, seed, escalationId, secondGuidance, minutes: 2);

        Assert.True(second.IsFailure);
        var error = Assert.Single(second.Errors);
        Assert.Equal(GuidanceConflictCode, error.Code);
        Assert.DoesNotContain("guidance", error.Description.Replace("different guidance", string.Empty), StringComparison.Ordinal);
        Assert.DoesNotContain("first", error.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("second", error.Description, StringComparison.Ordinal);
        await using var verify = _fixture.CreateContext();
        var authorization = await verify.ReviewCorrectionAuthorizations.SingleAsync();
        Assert.Equal(first.Value.AuthorizationId, authorization.Id);
        Assert.Single(await verify.CollaborationMessages.Where(m => m.Type == CollaborationMessageType.HumanInstruction).ToListAsync());
    }

    [Fact]
    public async Task Concurrent_submissions_with_different_guidance_record_exactly_one_and_conflict_the_other()
    {
        await using var seedContext = _fixture.CreateContext();
        var (seed, escalationId) = await SeedEscalatedAsync(seedContext);
        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();

        var results = await Task.WhenAll(
            Task.Run(() => AuthorizeAsync(firstContext, seed, escalationId, "guidance from the first submitter")),
            Task.Run(() => AuthorizeAsync(secondContext, seed, escalationId, "guidance from the second submitter")));

        var winner = Assert.Single(results, result => result.IsSuccess);
        var loser = Assert.Single(results, result => result.IsFailure);
        Assert.Equal(GuidanceConflictCode, Assert.Single(loser.Errors).Code);
        await using var verify = _fixture.CreateContext();
        var authorization = await verify.ReviewCorrectionAuthorizations.SingleAsync();
        Assert.Equal(winner.Value.AuthorizationId, authorization.Id);
        Assert.Single(await verify.CollaborationMessages.Where(m => m.Type == CollaborationMessageType.HumanInstruction).ToListAsync());
    }

    [Fact]
    public async Task Concurrent_identical_submissions_both_succeed_with_the_same_authorization()
    {
        await using var seedContext = _fixture.CreateContext();
        var (seed, escalationId) = await SeedEscalatedAsync(seedContext);
        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();

        var results = await Task.WhenAll(
            Task.Run(() => AuthorizeAsync(firstContext, seed, escalationId, "the same guidance")),
            Task.Run(() => AuthorizeAsync(secondContext, seed, escalationId, "the same guidance")));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(results[0].Value.AuthorizationId, results[1].Value.AuthorizationId);
        await using var verify = _fixture.CreateContext();
        Assert.Single(await verify.ReviewCorrectionAuthorizations.ToListAsync());
    }

    [Fact]
    public async Task A_corrupt_persisted_authorization_message_fails_a_retry_closed()
    {
        await using var context = _fixture.CreateContext();
        var (seed, escalationId) = await SeedEscalatedAsync(context);
        var escalation = await context.ReviewCorrectionEscalations.SingleAsync(e => e.Id == escalationId);
        var corrupt = CollaborationMessage.RecordHumanInstruction(
            Guid.NewGuid(), seed.Run.Id, escalation.CollaborationMessageId,
            "{\"instruction\":\"Do something else entirely.\",\"rationale\":\"x\"}", Now);
        context.CollaborationMessages.Add(corrupt);
        context.ReviewCorrectionAuthorizations.Add(
            ReviewCorrectionAuthorization.Create(Guid.NewGuid(), seed.Run.Id, escalationId, corrupt.Id, Now));
        await context.SaveChangesAsync(CancellationToken.None);

        var retry = await AuthorizeAsync(context, seed, escalationId, guidance: null, minutes: 2);

        Assert.Equal(InstructionInvalidCode, Assert.Single(retry.Errors).Code);
    }

    private async Task<(Seed Seed, Guid EscalationId, ReviewCorrectionAuthorization Authorization)> SeedAuthorizationWithMessageAsync(
        DevalCopilotDbContext context, Func<Seed, ReviewCorrectionEscalation, CollaborationMessage> createMessage)
    {
        var (seed, escalationId) = await SeedEscalatedAsync(context);
        var escalation = await context.ReviewCorrectionEscalations.SingleAsync(e => e.Id == escalationId);
        var message = createMessage(seed, escalation);
        context.CollaborationMessages.Add(message);
        var authorization = ReviewCorrectionAuthorization.Create(Guid.NewGuid(), seed.Run.Id, escalationId, message.Id, Now);
        context.ReviewCorrectionAuthorizations.Add(authorization);
        await context.SaveChangesAsync(CancellationToken.None);
        return (seed, escalationId, authorization);
    }

    public static TheoryData<string> IncoherentLinkKinds() => new() { "wrong-reply", "foreign-run", "corrupt-content", "not-an-instruction" };

    [Theory]
    [MemberData(nameof(IncoherentLinkKinds))]
    public async Task An_incoherent_authorization_link_fails_the_claim_closed_without_consuming_or_sealing(string kind)
    {
        await using var context = _fixture.CreateContext();
        var foreignProject = DevalCopilot.Domain.Features.Projects.Project.Register(Guid.NewGuid(), "Other", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var foreignRun = Run.RecordIntent(Guid.NewGuid(), foreignProject.Id, 1, "Another run", Now);
        context.Projects.Add(foreignProject);
        context.Runs.Add(foreignRun);
        await context.SaveChangesAsync(CancellationToken.None);
        var (seed, _, authorization) = await SeedAuthorizationWithMessageAsync(context, (s, escalation) => kind switch
        {
            // Replies to some other message than the escalation this authorization belongs to.
            "wrong-reply" => CollaborationMessage.RecordHumanInstruction(
                Guid.NewGuid(), s.Run.Id, s.ExecutionReport.Id, ReviewCorrectionGuidance.BuildStructuredContentJson("someone else"), Now),
            // Recorded for a different run entirely.
            "foreign-run" => CollaborationMessage.RecordHumanInstruction(
                Guid.NewGuid(), foreignRun.Id, escalation.CollaborationMessageId, ReviewCorrectionGuidance.BuildStructuredContentJson("foreign"), Now),
            // Passes the ledger's own field policy but is not the fixed authorization instruction.
            "corrupt-content" => CollaborationMessage.RecordHumanInstruction(
                Guid.NewGuid(), s.Run.Id, escalation.CollaborationMessageId, "{\"instruction\":\"Do something else.\",\"rationale\":\"x\"}", Now),
            // Points at the escalation message itself rather than a HumanInstruction.
            _ => null!,
        }, kind);
        var store = new TestArtifactStore();

        var result = await new CreateReviewCorrectionAttemptCommandHandler(
                context, new RecordingEvidenceReader(seed.Evidence), store, new FixedTimeProvider(Now.AddMinutes(5)))
            .HandleAsync(Command(seed), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(InstructionInvalidCode, Assert.Single(result.Errors).Code);
        Assert.DoesNotContain("someone else", string.Join(' ', result.Errors.Select(e => e.Description)), StringComparison.Ordinal);
        await using var verify = _fixture.CreateContext();
        Assert.True((await verify.ReviewCorrectionAuthorizations.SingleAsync(a => a.Id == authorization.Id)).IsAvailable);
        Assert.Equal(6, await verify.Attempts.CountAsync(a => a.RunId == seed.Run.Id));
        Assert.Empty(store.DeletedSealedFiles);
    }

    private async Task<(Seed Seed, Guid EscalationId, ReviewCorrectionAuthorization Authorization)> SeedAuthorizationWithMessageAsync(
        DevalCopilotDbContext context, Func<Seed, ReviewCorrectionEscalation, CollaborationMessage> createMessage, string kind)
    {
        if (kind != "not-an-instruction")
        {
            return await SeedAuthorizationWithMessageAsync(context, createMessage);
        }

        var (seed, escalationId) = await SeedEscalatedAsync(context);
        var escalation = await context.ReviewCorrectionEscalations.SingleAsync(e => e.Id == escalationId);
        var authorization = ReviewCorrectionAuthorization.Create(
            Guid.NewGuid(), seed.Run.Id, escalationId, escalation.CollaborationMessageId, Now);
        context.ReviewCorrectionAuthorizations.Add(authorization);
        await context.SaveChangesAsync(CancellationToken.None);
        return (seed, escalationId, authorization);
    }

    [Fact]
    public async Task A_globally_exhausted_run_refuses_the_claim_and_leaves_the_guided_authorization_unconsumed()
    {
        await using var context = _fixture.CreateContext();
        // Chain (4) + two prior corrections (2) use six slots; a ceiling of eight leaves room for the
        // escalation and authorization, then two filler attempts exhaust it.
        var (seed, escalationId) = await SeedEscalatedAsync(context, maximumAgentAttempts: 8);
        var authorized = await AuthorizeAsync(context, seed, escalationId, "guidance that must stay unconsumed");
        Assert.True(authorized.IsSuccess);
        for (var number = 7; number <= 8; number++)
        {
            var filler = Attempt.ClaimAgent(
                Guid.NewGuid(), seed.Run.Id, number, seed.Workspace.Id, seed.Checkpoint!.Id, Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 262144, 524288, Now, number);
            filler.Fail(Now);
            context.Attempts.Add(filler);
        }

        await context.SaveChangesAsync(CancellationToken.None);
        var store = new TestArtifactStore();

        var result = await new CreateReviewCorrectionAttemptCommandHandler(
                context, new RecordingEvidenceReader(seed.Evidence), store, new FixedTimeProvider(Now.AddMinutes(5)))
            .HandleAsync(Command(seed), CancellationToken.None);

        Assert.Equal("agent_attempts.budget_exhausted", Assert.Single(result.Errors).Code);
        Assert.True((await context.ReviewCorrectionAuthorizations.SingleAsync()).IsAvailable);
        Assert.Empty(store.DeletedSealedFiles);
    }

    [Fact]
    public async Task An_authorization_is_consumed_once_and_a_later_claim_never_reuses_its_guidance()
    {
        await using var context = _fixture.CreateContext();
        var (seed, escalationId) = await SeedEscalatedAsync(context);
        Assert.True((await AuthorizeAsync(context, seed, escalationId, "one-time guidance")).IsSuccess);
        var (created, manifest) = await ClaimAsync(context, seed, new TestArtifactStore());
        Assert.Contains("one-time guidance", manifest, StringComparison.Ordinal);

        // Complete the correction so a new claim is not blocked by a running attempt.
        var attempt = await context.Attempts.SingleAsync(a => a.Id == created.AttemptId);
        attempt.MarkAgentDispatched(Now.AddMinutes(6));
        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now.AddMinutes(7));
        await context.SaveChangesAsync(CancellationToken.None);

        var second = await new CreateReviewCorrectionAttemptCommandHandler(
                context, new RecordingEvidenceReader(seed.Evidence), new TestArtifactStore(), new FixedTimeProvider(Now.AddMinutes(8)))
            .HandleAsync(Command(seed), CancellationToken.None);

        // The authorization is spent, so this is back to the escalation path: no new attempt, no guidance.
        Assert.IsType<CreateReviewCorrectionAttemptCommandResult.Escalated>(second.Value);
        Assert.Equal(created.AttemptId, (await context.ReviewCorrectionAuthorizations.SingleAsync()).ConsumedByAttemptId);
        Assert.Equal(7, await context.Attempts.CountAsync(a => a.RunId == seed.Run.Id));
    }

    // A competing claim consumes the authorization after this claim read it and before this claim
    // commits: exactly one correction exists, the loser's sealed manifest is removed, and the
    // guidance is bound to the winner's snapshot alone.
    [Fact]
    public async Task A_claim_that_loses_the_authorization_race_leaves_one_correction_and_no_orphaned_manifest()
    {
        await using var seedContext = _fixture.CreateContext();
        var (seed, escalationId) = await SeedEscalatedAsync(seedContext);
        Assert.True((await AuthorizeAsync(seedContext, seed, escalationId, "guidance for the winner only")).IsSuccess);

        await using var winnerContext = _fixture.CreateContext();
        await using var loserContext = _fixture.CreateContext();
        var winnerStore = new TestArtifactStore();
        var loserInner = new TestArtifactStore();
        Guid winnerAttemptId = Guid.Empty;
        var loserStore = new SealHookArtifactStore(loserInner, async () =>
        {
            var winner = await new CreateReviewCorrectionAttemptCommandHandler(
                    winnerContext, new RecordingEvidenceReader(seed.Evidence), winnerStore, new FixedTimeProvider(Now.AddMinutes(5)))
                .HandleAsync(Command(seed), CancellationToken.None);
            winnerAttemptId = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(winner.Value).AttemptId;
        });

        var loser = await new CreateReviewCorrectionAttemptCommandHandler(
                loserContext, new RecordingEvidenceReader(seed.Evidence), loserStore, new FixedTimeProvider(Now.AddMinutes(5)))
            .HandleAsync(Command(seed), CancellationToken.None);

        Assert.True(loser.IsFailure);
        Assert.NotEqual(Guid.Empty, winnerAttemptId);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(winnerAttemptId, (await verify.ReviewCorrectionAuthorizations.SingleAsync()).ConsumedByAttemptId);
        Assert.Equal(7, await verify.Attempts.CountAsync(a => a.RunId == seed.Run.Id));
        Assert.Single(loserInner.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest && entry.AttemptId != winnerAttemptId);
        Assert.Empty(winnerStore.DeletedSealedFiles);
    }

    [Fact]
    public async Task Maximum_guidance_never_displaces_the_bounded_evidence_and_stays_within_the_manifest_bound()
    {
        await using var context = _fixture.CreateContext();
        var (seed, escalationId) = await SeedEscalatedAsync(context);
        Assert.True((await AuthorizeAsync(context, seed, escalationId, new string('m', ReviewCorrectionGuidance.MaximumLength))).IsSuccess);
        var oversizedEvidence = seed.Evidence with { CompleteDiff = new string('d', 64 * 1024) };
        var store = new TestArtifactStore();

        var result = await new CreateReviewCorrectionAttemptCommandHandler(
                context, new RecordingEvidenceReader(oversizedEvidence), store, new FixedTimeProvider(Now.AddMinutes(5)))
            .HandleAsync(Command(seed), CancellationToken.None);

        // The diff is inlined only up to its own 8 KiB cap, so the document stays within bounds and
        // the claim succeeds; this proves the guidance never displaces the bounded evidence rules.
        var created = Assert.IsType<CreateReviewCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        var manifest = await File.ReadAllTextAsync(store.GetPartialPath(seed.Run.Id, created.AttemptId, ArtifactPurpose.AgentContextManifest));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(manifest) <= 32 * 1024);
        Assert.Contains("\"diffTruncated\":true", manifest, StringComparison.Ordinal);
    }
}
