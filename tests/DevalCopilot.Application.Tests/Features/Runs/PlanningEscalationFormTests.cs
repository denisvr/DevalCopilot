using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.AuthorizePlanningImplementation;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Queries.GetPlanningImplementationAuthorization;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.PlanningAuthorizationTestSupport;
using static DevalCopilot.Application.Tests.Features.Runs.SecondChallengeRoundTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The two complete canonical escalation forms (ADR-0020): the production writer records only the current one, while a
/// historical escalation in the original form stays a valid source for authorization, consumption and every downstream
/// check. Matching is whole-text ordinal equality with one complete form recomputed from validated identifiers: no mixed
/// form, no semantic JSON equivalence, no arbitrary wording. Both forms are reproduced here independently of production.
/// </summary>
public sealed class PlanningEscalationFormTests : IAsyncLifetime
{
    private static readonly Guid RunId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid Root = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid First = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Second = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid[] Challenges =
        [Guid.Parse("44444444-4444-4444-4444-444444444444"), Guid.Parse("55555555-5555-5555-5555-555555555555")];

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    // ---- The writer: the current form only, with every retained host fact -------------------------------------------

    private const string PinnedCurrentContent =
        "{\"unresolvedDecision\":\"The second and final challenge-resolution round produced a revised proposal that has no further automated review or resolution.\","
        + "\"options\":\"Inspect the final proposal and the second-round decisions, then either separately authorize one implementation of that exact final plan and explicitly request it, or request a new plan with a new explicit planning request. Neither is chosen by this record.\","
        + "\"consequences\":\"Authorizing permits exactly one implementation claim of that exact final plan. Only a durably committed implementation claim consumes the authorization: a refused request, or a claim that definitely does not commit, consumes nothing, and a committed claim stays consumed even if its execution later fails. A new plan replaces this lineage. This record selects nothing, grants nothing and approves nothing, and a third critical review or resolution is not available.\","
        + "\"evidence\":\"Root proposal 11111111-1111-1111-1111-111111111111; first revision 22222222-2222-2222-2222-222222222222; second revision 33333333-3333-3333-3333-333333333333; 2 second-round challenge(s) each decided once: 44444444-4444-4444-4444-444444444444, 55555555-5555-5555-5555-555555555555.\","
        + "\"recommendedChoice\":\"Inspect the final proposal and every second-round decision before choosing.\"}";

    private const string PinnedLegacyContent =
        "{\"unresolvedDecision\":\"The second and final challenge-resolution round produced a revised proposal that has no further automated review or resolution.\","
        + "\"options\":\"Decide manually whether the revised proposal is acceptable, or start a new explicit planning request. Neither is chosen by this record.\","
        + "\"consequences\":\"The revised proposal is not implementable through this lineage and is not approved; a third review is not available.\","
        + "\"evidence\":\"Root proposal 11111111-1111-1111-1111-111111111111; first revision 22222222-2222-2222-2222-222222222222; second revision 33333333-3333-3333-3333-333333333333; 2 second-round challenge(s) each decided once: 44444444-4444-4444-4444-444444444444, 55555555-5555-5555-5555-555555555555.\","
        + "\"recommendedChoice\":\"Read the second-round decisions before starting any new planning request.\"}";

    [Fact]
    public void The_oracle_forms_are_pinned_byte_for_byte()
    {
        Assert.Equal(PinnedLegacyContent, PlanningEscalationForms.Legacy(Root, First, Second, Challenges));
        Assert.Equal(PinnedCurrentContent, PlanningEscalationForms.Current(Root, First, Second, Challenges));
        Assert.NotEqual(PinnedLegacyContent, PinnedCurrentContent);
    }

    [Fact]
    public void The_retained_historical_serialization_is_unchanged_and_is_never_what_the_writer_records()
    {
        var legacy = PlanningEscalationLegacyForm.BuildStructuredContentJson(Root, First, Second, Challenges);

        Assert.Equal(PinnedLegacyContent, legacy);
        Assert.NotEqual(legacy, PlanningEscalation.BuildStructuredContentJson(Root, First, Second, Challenges));
        Assert.True(PlanningEscalation.IsCanonicalContent(legacy, Root, First, Second, Challenges));
        Assert.True(PlanningEscalation.IsCanonicalContent(PinnedCurrentContent, Root, First, Second, Challenges));
        Assert.False(PlanningEscalation.IsCanonicalContent(legacy, Root, First, Second, Challenges[..1]));
    }

    [Fact]
    public void The_writer_records_exactly_the_current_form_with_the_retained_host_facts()
    {
        var message = PlanningEscalation.Record(RunId, Root, First, Second, Challenges, Now);

        Assert.Equal(PinnedCurrentContent, message.StructuredContentJson);
        Assert.Equal("The second challenge-resolution round is complete and needs a human decision.", message.Summary);
        Assert.Equal(PlanningEscalation.Summary, message.Summary);
        Assert.Equal(CollaborationMessageType.Escalation, message.Type);
        Assert.Equal(CollaborationMessage.ProtocolVersionOne, message.ProtocolVersion);
        Assert.Equal("1.0", message.ProtocolVersion);
        Assert.Equal(CollaborationMessageProvenance.HostConstructed, message.Provenance);
        Assert.Equal(ParticipantIdentity.ForOrchestrator(), message.Actor);
        Assert.Equal(ParticipantIdentity.ForHuman(), message.Recipient);
        Assert.Null(message.AttemptId);
        Assert.Equal(Second, message.InReplyToMessageId);
        Assert.Equal(RunId, message.RunId);
    }

    [Fact]
    public void The_current_form_explains_the_human_options_without_choosing_granting_or_approving()
    {
        var content = JsonDocument.Parse(PlanningEscalation.BuildStructuredContentJson(Root, First, Second, Challenges)).RootElement;
        var all = string.Join('\n', content.EnumerateObject().Select(property => property.Value.GetString()));

        Assert.Equal(
            ["unresolvedDecision", "options", "consequences", "evidence", "recommendedChoice"],
            content.EnumerateObject().Select(property => property.Name));
        Assert.Contains("authorize one implementation of that exact final plan", content.GetProperty("options").GetString());
        Assert.Contains("explicitly request it", content.GetProperty("options").GetString());
        Assert.Contains("request a new plan", content.GetProperty("options").GetString());
        Assert.Contains("selects nothing, grants nothing and approves nothing", content.GetProperty("consequences").GetString());
        Assert.Contains("third critical review or resolution is not available", content.GetProperty("consequences").GetString());
        // The consumption boundary is the claim handler's, stated exactly: only a durably committed claim consumes.
        Assert.Contains("Only a durably committed implementation claim consumes the authorization", content.GetProperty("consequences").GetString());
        Assert.Contains("a refused request, or a claim that definitely does not commit, consumes nothing", content.GetProperty("consequences").GetString());
        Assert.Contains("a committed claim stays consumed even if its execution later fails", content.GetProperty("consequences").GetString());
        Assert.DoesNotContain("requesting it spends", all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("second-round decision", content.GetProperty("recommendedChoice").GetString());
        Assert.DoesNotContain("not implementable", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not approved", all, StringComparison.OrdinalIgnoreCase);
        // The identifier-derived evidence keeps its exact sentence.
        Assert.Equal(PlanningEscalationForms.Evidence(Root, First, Second, Challenges), content.GetProperty("evidence").GetString());
        // It stays valid collaboration content with every retained field.
        CollaborationMessageContentPolicy.Validate(CollaborationMessageType.Escalation, content.GetRawText());
    }

    // ---- Both complete forms authorize and are consumed exactly once ------------------------------------------------

    private async Task<PlanningImplementationAuthorizationQueryResult> ReadAsync(Guid runId, Guid escalationId)
    {
        await using var dbContext = _fixture.CreateContext();
        var result = await new GetPlanningImplementationAuthorizationQueryHandler(dbContext).HandleAsync(
            new GetPlanningImplementationAuthorizationQuery(runId, escalationId), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    public static TheoryData<EscalationForm> EveryForm => new() { EscalationForm.Writer, EscalationForm.Legacy, EscalationForm.Current };

    [Theory]
    [MemberData(nameof(EveryForm))]
    public async Task Either_complete_form_authorizes_and_its_grant_is_consumed_exactly_once(EscalationForm form)
    {
        await using var seed = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(seed, secondChallengeCount: 2, form: form);
        Assert.Equal(PlanningImplementationAuthorizationState.Absent, (await ReadAsync(lineage.RunId, lineage.Escalation.Id)).State);

        var authorization = await AuthorizeAsync(seed, lineage);

        Assert.True(authorization.IsSuccess, authorization.IsFailure ? authorization.Errors[0].Code : null);
        var available = await ReadAsync(lineage.RunId, lineage.Escalation.Id);
        Assert.Equal(PlanningImplementationAuthorizationState.Available, available.State);
        Assert.Null(available.ConsumedByAttemptId);

        var claim = await ClaimAsync(seed, lineage);

        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(claim.Value.AttemptId, (await GrantAsync(verify, lineage.RunId)).ConsumedByAttemptId);
        var consumed = await ReadAsync(lineage.RunId, lineage.Escalation.Id);
        Assert.Equal(PlanningImplementationAuthorizationState.Consumed, consumed.State);
        Assert.Equal(claim.Value.AttemptId, consumed.ConsumedByAttemptId);

        // A second claim is refused (the first attempt is still active, and the spent grant is never reusable either way)
        // and creates nothing.
        var again = await ClaimAsync(seed, lineage);
        Assert.True(again.IsFailure);
        Assert.Equal(claim.Value.AttemptId, (await GrantAsync(verify, lineage.RunId)).ConsumedByAttemptId);
        Assert.Equal(1, await verify.Attempts.CountAsync(attempt => attempt.RunId == lineage.RunId && attempt.AgentRole == AgentRole.Implementer));
        Assert.Equal(PlanningImplementationAuthorizationState.Consumed, (await ReadAsync(lineage.RunId, lineage.Escalation.Id)).State);
    }

    [Theory]
    [MemberData(nameof(EveryForm))]
    public async Task A_newer_independent_planner_proposal_makes_either_form_stale_without_a_grant(EscalationForm form)
    {
        await using var seed = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(seed, form: form);
        lineage.Seeder.AddRoot();
        await seed.SaveChangesAsync(CancellationToken.None);
        var evidence = new CountingEvidenceReader();

        var result = await AuthorizeHandler(seed, evidence).HandleAsync(
            new AuthorizePlanningImplementationCommand(
                lineage.RunId, lineage.Escalation.Id, Rationale),
            CancellationToken.None);

        Assert.Equal(PlanningImplementationAuthorizationErrors.SourceStaleCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, evidence.Captures);
        Assert.Empty(seed.PlanningImplementationAuthorizations);
        Assert.Equal(PlanningImplementationAuthorizationState.Stale, (await ReadAsync(lineage.RunId, lineage.Escalation.Id)).State);
    }

    // ---- Only a whole canonical form is a source ---------------------------------------------------------------------

    private static (Guid Root, Guid First, Guid Second, Guid[] Challenges) Ids(EscalatedLineage lineage) => (
        lineage.Root.Id,
        lineage.First.RevisedProposal.Id,
        lineage.FinalProposal.Id,
        lineage.SecondReview.Outputs.Select(challenge => challenge.Id).ToArray());

    private static string Forge(string mutation, EscalationForm baseForm, EscalatedLineage lineage)
    {
        var (root, first, second, challenges) = Ids(lineage);
        var other = baseForm == EscalationForm.Legacy ? EscalationForm.Current : EscalationForm.Legacy;
        var text = PlanningEscalationForms.Of(baseForm, root, first, second, challenges);
        var foreignText = PlanningEscalationForms.Of(other, root, first, second, challenges);
        JsonObject Members(string json) => JsonNode.Parse(json)!.AsObject();
        string Serialize(JsonObject members) => members.ToJsonString();

        switch (mutation)
        {
            case "options-from-the-other-form":
                var mixedOptions = Members(text);
                mixedOptions["options"] = Members(foreignText)["options"]!.GetValue<string>();
                return Serialize(mixedOptions);
            case "consequences-from-the-other-form":
                var mixedConsequences = Members(text);
                mixedConsequences["consequences"] = Members(foreignText)["consequences"]!.GetValue<string>();
                return Serialize(mixedConsequences);
            case "recommendation-from-the-other-form":
                var mixedRecommendation = Members(text);
                mixedRecommendation["recommendedChoice"] = Members(foreignText)["recommendedChoice"]!.GetValue<string>();
                return Serialize(mixedRecommendation);
            case "edited-wording":
                var edited = Members(text);
                edited["options"] = edited["options"]!.GetValue<string>().Replace("Neither is chosen", "Both are chosen");
                return Serialize(edited);
            case "case-changed":
                var cased = Members(text);
                cased["recommendedChoice"] = cased["recommendedChoice"]!.GetValue<string>().ToUpperInvariant();
                return Serialize(cased);
            case "trailing-space":
                var spaced = Members(text);
                spaced["consequences"] = spaced["consequences"]!.GetValue<string>() + " ";
                return Serialize(spaced);
            case "indented-semantically-equal":
                return JsonSerializer.Serialize(JsonDocument.Parse(text).RootElement, new JsonSerializerOptions { WriteIndented = true });
            case "reordered-members":
                var original = Members(text);
                var reordered = new JsonObject();
                foreach (var name in new[] { "recommendedChoice", "evidence", "consequences", "options", "unresolvedDecision" })
                {
                    reordered[name] = original[name]!.GetValue<string>();
                }

                return Serialize(reordered);
            case "duplicate-member":
                return text[..^1] + ",\"options\":\"Duplicate.\"}";
            case "extra-member":
                return text[..^1] + ",\"extra\":\"Unexpected.\"}";
            case "uppercase-identifiers":
                var upper = Members(text);
                upper["evidence"] = upper["evidence"]!.GetValue<string>().ToUpperInvariant();
                return Serialize(upper);
            case "swapped-root-and-first-revision":
                return PlanningEscalationForms.Of(baseForm, first, root, second, challenges);
            case "reordered-challenges":
                return PlanningEscalationForms.Of(baseForm, root, first, second, [.. challenges.Reverse()]);
            case "missing-challenge":
                return PlanningEscalationForms.Of(baseForm, root, first, second, challenges[..1]);
            case "foreign-challenge":
                return PlanningEscalationForms.Of(baseForm, root, first, second, [challenges[0], Guid.NewGuid()]);
            case "foreign-final-proposal":
                return PlanningEscalationForms.Of(baseForm, root, first, Guid.NewGuid(), challenges);
            case "unpublished-submitted-wording":
                var submitted = Members(text);
                submitted["consequences"] = "Authorizing permits exactly one implementation claim of that exact final plan and requesting it spends the authorization; a new plan replaces this lineage. This record selects nothing, grants nothing and approves nothing, and a third critical review or resolution is not available.";
                return Serialize(submitted);
            case "another-wording":
                var another = Members(text);
                another["consequences"] = "The revised proposal may be implemented through this lineage.";
                return Serialize(another);
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }

    public static TheoryData<string, EscalationForm> Forgeries()
    {
        var mutations = new[]
        {
            "options-from-the-other-form", "consequences-from-the-other-form", "recommendation-from-the-other-form",
            "edited-wording", "case-changed", "trailing-space", "indented-semantically-equal", "reordered-members",
            "duplicate-member", "extra-member", "uppercase-identifiers", "swapped-root-and-first-revision",
            "reordered-challenges", "missing-challenge", "foreign-challenge", "foreign-final-proposal", "another-wording",
            "unpublished-submitted-wording",
        };
        var data = new TheoryData<string, EscalationForm>();
        foreach (var mutation in mutations)
        {
            data.Add(mutation, EscalationForm.Legacy);
            data.Add(mutation, EscalationForm.Current);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Forgeries))]
    public async Task Only_a_whole_canonical_form_is_a_source_and_every_deviation_fails_closed_before_any_effect(
        string mutation, EscalationForm baseForm)
    {
        await using var seed = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(seed, secondChallengeCount: 2, form: baseForm);
        var forged = Forge(mutation, baseForm, lineage);
        Assert.NotEqual(lineage.Escalation.StructuredContentJson, forged);
        await seed.CollaborationMessages.Where(message => message.Id == lineage.Escalation.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(message => message.StructuredContentJson, forged));
        var evidence = new CountingEvidenceReader();

        await using var handlerContext = _fixture.CreateContext();
        var result = await AuthorizeHandler(handlerContext, evidence).HandleAsync(
            new AuthorizePlanningImplementationCommand(
                lineage.RunId, lineage.Escalation.Id, Rationale),
            CancellationToken.None);

        Assert.True(result.IsFailure, mutation);
        Assert.Equal(PlanningImplementationAuthorizationErrors.SourceInvalidCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, evidence.Captures);
        Assert.Empty(handlerContext.PlanningImplementationAuthorizations);
        Assert.Empty(handlerContext.CollaborationMessages.Where(message => message.Type == CollaborationMessageType.HumanInstruction));
        Assert.Equal(PlanningImplementationAuthorizationState.Invalid, (await ReadAsync(lineage.RunId, lineage.Escalation.Id)).State);
    }

    [Theory]
    [MemberData(nameof(EveryForm))]
    public async Task A_text_edited_after_authorization_refuses_the_claim_and_leaves_the_grant_unconsumed(EscalationForm form)
    {
        await using var seed = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(seed, form: form);
        Assert.True((await AuthorizeAsync(seed, lineage)).IsSuccess);
        await seed.CollaborationMessages.Where(message => message.Id == lineage.Escalation.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(
                message => message.StructuredContentJson,
                message => message.StructuredContentJson.Replace("Neither is chosen", "Both are chosen")));
        var evidence = new CountingEvidenceReader();
        var store = new RecordingArtifactStore();

        await using var claimContext = _fixture.CreateContext();
        var claim = await ClaimAsync(claimContext, lineage, evidence, store);

        Assert.True(claim.IsFailure);
        Assert.Equal(0, evidence.Captures);
        Assert.Equal(0, store.Seals);
        await using var verify = _fixture.CreateContext();
        Assert.Null((await GrantAsync(verify, lineage.RunId)).ConsumedByAttemptId);
        Assert.Empty(verify.Attempts.Where(attempt => attempt.AgentRole == AgentRole.Implementer));
        Assert.Equal(PlanningImplementationAuthorizationState.Invalid, (await ReadAsync(lineage.RunId, lineage.Escalation.Id)).State);
    }

    // ---- Authorship, provenance, ambiguity ---------------------------------------------------------------------------

    public static TheoryData<EscalationForm, string> AuthorshipForgeries()
    {
        var data = new TheoryData<EscalationForm, string>();
        foreach (var form in new[] { EscalationForm.Legacy, EscalationForm.Current })
        {
            foreach (var forgery in new[] { "provenance", "actor", "recipient", "attempt-owned", "protocol", "reply-to-first-revision" })
            {
                data.Add(form, forgery);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AuthorshipForgeries))]
    public async Task A_canonical_text_never_makes_forged_authorship_or_provenance_a_source(EscalationForm form, string forgery)
    {
        await using var seed = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(seed, form: form);
        var messages = seed.CollaborationMessages.Where(message => message.Id == lineage.Escalation.Id);
        switch (forgery)
        {
            case "provenance":
                await messages.ExecuteUpdateAsync(set => set.SetProperty(
                    message => message.Provenance, CollaborationMessageProvenance.ProviderObserved));
                break;
            case "actor":
                await messages.ExecuteUpdateAsync(set => set.SetProperty(message => message.ActorKind, ParticipantKind.Human));
                break;
            case "recipient":
                await messages.ExecuteUpdateAsync(set => set.SetProperty(message => message.RecipientKind, ParticipantKind.Orchestrator));
                break;
            case "attempt-owned":
                await messages.ExecuteUpdateAsync(set => set.SetProperty(message => message.AttemptId, lineage.Second.Attempt.Id));
                break;
            case "protocol":
                await messages.ExecuteUpdateAsync(set => set.SetProperty(message => message.ProtocolVersion, "2.0"));
                break;
            default:
                await messages.ExecuteUpdateAsync(set => set.SetProperty(
                    message => message.InReplyToMessageId, lineage.First.RevisedProposal.Id));
                break;
        }

        var evidence = new CountingEvidenceReader();
        await using var handlerContext = _fixture.CreateContext();
        var result = await AuthorizeHandler(handlerContext, evidence).HandleAsync(
            new AuthorizePlanningImplementationCommand(
                lineage.RunId, lineage.Escalation.Id, Rationale),
            CancellationToken.None);

        Assert.Equal(PlanningImplementationAuthorizationErrors.SourceInvalidCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, evidence.Captures);
        Assert.Empty(handlerContext.PlanningImplementationAuthorizations);
    }

    [Fact]
    public async Task Two_canonical_escalations_of_different_forms_for_one_final_plan_are_ambiguous()
    {
        await using var seed = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(seed, form: EscalationForm.Legacy);
        var (root, first, second, challenges) = Ids(lineage);
        seed.CollaborationMessages.Add(PlanningEscalationForms.Record(
            lineage.RunId, second, PlanningEscalationForms.Current(root, first, second, challenges), Now));
        await seed.SaveChangesAsync(CancellationToken.None);
        var evidence = new CountingEvidenceReader();

        var result = await AuthorizeHandler(seed, evidence).HandleAsync(
            new AuthorizePlanningImplementationCommand(
                lineage.RunId, lineage.Escalation.Id, Rationale),
            CancellationToken.None);

        Assert.Equal(PlanningImplementationAuthorizationErrors.SourceInvalidCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, evidence.Captures);
        Assert.Empty(seed.PlanningImplementationAuthorizations);
    }
}
