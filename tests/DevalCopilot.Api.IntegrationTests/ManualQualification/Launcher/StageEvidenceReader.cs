using DevalCopilot.Api.Features.Runs.GetAgentAttemptEvidence;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// Builds one <see cref="StageReading"/> from the public status, timeline and message-evidence routes, supplemented by read-only
/// queries of the disposable rows and the sealed artifacts under the owned artifact root. It writes nothing, executes nothing, and
/// keeps no content: a manifest or artifact is read only to compare its length, its hash and three named members. The reader is used
/// by one session, one read at a time, and remembers for each role what it asked of the message-evidence route (see
/// <see cref="MessageEvidenceMemory"/>), so that the ceiling of requests holds for the whole stage however often it is read again.
/// </summary>
public sealed class StageEvidenceReader(
    IServiceProvider services,
    QualificationApiClient api,
    string artifactRoot,
    string objective)
{
    private readonly Dictionary<AllowanceRole, MessageEvidenceMemory> _evidenceRequests = [];

    public async Task<StageReading> ReadAsync(AllowanceRole role, Guid runId, CancellationToken cancellationToken)
    {
        var (provider, agentRole, contract) = role == AllowanceRole.Planner
            ? (AgentProvider.Codex, AgentRole.Planner, "Proposal")
            : (AgentProvider.ClaudeCode, AgentRole.CriticalReviewer, "CriticalReview");

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var attempts = await db.Attempts.AsNoTracking()
            .Where(attempt => attempt.RunId == runId).ToListAsync(cancellationToken);
        var counts = new StageReading
        {
            AttemptsInRun = attempts.Count,
            DispatchedAttemptsInRun = attempts.Count(attempt => attempt.AgentDispatchedAtUtc != null),
        };
        var stored = attempts.OrderBy(attempt => attempt.AttemptNumber)
            .FirstOrDefault(attempt => attempt.AgentRole == agentRole);
        if (stored is null)
        {
            return counts;
        }

        var route = await ReadRouteAsync(role, runId, cancellationToken);
        var status = stored.Status.ToString();
        var outcome = stored.AgentOutcome?.ToString();
        var reading = counts with
        {
            AttemptFound = true,
            AttemptId = stored.Id,
            AttemptNumber = stored.AttemptNumber,
            Provider = stored.AgentProvider?.ToString() ?? string.Empty,
            Role = stored.AgentRole?.ToString() ?? string.Empty,
            Contract = stored.AgentResponseContract?.ToString() ?? string.Empty,
            Status = status,
            Outcome = outcome,
            Dispatched = stored.AgentDispatchedAtUtc is not null,
            AdapterContractVersion = stored.AgentAdapterContractVersion ?? string.Empty,
            StandardOutputCompletion = stored.AgentStandardOutputCompletion?.ToString(),
            StandardErrorCompletion = stored.AgentStandardErrorCompletion?.ToString(),
            StandardInputDelivery = stored.AgentStandardInputDelivery?.ToString(),
            InvocationDiagnosticState = Diagnostic(stored).State.ToString(),
            InvocationDiagnosticCategory = Diagnostic(stored).Category?.ToString(),
            InvocationDiagnosticVersion = Diagnostic(stored).Version,
            ProcessOutcome = stored.AgentProcessOutcome?.ToString(),
            ExitCode = stored.AgentProcessExitCode,
            DurationMilliseconds = stored.AgentProcessDuration is { } time ? (long)time.TotalMilliseconds : null,
            StatusRouteAgrees = route.AttemptId == stored.Id && route.Status == status && route.Outcome == outcome,
            ReviewedProposalMessageId = route.ReviewedProposal,
        };
        return stored.Status == AttemptStatus.Running
            ? reading
            : await CompleteAsync(reading, stored, db, role, provider, agentRole, contract, runId, cancellationToken);
    }

    private async Task<(Guid? AttemptId, string? Status, string? Outcome, Guid? ReviewedProposal)> ReadRouteAsync(
        AllowanceRole role,
        Guid runId,
        CancellationToken cancellationToken)
    {
        if (role == AllowanceRole.Planner)
        {
            var planner = await api.GetPlannerStatusAsync(runId, cancellationToken);
            return (planner.AttemptId, planner.Status, planner.Outcome, null);
        }

        var reviewer = await api.GetReviewerStatusAsync(runId, cancellationToken);
        return (reviewer.AttemptId, reviewer.Status, reviewer.Outcome, reviewer.ReviewedProposalMessageId);
    }

    private async Task<StageReading> CompleteAsync(
        StageReading reading,
        Attempt stored,
        DevalCopilotDbContext db,
        AllowanceRole role,
        AgentProvider provider,
        AgentRole agentRole,
        string contract,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var attemptEvidence = await api.GetAttemptEvidenceAsync(runId, stored.Id, cancellationToken);
        reading = reading with { AttemptEvidenceRouteComparison = CompareAttemptEvidence(attemptEvidence, stored, reading) };
        var timeline = await api.GetTimelineAsync(runId, cancellationToken);
        var produced = timeline.Where(message => message.AttemptId == stored.Id).ToArray();
        var inputs = await db.AttemptInputMessages.AsNoTracking()
            .Where(input => input.AttemptId == stored.Id).OrderBy(input => input.Sequence)
            .Select(input => input.CollaborationMessageId).ToListAsync(cancellationToken);
        var rows = (await db.CollaborationMessages.AsNoTracking()
                .Where(row => row.RunId == runId && row.AttemptId == stored.Id)
                .Select(row => new
                {
                    row.Id,
                    row.Type,
                    row.InReplyToMessageId,
                    row.ActorKind,
                    row.ActorAgentProvider,
                    row.ActorAgentRole,
                    row.Provenance,
                })
                .ToListAsync(cancellationToken))
            .Select(row => new StoredMessage(
                row.Id,
                row.Type.ToString(),
                row.InReplyToMessageId,
                row.ActorKind.ToString(),
                $"{row.ActorAgentProvider}.{row.ActorAgentRole}",
                row.Provenance.ToString()))
            .ToList();

        // Evidence is requested for at most the number of messages the role and recorded outcome allow, in timeline order and one
        // request each, across every read of the stage, so an unexpected set or a repeated read can never make the reader call the
        // route an unbounded number of times. A message beyond that bound, or of an outcome that expects none, stays not attempted and
        // its set is rejected by the verifier.
        var evidenceBound = StageMessageBounds.Maximum(agentRole.ToString(), reading.Outcome);
        // The comparison facts of this read are observed before any reply is looked at, so a drift is latched even when the timeline
        // lists nothing or the ceiling is zero; it is never undone by a later read that returns to the first facts.
        if (!_evidenceRequests.TryGetValue(role, out var memory))
        {
            memory = _evidenceRequests[role] = new MessageEvidenceMemory();
        }

        var facts = MessageEvidenceFacts.Create(
            stored.Id,
            provider.ToString(),
            agentRole.ToString(),
            reading.Outcome,
            reading.ProcessOutcome,
            reading.ExitCode,
            ExpectedEnvelope(role),
            inputs);
        memory.Observe(facts);
        var listed = new List<StageMessage>();
        foreach (var (message, position) in produced.Take(StageMessageBounds.MaximumListed).Select((item, index) => (item, index)))
        {
            var comparison = position < evidenceBound
                ? await memory.ResolveAsync(
                    message.Id,
                    facts,
                    evidenceBound,
                    () => CompareMessageEvidenceAsync(
                        role, runId, message.Id, stored, reading, provider, agentRole, inputs, cancellationToken))
                : EvidenceRouteComparisonStatus.NotAttempted;
            var row = rows.FirstOrDefault(candidate => candidate.Id == message.Id);
            var persistedAgrees = row is not null
                && row.Type == message.Type
                && row.InReplyToMessageId == message.InReplyToMessageId
                && row.ActorKind == message.Actor.Kind
                && row.Actor == $"{message.Actor.Provider}.{message.Actor.Role}"
                && row.Provenance == message.Provenance;
            listed.Add(new StageMessage(
                message.Id,
                message.Type,
                message.InReplyToMessageId,
                message.AttemptId,
                $"{message.Actor.Provider}.{message.Actor.Role}",
                message.Provenance,
                comparison,
                persistedAgrees ? EvidenceRouteComparisonStatus.Agrees : EvidenceRouteComparisonStatus.Disagrees));
        }

        var timelineIds = produced.Select(message => message.Id).ToArray();
        // The timeline was enumerated, so the comparison is always made: an observed empty timeline beside stored replies disagrees.
        var setComparison = timelineIds.Distinct().Count() == timelineIds.Length
            && timelineIds.Length == rows.Count
            && timelineIds.ToHashSet().SetEquals(rows.Select(row => row.Id))
            ? EvidenceRouteComparisonStatus.Agrees
            : EvidenceRouteComparisonStatus.Disagrees;

        var artifacts = await db.Artifacts.AsNoTracking()
            .Where(artifact => artifact.AttemptId == stored.Id).ToListAsync(cancellationToken);
        var manifest = artifacts.SingleOrDefault(artifact => artifact.Purpose == ArtifactPurpose.AgentContextManifest);
        var manifestIsTheAttemptsOwn = manifest is not null && stored.AgentContextManifestArtifactId == manifest.Id;
        var manifestBytes = manifestIsTheAttemptsOwn ? ReadSealedBytes(manifest!) : null;
        var manifestFacts = manifestBytes is null
            ? SealedManifestFacts.None
            : SealedManifestReader.Read(manifestBytes, contract, objective);
        return reading with
        {
            MessageCount = produced.Length,
            Messages = listed,
            MessageSetComparison = setComparison,
            InputMessageIds = inputs,
            ManifestPresent = manifestIsTheAttemptsOwn,
            ManifestBytesAgree = manifestBytes is not null,
            ManifestContractAgrees = manifestFacts.ContractAgrees,
            ManifestObjectiveAgrees = manifestFacts.ObjectiveAgrees,
            ManifestProposalMessageId = manifestFacts.ProposalMessageId,
            ArtifactCount = artifacts.Count,
            ArtifactsAgreeing = artifacts.Count(artifact => ReadSealedBytes(artifact) is not null),
        };
    }

    private static string ExpectedEnvelope(AllowanceRole role) => role == AllowanceRole.Planner ? "Empty" : "Recorded";

    /// <summary>One request of the public message-evidence route, compared with the stored attempt on provider, role, outcome,
    /// process and the complete recorded input envelope: its status (empty for the Planner, recorded for the reviewer), that nothing is
    /// omitted, its total count and the exact ordered identities. Any difference, and an evidence status other than a recorded
    /// one, is a disagreement.</summary>
    private async Task<EvidenceRouteComparisonStatus> CompareMessageEvidenceAsync(
        AllowanceRole role,
        Guid runId,
        Guid messageId,
        Attempt stored,
        StageReading reading,
        AgentProvider provider,
        AgentRole agentRole,
        IReadOnlyList<Guid> inputs,
        CancellationToken cancellationToken)
    {
        var evidence = await api.GetMessageEvidenceAsync(runId, messageId, cancellationToken);
        var expectedEnvelope = ExpectedEnvelope(role);
        var evidenceAgrees = evidence.EvidenceStatus == "HasEvidence"
            && evidence.AttemptId == stored.Id
            && evidence.AgentProvider == provider.ToString()
            && evidence.AgentRole == agentRole.ToString()
            && evidence.AgentOutcome == reading.Outcome
            && evidence.ProcessExecution is { } process
            && process.Outcome == reading.ProcessOutcome
            && process.ExitCode == reading.ExitCode
            && evidence.InputMessagesStatus == expectedEnvelope
            && !evidence.InputMessagesOmitted
            && evidence.InputMessageTotalCount == inputs.Count
            && evidence.InputMessages.Select(input => input.CollaborationMessageId).SequenceEqual(inputs);
        return evidenceAgrees ? EvidenceRouteComparisonStatus.Agrees : EvidenceRouteComparisonStatus.Disagrees;
    }

    private sealed record StoredMessage(
        Guid Id, string Type, Guid? InReplyToMessageId, string ActorKind, string Actor, string Provenance);

    private static AgentInvocationDiagnosticFact Diagnostic(Attempt stored) => AgentInvocationDiagnosticFact.ForAttempt(stored);

    /// <summary>Compares the public attempt-evidence route with the stored attempt on identity, status, outcome, process outcome and
    /// exit code, the three recorded I/O facts and the closed diagnostic. The route is read for every terminal attempt, whether or
    /// not it produced a message; any difference, and an incoherent identity, is a disagreement.</summary>
    private static EvidenceRouteComparisonStatus CompareAttemptEvidence(
        AgentAttemptEvidenceResponse route,
        Attempt stored,
        StageReading reading)
    {
        var process = route.ProcessExecution;
        var diagnostic = Diagnostic(stored);
        var agrees = route.IdentityValid
            && route.AttemptId == stored.Id
            && route.AttemptStatus == reading.Status
            && route.Provider == reading.Provider
            && route.Role == reading.Role
            && route.ResponseContract == reading.Contract
            && route.Outcome == reading.Outcome
            && process is not null
            && process.Outcome == reading.ProcessOutcome
            && process.ExitCode == reading.ExitCode
            && process.StandardOutputCompletion == reading.StandardOutputCompletion
            && process.StandardErrorCompletion == reading.StandardErrorCompletion
            && process.StandardInputDelivery == reading.StandardInputDelivery
            && route.InvocationDiagnostic is { } routeDiagnostic
            && routeDiagnostic.State == diagnostic.State.ToString()
            && routeDiagnostic.Category == diagnostic.Category?.ToString()
            && routeDiagnostic.Version == diagnostic.Version;
        return agrees ? EvidenceRouteComparisonStatus.Agrees : EvidenceRouteComparisonStatus.Disagrees;
    }

    private byte[]? ReadSealedBytes(Artifact artifact) => SealedArtifactFile.ReadVerified(
        artifactRoot,
        artifact.RelativeStoragePath,
        artifact.ByteLength,
        artifact.ContentHash,
        artifact.CaptureOutcome == ArtifactCaptureOutcome.Captured);
}
