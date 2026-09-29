using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The persistence-to-manifest authority boundary: the reserved default rationale cannot be
/// submitted as guidance, and a stored authorization chain whose content is not canonical or whose
/// message envelopes are not exactly as recorded is refused on both an idempotent retry and a claim —
/// with fixed errors that never echo the stored text, no authorization consumed, and nothing sealed.
/// </summary>
public sealed partial class CreateReviewCorrectionAttemptCommandHandlerTests
{
    private const string GuidedText = "Keep the fix small.";
    private const string Marker = "SENTINEL-CORRUPT";

    private static string InstructionJson(string rationale) =>
        JsonSerializer.Serialize(new { instruction = ReviewCorrectionGuidance.FixedInstruction, rationale });

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property)!.SetValue(target, value);

    public static TheoryData<string> CorruptionKinds() => new()
    {
        // Stored content that is shape-valid but violates the accepted canonical form.
        "content-overlong",
        "content-unsafe",
        "content-control",
        "content-padded",
        "content-crlf",
        "content-not-nfc",
        "json-reordered",
        "json-spaced",
        "json-default-spaced",
        // HumanInstruction envelope and linkage.
        "instruction-provenance",
        "instruction-actor",
        "instruction-recipient",
        "instruction-protocol",
        "instruction-attempt",
        "instruction-reply",
        // Escalation message envelope.
        "escalation-provenance",
        "escalation-actor",
        "escalation-recipient",
        "escalation-protocol",
        "escalation-attempt",
    };

    private async Task ApplyCorruptionAsync(string kind, Seed seed, Guid instructionId, Guid escalationMessageId)
    {
        await using var context = _fixture.CreateContext();
        var instruction = await context.CollaborationMessages.SingleAsync(m => m.Id == instructionId);
        var escalationMessage = await context.CollaborationMessages.SingleAsync(m => m.Id == escalationMessageId);
        switch (kind)
        {
            case "content-overlong": Set(instruction, "StructuredContentJson", InstructionJson(Marker + new string('x', 700))); break;
            case "content-unsafe": Set(instruction, "StructuredContentJson", InstructionJson(Marker + " the secret note")); break;
            case "content-control": Set(instruction, "StructuredContentJson", InstructionJson(Marker + "\u0007bell")); break;
            case "content-padded": Set(instruction, "StructuredContentJson", InstructionJson("  " + Marker + " padded  ")); break;
            case "content-crlf": Set(instruction, "StructuredContentJson", InstructionJson(Marker + " a\r\nb")); break;
            case "content-not-nfc": Set(instruction, "StructuredContentJson", InstructionJson(Marker + " café")); break;
            case "json-reordered":
                Set(instruction, "StructuredContentJson",
                    "{\"rationale\":\"" + Marker + " ok\",\"instruction\":\"" + ReviewCorrectionGuidance.FixedInstruction + "\"}");
                break;
            case "json-spaced":
                Set(instruction, "StructuredContentJson",
                    "{ \"instruction\": \"" + ReviewCorrectionGuidance.FixedInstruction + "\", \"rationale\": \"" + Marker + " ok\" }");
                break;
            case "json-default-spaced":
                Set(instruction, "StructuredContentJson",
                    "{ \"instruction\": \"" + ReviewCorrectionGuidance.FixedInstruction + "\", \"rationale\": \"" + ReviewCorrectionGuidance.DefaultRationale + "\" }");
                break;
            case "instruction-provenance": Set(instruction, "Provenance", CollaborationMessageProvenance.HostConstructed); break;
            case "instruction-actor": Set(instruction, "ActorKind", ParticipantKind.Orchestrator); break;
            case "instruction-recipient": Set(instruction, "RecipientKind", ParticipantKind.Human); break;
            case "instruction-protocol": Set(instruction, "ProtocolVersion", "2.0"); break;
            case "instruction-attempt": Set(instruction, "AttemptId", seed.Review.Id); break;
            case "instruction-reply": Set(instruction, "InReplyToMessageId", seed.ExecutionReport.Id); break;
            case "escalation-provenance": Set(escalationMessage, "Provenance", CollaborationMessageProvenance.ProviderObserved); break;
            case "escalation-actor": Set(escalationMessage, "ActorKind", ParticipantKind.Human); break;
            case "escalation-recipient": Set(escalationMessage, "RecipientKind", ParticipantKind.Orchestrator); break;
            case "escalation-protocol": Set(escalationMessage, "ProtocolVersion", "2.0"); break;
            case "escalation-attempt": Set(escalationMessage, "AttemptId", seed.Review.Id); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        await context.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task The_reserved_default_rationale_is_rejected_as_guidance_and_never_becomes_a_silent_bodyless_success()
    {
        await using var context = _fixture.CreateContext();
        var (seed, escalationId) = await SeedEscalatedAsync(context);

        foreach (var reserved in new[]
                 {
                     ReviewCorrectionGuidance.DefaultRationale,
                     "  " + ReviewCorrectionGuidance.DefaultRationale + "\r\n",
                 })
        {
            var result = await AuthorizeAsync(context, seed, escalationId, reserved);
            Assert.Equal(GuidanceInvalidCode, Assert.Single(result.Errors).Code);
        }

        Assert.Empty(await context.ReviewCorrectionAuthorizations.ToListAsync());

        // Even after a real bodyless authorization exists, the reserved text as "guidance" is refused
        // outright instead of being reported as an idempotent success for somebody's bodyless request.
        Assert.True((await AuthorizeAsync(context, seed, escalationId, guidance: null)).IsSuccess);
        var afterBodyless = await AuthorizeAsync(context, seed, escalationId, ReviewCorrectionGuidance.DefaultRationale, minutes: 2);
        Assert.Equal(GuidanceInvalidCode, Assert.Single(afterBodyless.Errors).Code);
        var (created, manifest) = await ClaimAsync(context, seed, new TestArtifactStore());
        Assert.DoesNotContain("humanGuidance", manifest, StringComparison.Ordinal);
        Assert.Equal(created.AttemptId, (await context.ReviewCorrectionAuthorizations.SingleAsync()).ConsumedByAttemptId);
    }

    [Fact]
    public async Task A_guided_authorization_always_reaches_the_manifest_with_its_exact_text()
    {
        await using var context = _fixture.CreateContext();
        var (seed, escalationId) = await SeedEscalatedAsync(context);
        var authorized = await AuthorizeAsync(context, seed, escalationId, "  " + GuidedText + "  ");
        Assert.True(authorized.IsSuccess);

        var (_, manifest) = await ClaimAsync(context, seed, new TestArtifactStore());

        using var document = JsonDocument.Parse(manifest);
        Assert.Equal(GuidedText, document.RootElement.GetProperty("humanGuidance").GetProperty("text").GetString());
        Assert.Equal(authorized.Value.HumanInstructionMessageId, document.RootElement.GetProperty("humanGuidance").GetProperty("messageId").GetGuid());
    }

    [Theory]
    [MemberData(nameof(CorruptionKinds))]
    public async Task A_corrupted_stored_authorization_chain_is_refused_on_retry_and_on_claim_without_echo_consumption_or_sealing(string kind)
    {
        Seed seed;
        Guid escalationId;
        Guid instructionId;
        Guid escalationMessageId;
        await using (var seedContext = _fixture.CreateContext())
        {
            (seed, escalationId) = await SeedEscalatedAsync(seedContext);
            var authorized = await AuthorizeAsync(seedContext, seed, escalationId, GuidedText);
            Assert.True(authorized.IsSuccess);
            instructionId = authorized.Value.HumanInstructionMessageId;
            escalationMessageId = (await seedContext.ReviewCorrectionEscalations.AsNoTracking().SingleAsync(e => e.Id == escalationId)).CollaborationMessageId;
        }

        await ApplyCorruptionAsync(kind, seed, instructionId, escalationMessageId);

        // An idempotent retry with the very guidance that was originally accepted must not report success.
        await using (var retryContext = _fixture.CreateContext())
        {
            var retry = await AuthorizeAsync(retryContext, seed, escalationId, GuidedText, minutes: 2);
            Assert.True(retry.IsFailure, $"{kind}: a retry reported success for a corrupted chain.");
            var retryError = Assert.Single(retry.Errors);
            Assert.DoesNotContain(Marker, retryError.Description, StringComparison.Ordinal);
            Assert.DoesNotContain(GuidedText, retryError.Description, StringComparison.Ordinal);
        }

        // A claim must refuse before sealing anything and must not consume the authorization.
        var store = new TestArtifactStore();
        await using var claimContext = _fixture.CreateContext();
        var claim = await new CreateReviewCorrectionAttemptCommandHandler(
                claimContext, new RecordingEvidenceReader(seed.Evidence), store, new FixedTimeProvider(Now.AddMinutes(5)))
            .HandleAsync(Command(seed), CancellationToken.None);

        Assert.True(claim.IsFailure, $"{kind}: a claim accepted a corrupted chain.");
        var claimError = Assert.Single(claim.Errors);
        Assert.Equal(InstructionInvalidCode, claimError.Code);
        Assert.DoesNotContain(Marker, claimError.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(GuidedText, claimError.Description, StringComparison.Ordinal);

        await using var verify = _fixture.CreateContext();
        Assert.True((await verify.ReviewCorrectionAuthorizations.SingleAsync()).IsAvailable);
        Assert.Equal(6, await verify.Attempts.CountAsync(a => a.RunId == seed.Run.Id));
        Assert.Empty(store.DeletedSealedFiles);
        var runDirectory = Path.GetDirectoryName(Path.GetDirectoryName(
            store.GetPartialPath(seed.Run.Id, Guid.Empty, ArtifactPurpose.AgentContextManifest)))!;
        Assert.False(Directory.Exists(runDirectory), "A refused claim must not write or seal any manifest.");
    }
}
