using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// Builds one <see cref="StageReading"/> from the public status, timeline and message-evidence routes, supplemented by read-only
/// queries of the disposable rows and the sealed artifacts under the owned artifact root. It writes nothing, executes nothing, and
/// keeps no content: a manifest or artifact is read only to compare its length, its hash and three named members.
/// </summary>
public sealed class StageEvidenceReader(
    IServiceProvider services,
    QualificationApiClient api,
    string artifactRoot,
    string objective)
{
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
            ProcessOutcome = stored.AgentProcessOutcome?.ToString(),
            ExitCode = stored.AgentProcessExitCode,
            DurationMilliseconds = stored.AgentProcessDuration is { } time ? (long)time.TotalMilliseconds : null,
            StatusRouteAgrees = route.AttemptId == stored.Id && route.Status == status && route.Outcome == outcome,
            ReviewedProposalMessageId = route.ReviewedProposal,
        };
        return stored.Status == AttemptStatus.Running
            ? reading
            : await CompleteAsync(reading, stored, db, provider, agentRole, contract, runId, cancellationToken);
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
        AgentProvider provider,
        AgentRole agentRole,
        string contract,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var timeline = await api.GetTimelineAsync(runId, cancellationToken);
        var produced = timeline.Where(message => message.AttemptId == stored.Id).ToArray();
        var message = produced.Length == 1 ? produced[0] : null;
        var inputs = await db.AttemptInputMessages.AsNoTracking()
            .Where(input => input.AttemptId == stored.Id).OrderBy(input => input.Sequence)
            .Select(input => input.CollaborationMessageId).ToListAsync(cancellationToken);
        var evidenceAgrees = false;
        if (message is not null)
        {
            var evidence = await api.GetMessageEvidenceAsync(runId, message.Id, cancellationToken);
            evidenceAgrees = evidence.EvidenceStatus == "HasEvidence"
                && evidence.AttemptId == stored.Id
                && evidence.AgentProvider == provider.ToString()
                && evidence.AgentRole == agentRole.ToString()
                && evidence.AgentOutcome == reading.Outcome
                && evidence.ProcessExecution is { } process
                && process.Outcome == reading.ProcessOutcome
                && process.ExitCode == reading.ExitCode
                && evidence.InputMessages.Select(input => input.CollaborationMessageId).SequenceEqual(inputs);
        }

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
            MessageId = message?.Id,
            MessageType = message?.Type,
            InReplyToMessageId = message?.InReplyToMessageId,
            MessageAttemptId = message?.AttemptId,
            MessageActor = message is null ? null : $"{message.Actor.Provider}.{message.Actor.Role}",
            MessageProvenance = message?.Provenance,
            EvidenceRouteAgrees = evidenceAgrees,
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

    private byte[]? ReadSealedBytes(Artifact artifact) => SealedArtifactFile.ReadVerified(
        artifactRoot,
        artifact.RelativeStoragePath,
        artifact.ByteLength,
        artifact.ContentHash,
        artifact.CaptureOutcome == ArtifactCaptureOutcome.Captured);
}
