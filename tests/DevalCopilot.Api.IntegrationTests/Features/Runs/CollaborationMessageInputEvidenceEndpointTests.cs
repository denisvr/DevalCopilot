using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// Exercises the recorded-collaboration-input-provenance fields on
/// <c>GET /api/runs/{runId}/collaboration-messages/{messageId}/evidence</c> through the real Api
/// host: an honest <c>Empty</c> state for a legitimate attempt with none, a coherent ordered
/// <c>Recorded</c> set, and a fail-closed <c>Invalid</c> set for a gapped or foreign input
/// reference. The existing <c>evidenceStatus</c>/artifact behavior is unaffected; see
/// <see cref="GetCollaborationMessageEvidenceEndpointTests"/> for that coverage.
/// </summary>
public sealed class CollaborationMessageInputEvidenceEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string Fingerprint = new('a', 64);

    private const string ProposalContent =
        "{\"scope\":\"Ledger\",\"implementationSteps\":\"Add the table then the query\",\"risks\":\"Unbounded content\",\"verificationPlan\":\"Tests\",\"escalationPoints\":\"None expected\"}";

    private const string ChallengeContent =
        "{\"disputedItem\":\"Disputed item\",\"materialImpact\":\"Material impact\",\"reasoning\":\"Reasoning\",\"alternativeOrQuestion\":\"Alternative or question\"}";

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<Guid> SeedRunAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), name, $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, name, now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    [Fact]
    public async Task A_planner_attempt_with_no_recorded_inputs_reports_Empty_and_never_claims_omission()
    {
        var runId = await SeedRunAsync("No recorded inputs");
        Guid messageId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var now = DateTimeOffset.UtcNow;
            var attempt = Attempt.ClaimAgent(
                Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now, 1);
            attempt.MarkAgentDispatched(now.AddSeconds(1));
            attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now.AddSeconds(2), TestProcessEvidence.CleanExit);
            var message = CollaborationMessage.RecordAgent(
                attempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
                CollaborationMessageType.Proposal, null, "A proposal", ProposalContent, now.AddSeconds(3));
            messageId = message.Id;
            dbContext.Attempts.Add(attempt);
            dbContext.CollaborationMessages.Add(message);
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/collaboration-messages/{messageId}/evidence");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal("HasEvidence", root.GetProperty("evidenceStatus").GetString());
        Assert.Equal("Empty", root.GetProperty("inputMessagesStatus").GetString());
        Assert.Empty(root.GetProperty("inputMessages").EnumerateArray());
        Assert.False(root.GetProperty("inputMessagesOmitted").GetBoolean());
        Assert.Equal(0, root.GetProperty("inputMessageTotalCount").GetInt32());
    }

    [Fact]
    public async Task A_resolvers_ordered_proposal_and_challenges_are_reported_coherently_in_stored_order()
    {
        var runId = await SeedRunAsync("Coherent ordered inputs");
        Guid revisedProposalId;
        Guid proposalId;
        Guid challengeOneId;
        Guid challengeTwoId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var now = DateTimeOffset.UtcNow;

            var plannerAttempt = Attempt.ClaimAgent(
                Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now, 1);
            plannerAttempt.MarkAgentDispatched(now.AddSeconds(1));
            var proposal = CollaborationMessage.RecordAgent(
                plannerAttempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
                CollaborationMessageType.Proposal, null, "A proposal", ProposalContent, now.AddSeconds(2));
            plannerAttempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now.AddSeconds(2), TestProcessEvidence.CleanExit);
            proposalId = proposal.Id;

            var reviewAttempt = Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), runId, 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now.AddSeconds(3), 2);
            reviewAttempt.MarkAgentDispatched(now.AddSeconds(4));
            var challengeOne = CollaborationMessage.RecordAgent(
                reviewAttempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
                CollaborationMessageType.Challenge, proposal.Id, "A challenge", ChallengeContent, now.AddSeconds(5));
            var challengeTwo = CollaborationMessage.RecordAgent(
                reviewAttempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
                CollaborationMessageType.Challenge, proposal.Id, "Another challenge", ChallengeContent, now.AddSeconds(6));
            reviewAttempt.CompleteAgent(AgentOutcome.Challenged, Fingerprint, now.AddSeconds(6), TestProcessEvidence.CleanExit);
            challengeOneId = challengeOne.Id;
            challengeTwoId = challengeTwo.Id;

            var resolverAttempt = Attempt.ClaimAgentChallengeResolution(
                Guid.NewGuid(), runId, 3, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 262144, 524288, now.AddSeconds(7), 3);
            resolverAttempt.MarkAgentDispatched(now.AddSeconds(8));
            var revisedProposal = CollaborationMessage.RecordAgent(
                resolverAttempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
                CollaborationMessageType.Proposal, proposal.Id, "A revised proposal", ProposalContent, now.AddSeconds(9));
            revisedProposalId = revisedProposal.Id;

            dbContext.Attempts.AddRange(plannerAttempt, reviewAttempt, resolverAttempt);
            dbContext.CollaborationMessages.AddRange(proposal, challengeOne, challengeTwo, revisedProposal);
            dbContext.AttemptInputMessages.AddRange(
                AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, proposal.Id, sequence: 0),
                AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, challengeOne.Id, sequence: 1),
                AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, challengeTwo.Id, sequence: 2));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/collaboration-messages/{revisedProposalId}/evidence");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal("Recorded", root.GetProperty("inputMessagesStatus").GetString());
        Assert.False(root.GetProperty("inputMessagesOmitted").GetBoolean());
        Assert.Equal(3, root.GetProperty("inputMessageTotalCount").GetInt32());

        var entries = root.GetProperty("inputMessages").EnumerateArray().ToArray();
        Assert.Equal(3, entries.Length);
        Assert.Equal([0, 1, 2], entries.Select(entry => entry.GetProperty("sequence").GetInt32()));
        Assert.Equal(
            [proposalId, challengeOneId, challengeTwoId],
            entries.Select(entry => entry.GetProperty("collaborationMessageId").GetGuid()));
        Assert.Equal("Proposal", entries[0].GetProperty("type").GetString());
        Assert.Equal("Challenge", entries[1].GetProperty("type").GetString());
        Assert.Equal("Challenge", entries[2].GetProperty("type").GetString());

        // Never exposes the referenced message's own content — only durable identity/metadata.
        Assert.DoesNotContain("disputedItem", body, StringComparison.Ordinal);
        Assert.DoesNotContain("structuredContentJson", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_gap_in_the_stored_input_sequence_reports_Invalid_and_shows_no_partial_list()
    {
        var runId = await SeedRunAsync("Gapped inputs");
        Guid revisedProposalId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var now = DateTimeOffset.UtcNow;

            var plannerAttempt = Attempt.ClaimAgent(
                Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now, 1);
            plannerAttempt.MarkAgentDispatched(now.AddSeconds(1));
            var proposal = CollaborationMessage.RecordAgent(
                plannerAttempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
                CollaborationMessageType.Proposal, null, "A proposal", ProposalContent, now.AddSeconds(2));
            plannerAttempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now.AddSeconds(2), TestProcessEvidence.CleanExit);

            var reviewAttempt = Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), runId, 2, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, now.AddSeconds(3), 2);
            reviewAttempt.MarkAgentDispatched(now.AddSeconds(4));
            var challenge = CollaborationMessage.RecordAgent(
                reviewAttempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
                CollaborationMessageType.Challenge, proposal.Id, "A challenge", ChallengeContent, now.AddSeconds(5));
            reviewAttempt.CompleteAgent(AgentOutcome.Challenged, Fingerprint, now.AddSeconds(5), TestProcessEvidence.CleanExit);

            var resolverAttempt = Attempt.ClaimAgentChallengeResolution(
                Guid.NewGuid(), runId, 3, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 262144, 524288, now.AddSeconds(6), 3);
            resolverAttempt.MarkAgentDispatched(now.AddSeconds(7));
            var revisedProposal = CollaborationMessage.RecordAgent(
                resolverAttempt, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
                CollaborationMessageType.Proposal, proposal.Id, "A revised proposal", ProposalContent, now.AddSeconds(8));
            revisedProposalId = revisedProposal.Id;

            dbContext.Attempts.AddRange(plannerAttempt, reviewAttempt, resolverAttempt);
            dbContext.CollaborationMessages.AddRange(proposal, challenge, revisedProposal);
            // Sequence 1 is missing (0, then 2).
            dbContext.AttemptInputMessages.AddRange(
                AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, proposal.Id, sequence: 0),
                AttemptInputMessage.Record(Guid.NewGuid(), resolverAttempt.Id, challenge.Id, sequence: 2));
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/collaboration-messages/{revisedProposalId}/evidence");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal("HasEvidence", root.GetProperty("evidenceStatus").GetString());
        Assert.Equal("Invalid", root.GetProperty("inputMessagesStatus").GetString());
        Assert.Empty(root.GetProperty("inputMessages").EnumerateArray());
        Assert.Equal(2, root.GetProperty("inputMessageTotalCount").GetInt32());
    }
}
