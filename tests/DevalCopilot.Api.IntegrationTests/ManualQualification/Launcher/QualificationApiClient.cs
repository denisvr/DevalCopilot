using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevalCopilot.Api.Features.Projects.PrepareRepositoryWorkspace;
using DevalCopilot.Api.Features.Projects.RegisterProject;
using DevalCopilot.Api.Features.Runs.CreateManualRun;
using DevalCopilot.Api.Features.Runs.GetAgentAttemptStatus;
using DevalCopilot.Api.Features.Runs.GetClaudeCriticalReviewAttemptStatus;
using DevalCopilot.Api.Features.Runs.GetCollaborationMessageEvidence;
using DevalCopilot.Api.Features.Runs.GetCollaborationTimeline;
using DevalCopilot.Api.Features.Runs.RequestClaudeCriticalReview;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The authenticated HTTP operations the session uses, with the host's own contract types. The launch secret is attached
/// once as the bearer header and appears in no URL, argument, file or message. Setup calls and reads are bounded and
/// fail with a closed message; the two submissions are classified rather than thrown, and never repeat a request.
/// </summary>
public sealed partial class QualificationApiClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly QualificationLimits _limits;

    public QualificationApiClient(Uri baseAddress, string launchSecret, QualificationLimits limits)
        : this(new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan }, launchSecret, limits)
    {
    }

    /// <summary>Takes over the client, so the offline regressions can answer through a handler without a network.</summary>
    public QualificationApiClient(HttpClient http, string launchSecret, QualificationLimits limits)
    {
        _limits = limits;
        _http = http;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", launchSecret);
    }

    public void Dispose() => _http.Dispose();

    public async Task<Guid> RegisterProjectAsync(string name, string path, CancellationToken cancellationToken)
    {
        var request = new RegisterProjectRequest(name, path);
        var response = await PostSetupAsync("register", "api/projects", request, cancellationToken);
        return (await ReadAsync<RegisterProjectResponse>(response, "register", cancellationToken)).ProjectId;
    }

    public async Task RecheckIdentityAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var route = $"api/projects/{projectId}/physical-identity/recheck";
        (await PostSetupAsync("recheck", route, null, cancellationToken)).Dispose();
    }

    public async Task<PrepareRepositoryWorkspaceResponse> PrepareWorkspaceAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var route = $"api/projects/{projectId}/workspace/prepare";
        var response = await PostSetupAsync("prepare", route, null, cancellationToken);
        return await ReadAsync<PrepareRepositoryWorkspaceResponse>(response, "prepare", cancellationToken);
    }

    public async Task CaptureCheckpointAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var route = $"api/projects/{projectId}/workspace/checkpoints";
        (await PostSetupAsync("checkpoint", route, null, cancellationToken)).Dispose();
    }

    public async Task<Guid> CreateManualRunAsync(
        Guid projectId,
        string objective,
        int maximumAgentAttempts,
        int maximumAgentInvocationMinutes,
        CancellationToken cancellationToken)
    {
        var request = new CreateManualRunRequest(
            projectId,
            objective,
            maximumAgentAttempts,
            maximumAgentInvocationMinutes);
        var response = await PostSetupAsync("run", "api/runs/manual", request, cancellationToken);
        return (await ReadAsync<CreateManualRunResponse>(response, "run", cancellationToken)).RunId;
    }

    /// <summary>The one planner POST. Any outcome other than a durable attempt is reported, never retried.</summary>
    public Task<SubmissionResult> SubmitPlannerAsync(Guid runId, CancellationToken cancellationToken) =>
        SubmitAsync($"api/runs/{runId}/agent-attempts/codex-plan", null, cancellationToken);

    /// <summary>The one reviewer POST, bound to the exact persisted Proposal.</summary>
    public Task<SubmissionResult> SubmitReviewerAsync(
        Guid runId,
        Guid proposalMessageId,
        CancellationToken cancellationToken) =>
        SubmitAsync(
            $"api/runs/{runId}/agent-attempts/claude-critical-review",
            new RequestClaudeCriticalReviewRequest(proposalMessageId),
            cancellationToken);

    public Task<AgentAttemptStatusResponse> GetPlannerStatusAsync(Guid runId, CancellationToken cancellationToken) =>
        GetAsync<AgentAttemptStatusResponse>($"api/runs/{runId}/agent-attempts/codex-plan", cancellationToken);

    public Task<ClaudeCriticalReviewAttemptStatusResponse> GetReviewerStatusAsync(
        Guid runId,
        CancellationToken cancellationToken) =>
        GetAsync<ClaudeCriticalReviewAttemptStatusResponse>(
            $"api/runs/{runId}/agent-attempts/claude-critical-review",
            cancellationToken);

    public Task<CollaborationMessageTimelineResponse[]> GetTimelineAsync(
        Guid runId,
        CancellationToken cancellationToken) =>
        GetAsync<CollaborationMessageTimelineResponse[]>(
            $"api/runs/{runId}/collaboration-timeline",
            cancellationToken);

    public Task<CollaborationMessageEvidenceResponse> GetMessageEvidenceAsync(
        Guid runId,
        Guid messageId,
        CancellationToken cancellationToken) =>
        GetAsync<CollaborationMessageEvidenceResponse>(
            $"api/runs/{runId}/collaboration-messages/{messageId}/evidence",
            cancellationToken);

    private async Task<SubmissionResult> SubmitAsync(string path, object? body, CancellationToken cancellationToken)
    {
        try
        {
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bound.CancelAfter(_limits.Submission);
            using var content = ContentOf(body);
            using var response = await _http.PostAsync(path, content, bound.Token);
            var status = (int)response.StatusCode;
            if (status is >= 200 and < 300)
            {
                var accepted = await response.Content.ReadFromJsonAsync<AttemptCreated>(Json, bound.Token);
                return accepted is { AttemptId: var attemptId } && attemptId != Guid.Empty
                    ? SubmissionResult.Accepted(attemptId)
                    : SubmissionResult.Unknown();
            }

            if (status is >= 400 and < 500)
            {
                return SubmissionResult.Refused(ProblemCode(await response.Content.ReadAsStringAsync(bound.Token)));
            }

            return SubmissionResult.Unknown();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return SubmissionResult.Unknown();
        }
    }

    private async Task<HttpResponseMessage> PostSetupAsync(
        string operation,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(_limits.Setup);
        try
        {
            using var content = ContentOf(body);
            var response = await _http.PostAsync(path, content, bound.Token);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new InvalidOperationException($"Setup step '{operation}' was refused with HTTP {status}.");
            }

            return response;
        }
        catch (OperationCanceledException) when (BoundElapsed(bound, cancellationToken))
        {
            throw new TimeoutException($"Setup step '{operation}' did not finish within its bound.");
        }
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(_limits.Read);
        try
        {
            using var response = await _http.GetAsync(path, bound.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"A read was refused with HTTP {(int)response.StatusCode}.");
            }

            return await response.Content.ReadFromJsonAsync<T>(Json, bound.Token)
                ?? throw new InvalidOperationException("A read returned no body.");
        }
        catch (OperationCanceledException) when (BoundElapsed(bound, cancellationToken))
        {
            throw new TimeoutException("A read did not finish within its bound.");
        }
    }

    private static bool BoundElapsed(CancellationTokenSource bound, CancellationToken caller) =>
        bound.IsCancellationRequested && !caller.IsCancellationRequested;

    private async Task<T> ReadAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
                ?? throw new InvalidOperationException($"Setup step '{operation}' returned no body.");
        }
    }

    private static HttpContent ContentOf(object? body) =>
        body is null ? new StringContent(string.Empty) : JsonContent.Create(body, options: Json);

    private static string? ProblemCode(string body)
    {
        var match = CodeMember().Match(body);
        return match.Success ? match.Groups["code"].Value : null;
    }

    [GeneratedRegex("\"code\"\\s*:\\s*\"(?<code>[a-z0-9_.]{1,80})\"")]
    private static partial Regex CodeMember();
}
