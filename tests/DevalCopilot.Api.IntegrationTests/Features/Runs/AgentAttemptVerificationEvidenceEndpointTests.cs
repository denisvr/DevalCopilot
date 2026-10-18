using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSwag.Generation;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0052: the protected, bodyless, read-only recorded-verification inspection of one CodeReviewer attempt, against the real host and
/// file-backed SQLite. An unknown run and a missing, foreign or non-Agent attempt are one fixed 404, the answer is the closed status with
/// the immutable header, the recorded checkpoint, complete ordered members and truthful stream descriptors whose times are strings, and
/// nothing is written, echoed or substituted.
/// </summary>
public sealed class AgentAttemptVerificationEvidenceEndpointTests(ApiWebApplicationFactory factory)
    : AgentAttemptVerificationApiTestBase(factory), IClassFixture<ApiWebApplicationFactory>
{
    private static string EvidenceRoute(Guid runId, Guid attemptId) => $"/api/runs/{runId}/agent-attempts/{attemptId}/verification-evidence";

    private static readonly string[] RootMembers = ["attempt", "attemptId", "checkpoint", "memberTotalCount", "members", "runId", "status"];

    private static readonly string[] HeaderMembers = ["attemptNumber", "id", "provider", "responseContract", "role"];

    private static readonly string[] CheckpointMembers = ["checkpointNumber", "id"];

    private static readonly string[] MemberMembers =
    [
        "claimedAtUtc", "commandId", "commandName", "completedAtUtc", "dispatchedAtUtc", "executionId", "executionNumber", "exitCode",
        "sequence", "standardError", "standardOutput", "status",
    ];

    private static readonly string[] StreamMembers = ["artifactId", "byteLength", "captureOutcome", "status", "truncated"];

    [Fact]
    public async Task The_route_requires_authentication_and_accepts_only_a_bodyless_get()
    {
        var lineage = await SeedAsync(false, false);
        var route = EvidenceRoute(lineage.RunId, lineage.AttemptId);

        using var anonymous = Host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);

        using var wrong = Host.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(route)).StatusCode);

        using var queryOnly = Host.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await queryOnly.GetAsync($"{route}?access_token={ApiWebApplicationFactory.ValidSecret}")).StatusCode);

        using var client = AuthenticatedClient();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PatchAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
    }

    [Fact]
    public void Exactly_one_evidence_route_and_one_window_route_exist_and_both_are_authorized_gets_with_no_anonymous_metadata()
    {
        var endpoints = Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("verification-evidence", StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(endpoint => endpoint.RoutePattern.RawText, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "api/runs/{runId:guid}/agent-attempts/{attemptId:guid}/verification-evidence",
                "api/runs/{runId:guid}/agent-attempts/{attemptId:guid}/verification-evidence/{executionId:guid}/artifact-window/{stream}",
            ],
            endpoints.Select(endpoint => endpoint.RoutePattern.RawText));
        foreach (var endpoint in endpoints)
        {
            Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
        }
    }

    [Fact]
    public async Task An_unknown_run_and_a_missing_foreign_or_non_agent_attempt_are_the_same_fixed_not_found_problem()
    {
        var lineage = await SeedAsync(false, false);
        var other = await SeedAsync(false, false);
        var (_, simulatedRun) = await SeedRunAsync();
        var simulated = await WithDbAsync(async db =>
        {
            var claimed = Attempt.Claim(Guid.NewGuid(), simulatedRun, 1, Now);
            db.Attempts.Add(claimed);
            await db.SaveChangesAsync();
            return claimed;
        });
        var unknownId = Guid.NewGuid();

        var responses = new[]
        {
            await GetAsync(EvidenceRoute(Guid.NewGuid(), lineage.AttemptId)),
            await GetAsync(EvidenceRoute(lineage.RunId, unknownId)),
            await GetAsync(EvidenceRoute(lineage.RunId, other.AttemptId)),
            await GetAsync(EvidenceRoute(simulatedRun, simulated.Id)),
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.Status);
            Assert.Equal("urn:devalente:problem:not-found", response.Body.GetProperty("type").GetString());
            Assert.Equal(404, response.Body.GetProperty("status").GetInt32());
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal("agent_attempt_verification.not_found", error.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(response.Body.GetProperty("traceId").GetString()));
            var body = Normalize(response.Text);
            foreach (var value in new[] { unknownId, lineage.AttemptId, other.AttemptId, lineage.RunId, simulated.Id, simulatedRun })
            {
                Assert.DoesNotContain(value.ToString(), body, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Single(responses.Select(response => Normalize(response.Text)).Distinct());
    }

    [Fact]
    public async Task A_malformed_identifier_is_not_routed_and_is_not_echoed()
    {
        var lineage = await SeedAsync(false, false);

        var response = await GetAsync($"/api/runs/{lineage.RunId}/agent-attempts/not-a-guid-{Secret}/verification-evidence");

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.DoesNotContain(Secret, response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recorded_carries_only_the_documented_members_with_string_times_and_closed_stream_descriptors()
    {
        var lineage = await SeedAsync(true, true, false);
        var failed = lineage.Members[0].ExecutionId;
        var passed = lineage.Members[1].ExecutionId;
        var hash = ContentHash([1, 2, 3]);
        var stdout = await AddOutputAsync(failed, VerificationOutputPurpose.StandardOutput, "a/out.sealed", hash, 12, truncated: true);
        var stderr = await AddOutputAsync(
            failed, VerificationOutputPurpose.StandardError, "a/err.sealed", hash, 0, null,
            VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption);
        await AddOutputAsync(passed, VerificationOutputPurpose.StandardOutput, "b/out.sealed", hash, 3, truncated: false);

        var response = await GetAsync(EvidenceRoute(lineage.RunId, lineage.AttemptId));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(RootMembers, Names(response.Body));
        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
        Assert.Equal(lineage.RunId, response.Body.GetProperty("runId").GetGuid());
        Assert.Equal(lineage.AttemptId, response.Body.GetProperty("attemptId").GetGuid());
        Assert.Equal(2, response.Body.GetProperty("memberTotalCount").GetInt32());
        var header = response.Body.GetProperty("attempt");
        Assert.Equal(HeaderMembers, Names(header));
        Assert.Equal("CodeReviewer", header.GetProperty("role").GetString());
        Assert.Equal("Codex", header.GetProperty("provider").GetString());
        Assert.Equal("VerificationDiagnosis", header.GetProperty("responseContract").GetString());
        var checkpoint = response.Body.GetProperty("checkpoint");
        Assert.Equal(CheckpointMembers, Names(checkpoint));
        Assert.Equal(lineage.CheckpointId, checkpoint.GetProperty("id").GetGuid());
        Assert.Equal(1, checkpoint.GetProperty("checkpointNumber").GetInt32());

        var members = response.Body.GetProperty("members").EnumerateArray().ToArray();
        Assert.Equal(2, members.Length);
        for (var index = 0; index < members.Length; index++)
        {
            var member = members[index];
            Assert.Equal(MemberMembers, Names(member));
            Assert.Equal(index, member.GetProperty("sequence").GetInt32());
            Assert.Equal(lineage.Members[index].CommandId, member.GetProperty("commandId").GetGuid());
            Assert.Equal(lineage.Members[index].ExecutionId, member.GetProperty("executionId").GetGuid());
            Assert.Equal($"Command {index + 1}", member.GetProperty("commandName").GetString());
            Assert.Equal(index == 0 ? "Failed" : "Passed", member.GetProperty("status").GetString());
            Assert.Equal(index == 0 ? 1 : 0, member.GetProperty("exitCode").GetInt32());
            foreach (var name in new[] { "claimedAtUtc", "dispatchedAtUtc", "completedAtUtc" })
            {
                Assert.Equal(JsonValueKind.String, member.GetProperty(name).ValueKind);
                var text = member.GetProperty(name).GetString()!;
                Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$", text);
                DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            }
        }

        var failedOut = members[0].GetProperty("standardOutput");
        Assert.Equal(StreamMembers, Names(failedOut));
        Assert.Equal("Recorded", failedOut.GetProperty("status").GetString());
        Assert.Equal(stdout.Id, failedOut.GetProperty("artifactId").GetGuid());
        Assert.Equal(12, failedOut.GetProperty("byteLength").GetInt64());
        Assert.Equal("CapturedWithKnownTruncation", failedOut.GetProperty("captureOutcome").GetString());
        Assert.Equal(JsonValueKind.True, failedOut.GetProperty("truncated").ValueKind);

        var recovered = members[0].GetProperty("standardError");
        Assert.Equal(stderr.Id, recovered.GetProperty("artifactId").GetGuid());
        Assert.Equal(0, recovered.GetProperty("byteLength").GetInt64());
        Assert.Equal("RecoveredAfterHostInterruption", recovered.GetProperty("captureOutcome").GetString());
        Assert.Equal(JsonValueKind.Null, recovered.GetProperty("truncated").ValueKind);

        Assert.Equal(JsonValueKind.False, members[1].GetProperty("standardOutput").GetProperty("truncated").ValueKind);
        var notRecorded = members[1].GetProperty("standardError");
        Assert.Equal(StreamMembers, Names(notRecorded));
        Assert.Equal("NotRecorded", notRecorded.GetProperty("status").GetString());
        foreach (var name in new[] { "artifactId", "byteLength", "captureOutcome", "truncated" })
        {
            Assert.Equal(JsonValueKind.Null, notRecorded.GetProperty(name).ValueKind);
        }

        foreach (var forbidden in new[]
        {
            "path", "hash", "sha256", "snapshot", "argument", "executable", "workspace", "fingerprint", "digest", "recipe", "timeout",
            "enabled", "c:\\\\", "verify.exe",
        })
        {
            Assert.DoesNotContain(forbidden, response.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-int.MaxValue)]
    [InlineData(-int.MaxValue + 1)]
    [InlineData(int.MaxValue)]
    public async Task A_failed_member_carries_its_exact_int32_boundary_exit_code_and_recorded_stream_on_the_wire(int exitCode)
    {
        var lineage = await SeedAsync(true, true, false);
        var failed = lineage.Members[0].ExecutionId;
        await ExecuteAsync($"UPDATE verification_executions SET ExitCode = {exitCode} WHERE Id = {failed}");
        var stdout = await AddOutputAsync(failed, VerificationOutputPurpose.StandardOutput, "a/out.sealed", ContentHash([1, 2, 3]), 3);

        var response = await GetAsync(EvidenceRoute(lineage.RunId, lineage.AttemptId));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("Recorded", response.Body.GetProperty("status").GetString());
        Assert.Equal(2, response.Body.GetProperty("memberTotalCount").GetInt32());
        var member = response.Body.GetProperty("members")[0];
        Assert.Equal("Failed", member.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Number, member.GetProperty("exitCode").ValueKind);
        Assert.Equal(exitCode, member.GetProperty("exitCode").GetInt32());
        Assert.Equal(exitCode.ToString(CultureInfo.InvariantCulture), member.GetProperty("exitCode").GetRawText());
        Assert.Equal("Recorded", member.GetProperty("standardOutput").GetProperty("status").GetString());
        Assert.Equal(stdout.Id, member.GetProperty("standardOutput").GetProperty("artifactId").GetGuid());
    }

    [Fact]
    public async Task Unavailable_carries_only_the_documented_members_and_no_target_or_partial_list()
    {
        var zero = await SeedAsync(false, false);
        await ExecuteAsync($"DELETE FROM attempt_verification_evidence WHERE AttemptId = {zero.AttemptId}");
        var defect = await SeedAsync(true, true, false, false);
        await ExecuteAsync($"UPDATE verification_executions SET Status = {"Weird" + Secret} WHERE Id = {defect.Members[^1].ExecutionId}");

        foreach (var lineage in new[] { zero, defect })
        {
            var response = await GetAsync(EvidenceRoute(lineage.RunId, lineage.AttemptId));
            Assert.Equal(HttpStatusCode.OK, response.Status);
            Assert.Equal(RootMembers, Names(response.Body));
            Assert.Equal("Unavailable", response.Body.GetProperty("status").GetString());
            Assert.Equal(lineage.RunId, response.Body.GetProperty("runId").GetGuid());
            Assert.Equal(lineage.AttemptId, response.Body.GetProperty("attemptId").GetGuid());
            Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("attempt").ValueKind);
            Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("checkpoint").ValueKind);
            Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("memberTotalCount").ValueKind);
            Assert.Empty(response.Body.GetProperty("members").EnumerateArray());
            Assert.DoesNotContain(Secret, response.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Thirty_two_members_are_recorded_and_a_thirty_third_is_unavailable_without_a_count()
    {
        var lineage = await SeedAsync(false, new bool[32]);

        var recorded = await GetAsync(EvidenceRoute(lineage.RunId, lineage.AttemptId));
        Assert.Equal("Recorded", recorded.Body.GetProperty("status").GetString());
        Assert.Equal(32, recorded.Body.GetProperty("memberTotalCount").GetInt32());
        Assert.Equal(32, recorded.Body.GetProperty("members").GetArrayLength());

        await WithDbAsync(async db =>
        {
            var workspace = await db.GitWorkspaces.SingleAsync(candidate => candidate.Id == lineage.WorkspaceId);
            var checkpoint = await db.GitCheckpoints.SingleAsync(candidate => candidate.Id == lineage.CheckpointId);
            var command = VerificationCommand.Configure(Guid.NewGuid(), lineage.ProjectId, 33, "Command 33", @"C:\tools\verify.exe", [], 60, true, Now);
            db.VerificationCommands.Add(command);
            await db.SaveChangesAsync();
            var execution = VerificationExecution.Claim(Guid.NewGuid(), lineage.ProjectId, 33, workspace, checkpoint, command, Now.AddMinutes(1));
            execution.MarkDispatched(Now.AddMinutes(1).AddSeconds(1));
            execution.Complete(VerificationExecutionOutcome.Exited, 0, checkpoint.FingerprintSha256, Now.AddMinutes(1).AddSeconds(2));
            db.VerificationExecutions.Add(execution);
            db.AttemptVerificationEvidence.Add(AttemptVerificationEvidence.Record(Guid.NewGuid(), lineage.AttemptId, command.Id, execution.Id, 32));
            return await db.SaveChangesAsync();
        });

        var unavailable = await GetAsync(EvidenceRoute(lineage.RunId, lineage.AttemptId));
        Assert.Equal("Unavailable", unavailable.Body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, unavailable.Body.GetProperty("memberTotalCount").ValueKind);
        Assert.Empty(unavailable.Body.GetProperty("members").EnumerateArray());
    }

    [Fact]
    public async Task Reading_the_evidence_writes_nothing()
    {
        var lineage = await SeedAsync(true, true, false);
        await AddOutputAsync(lineage.Members[0].ExecutionId, VerificationOutputPurpose.StandardOutput, "a/out.sealed", ContentHash([1]), 1);

        async Task<string> SnapshotAsync() => await WithDbAsync(async db =>
        {
            var executions = await db.VerificationExecutions.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => row.Id + "|" + row.Status + "|" + row.ExitCode + "|" + row.CommandName).ToListAsync();
            var members = await db.AttemptVerificationEvidence.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => row.Id + "|" + row.Sequence + "|" + row.SnapshotSha256).ToListAsync();
            var outputs = await db.VerificationOutputArtifacts.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => row.Id + "|" + row.RelativeStoragePath + "|" + row.ContentHash).ToListAsync();
            return string.Join(";", executions) + "#" + string.Join(";", members) + "#" + string.Join(";", outputs) + "#"
                + await db.Events.CountAsync() + "#" + await db.Attempts.CountAsync() + "#" + await db.Artifacts.CountAsync();
        });

        var before = await SnapshotAsync();
        await GetAsync(EvidenceRoute(lineage.RunId, lineage.AttemptId));
        await GetAsync(EvidenceRoute(lineage.RunId, Guid.NewGuid()));

        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task The_actual_openapi_document_declares_both_bodyless_routes_the_closed_responses_and_string_times()
    {
        var generator = Host.Services.GetRequiredService<IOpenApiDocumentGenerator>();
        var document = JsonNode.Parse((await generator.GenerateAsync("v1")).ToJson())!;

        var operation = document["paths"]!["/api/runs/{runId}/agent-attempts/{attemptId}/verification-evidence"]!["get"]!;
        var parameters = operation["parameters"]!.AsArray().Select(p => (p!["name"]!.GetValue<string>(), p["in"]!.GetValue<string>()));
        Assert.Equal([("attemptId", "path"), ("runId", "path")], parameters.Order());
        Assert.Null(operation["requestBody"]);
        var responses = operation["responses"]!.AsObject();
        Assert.Equal(["200", "404"], responses.Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("GetAgentAttemptVerificationEvidenceResponse", responses["200"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("ApiProblemDetails", responses["404"]!["content"]!.ToJsonString(), StringComparison.Ordinal);

        Assert.Equal(RootMembers, PropertiesOf(document, "GetAgentAttemptVerificationEvidenceResponse").Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Equal(HeaderMembers, PropertiesOf(document, "AgentAttemptVerificationHeaderResponse").Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Equal(CheckpointMembers, PropertiesOf(document, "AgentAttemptVerificationCheckpointResponse").Select(item => item.Key).Order(StringComparer.Ordinal));
        var member = PropertiesOf(document, "AgentAttemptVerificationMemberResponse");
        Assert.Equal(MemberMembers, member.Select(item => item.Key).Order(StringComparer.Ordinal));
        foreach (var name in new[] { "claimedAtUtc", "dispatchedAtUtc", "completedAtUtc" })
        {
            var schema = member[name]!.ToJsonString();
            Assert.Contains("\"string\"", schema, StringComparison.Ordinal);
            Assert.DoesNotContain("date-time", schema, StringComparison.Ordinal);
        }

        Assert.Equal(StreamMembers, PropertiesOf(document, "AgentAttemptVerificationStreamResponse").Select(item => item.Key).Order(StringComparer.Ordinal));
        Assert.Contains("integer", PropertiesOf(document, "AgentAttemptVerificationStreamResponse")["byteLength"]!.ToJsonString(), StringComparison.Ordinal);
    }
}
