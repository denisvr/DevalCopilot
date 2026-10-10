using System.Net;
using System.Text;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>How the two submissions are classified, proven without a network: exactly one request each, never repeated, and every
/// answer the host could not have meant as "an attempt was created" is Refused (a definite client error) or Unknown.</summary>
public sealed class QualificationApiClientTests : QualificationTestBase
{
    private const string Secret = "launch-secret-for-this-test-only";

    private static readonly Guid Run = Guid.NewGuid();

    private sealed class ScriptedHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        private readonly List<(HttpMethod Method, string Url, string? Authorization, string? Body)> _requests = [];

        public IReadOnlyList<(HttpMethod Method, string Url, string? Authorization, string? Body)> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_requests)
            {
                _requests.Add((request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
            }

            return await answer(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private (QualificationApiClient Client, ScriptedHandler Handler) Client(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer,
        QualificationLimits? limits = null)
    {
        var handler = new ScriptedHandler(answer);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:9"), Timeout = Timeout.InfiniteTimeSpan };
        return (new QualificationApiClient(http, Secret, limits ?? Tiny), handler);
    }

    [Fact]
    public async Task An_answer_with_a_durable_attempt_is_accepted_after_exactly_one_authenticated_request()
    {
        var attempt = Guid.NewGuid();
        var (client, handler) = Client((_, _) => Task.FromResult(Json(HttpStatusCode.OK, $"{{\"attemptId\":\"{attempt}\",\"attemptNumber\":1}}")));

        var result = await client.SubmitPlannerAsync(Run, CancellationToken.None);

        Assert.Equal(SubmissionOutcome.Accepted, result.Outcome);
        Assert.Equal(attempt, result.AttemptId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"http://127.0.0.1:9/api/runs/{Run}/agent-attempts/codex-plan", request.Url);
        Assert.Equal("Bearer " + Secret, request.Authorization);
        Assert.DoesNotContain(Secret, request.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_review_request_carries_exactly_the_proposal_identity_once()
    {
        var proposal = Guid.NewGuid();
        var (client, handler) = Client((_, _) => Task.FromResult(Json(HttpStatusCode.OK, $"{{\"attemptId\":\"{Guid.NewGuid()}\",\"attemptNumber\":2}}")));

        var result = await client.SubmitReviewerAsync(Run, proposal, CancellationToken.None);

        Assert.Equal(SubmissionOutcome.Accepted, result.Outcome);
        var request = Assert.Single(handler.Requests);
        Assert.Equal($"http://127.0.0.1:9/api/runs/{Run}/agent-attempts/claude-critical-review", request.Url);
        Assert.Equal($"{{\"proposalMessageId\":\"{proposal}\"}}", request.Body);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"attemptId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"attemptId\":\"not-a-guid\"}")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    public async Task A_success_answer_that_names_no_attempt_is_unknown_not_accepted_and_is_not_repeated(string body)
    {
        var (client, handler) = Client((_, _) => Task.FromResult(Json(HttpStatusCode.OK, body)));

        var result = await client.SubmitPlannerAsync(Run, CancellationToken.None);

        Assert.Equal(SubmissionOutcome.Unknown, result.Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_client_error_is_a_definite_refusal_with_only_a_closed_code()
    {
        var (client, handler) = Client((_, _) => Task.FromResult(Json(
            HttpStatusCode.Conflict, "{\"title\":\"Conflict\",\"code\":\"agent_attempts.provider_not_observed\",\"detail\":\"C:/Users/someone/secret\"}")));

        var result = await client.SubmitPlannerAsync(Run, CancellationToken.None);

        Assert.Equal(SubmissionOutcome.Refused, result.Outcome);
        Assert.Equal("agent_attempts.provider_not_observed", result.Code);
        Assert.Null(result.AttemptId);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("{\"code\":\"Has Spaces And C:/Paths\"}")]
    [InlineData("<html>bad gateway</html>")]
    [InlineData("")]
    public async Task A_client_error_without_a_closed_code_is_refused_without_one(string body)
    {
        var (client, _) = Client((_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest, body)));

        var result = await client.SubmitReviewerAsync(Run, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(SubmissionOutcome.Refused, result.Outcome);
        Assert.Null(result.Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task A_server_error_or_an_unexpected_status_is_unknown_never_a_retry(HttpStatusCode status)
    {
        var (client, handler) = Client((_, _) => Task.FromResult(Json(status, "{\"code\":\"agent_attempts.something\"}")));

        var result = await client.SubmitPlannerAsync(Run, CancellationToken.None);

        Assert.Equal(SubmissionOutcome.Unknown, result.Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_transport_failure_is_unknown_and_is_not_repeated()
    {
        var (client, handler) = Client((_, _) => throw new HttpRequestException("connection reset"));

        var result = await client.SubmitPlannerAsync(Run, CancellationToken.None);

        Assert.Equal(SubmissionOutcome.Unknown, result.Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task An_answer_slower_than_the_submission_bound_is_unknown_and_is_not_repeated()
    {
        var limits = Tiny with { Submission = TimeSpan.FromMilliseconds(50) };
        var (client, handler) = Client(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            return Json(HttpStatusCode.OK, "{}");
        }, limits);

        var result = await client.SubmitPlannerAsync(Run, CancellationToken.None);

        Assert.Equal(SubmissionOutcome.Unknown, result.Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_cancellation_while_the_post_is_in_flight_is_unknown_and_is_not_repeated()
    {
        using var cancellation = new CancellationTokenSource();
        var (client, handler) = Client(async (_, token) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            return Json(HttpStatusCode.OK, "{}");
        });

        var result = await client.SubmitPlannerAsync(Run, cancellation.Token);

        Assert.Equal(SubmissionOutcome.Unknown, result.Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_read_that_is_refused_or_slow_fails_closed_without_a_body_in_the_message()
    {
        var (refused, _) = Client((_, _) => Task.FromResult(Json(HttpStatusCode.NotFound, "{\"detail\":\"C:/Users/someone\"}")));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => refused.GetPlannerStatusAsync(Run, CancellationToken.None));
        Assert.DoesNotContain("someone", failure.Message, StringComparison.Ordinal);

        var (slow, _) = Client(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            return Json(HttpStatusCode.OK, "{}");
        }, Tiny with { Read = TimeSpan.FromMilliseconds(50) });
        await Assert.ThrowsAsync<TimeoutException>(() => slow.GetTimelineAsync(Run, CancellationToken.None));
    }

    [Fact]
    public async Task A_setup_step_that_is_refused_names_only_the_step_and_the_status()
    {
        var (client, _) = Client((_, _) => Task.FromResult(Json(HttpStatusCode.Conflict, "{\"detail\":\"C:/Users/someone/repo\"}")));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.RegisterProjectAsync("name", "C:/Users/someone/repo", CancellationToken.None));

        Assert.Equal("Setup step 'register' was refused with HTTP 409.", failure.Message);
    }
}
