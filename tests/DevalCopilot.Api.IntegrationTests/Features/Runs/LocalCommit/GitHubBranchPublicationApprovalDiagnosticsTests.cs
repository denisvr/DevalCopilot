using System.Globalization;
using System.Net;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using static DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit.GitHubBranchPublicationApprovalDurabilityTests;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// Synthetic diagnostic evidence for the concurrent identical-request approval fact: fabricated responses prove that its bounded
/// diagnostics keep only ordinals, the numeric status and an exact ADR-0048 code or a fixed fallback, and never the body. They need no
/// host or delivery lineage, and they neither reproduce nor attribute the historical concurrent-replay 409.
/// </summary>
public sealed class GitHubBranchPublicationApprovalDiagnosticsTests
{
    private const string Sentinel = "TEST-48-01-INERT-FICTITIOUS-SENTINEL";

    private const string Unavailable = "github_branch_publication_approval.unavailable";

    private const string Conflict = "github_branch_publication_approval.conflict";

    private const char Backslash = (char)92;

    private static readonly string SuccessBody = $$"""{"runId":"{{Sentinel}}","approvalId":"{{Sentinel}}"}""";

    [Fact]
    public void Diagnostic_unavailable_and_conflict_keep_the_safe_code_and_both_ordinals_in_the_retained_failure()
    {
        var responses = Enumerable.Range(0, 16).Select(_ => new ReceiptReading(HttpStatusCode.OK, SuccessBody)).ToArray();
        responses[5] = new ReceiptReading(HttpStatusCode.Conflict, ProblemBody(Unavailable));
        responses[12] = new ReceiptReading(HttpStatusCode.Conflict, ProblemBody(Conflict));

        // Request 5 names host 3 rather than its usual 5 % 4, so the record must carry the submission's own host ordinal.
        var submissions = responses
            .Select((response, index) => (Host: index == 5 ? 3 : index % 4, Response: Task.FromResult(response)))
            .ToArray();

        var failure = Assert.Throws<TrueException>(() => AssertEveryResponseSucceeded(responses, DescribeResponses(submissions)));

        Assert.Contains($"request=5 host=3 status=409 code={Unavailable}", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"request=12 host=0 status=409 code={Conflict}", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"request=0 host=0 status=200 code={MissingFallback}", failure.Message, StringComparison.Ordinal);
        Assert.Contains("16 of 16 submissions returned a response", failure.Message, StringComparison.Ordinal);
        AssertDiscloses(failure.Message, records: 16);
    }

    [Fact]
    public async Task Diagnostic_a_faulted_submission_keeps_its_own_exception_after_the_returned_responses_are_written()
    {
        var output = new CapturedOutput();
        var original = new HttpRequestException(Sentinel);
        var submissions = Enumerable.Range(0, 16).Select(index => Submission(index % 4, HttpStatusCode.OK, SuccessBody)).ToArray();
        submissions[7] = (3, Task.FromException<ReceiptReading>(original));
        submissions[9] = Submission(1, HttpStatusCode.Conflict, ProblemBody(Unavailable));

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => SettleAsync(submissions, output));

        Assert.Same(original, thrown);
        var written = Assert.Single(output.Lines);
        Assert.Contains("15 of 16 submissions returned a response", written, StringComparison.Ordinal);
        Assert.Contains($"request=9 host=1 status=409 code={Unavailable}", written, StringComparison.Ordinal);
        Assert.DoesNotContain("request=7 ", written, StringComparison.Ordinal);
        AssertDiscloses(written, records: 15);
    }

    [Fact]
    public async Task Diagnostic_settled_responses_are_written_once_and_returned_with_the_same_text()
    {
        var output = new CapturedOutput();
        var submissions = Enumerable.Range(0, 16).Select(index => Submission(index % 4, HttpStatusCode.OK, SuccessBody)).ToArray();

        var (responses, diagnostics) = await SettleAsync(submissions, output);

        Assert.Equal(16, responses.Length);
        Assert.Equal(diagnostics, Assert.Single(output.Lines));
        AssertEveryResponseSucceeded(responses, diagnostics);
        AssertDiscloses(diagnostics, records: 16);
    }

    [Theory]
    [InlineData("github_branch_publication_approval.invalid_request")]
    [InlineData("github_branch_publication_approval.body_not_allowed")]
    [InlineData("github_branch_publication_approval.body_too_large")]
    [InlineData("github_branch_publication_approval.not_found")]
    [InlineData("github_branch_publication_approval.unavailable")]
    [InlineData("github_branch_publication_approval.conflict")]
    public void Diagnostic_each_of_the_six_fixed_codes_is_admitted_exactly(string code)
    {
        Assert.Equal(6, DiagnosticCodes.Length);
        Assert.Contains(code, DiagnosticCodes);
        Assert.Equal(code, DiagnosticCodeOf(ProblemBody(code)));
    }

    [Theory]
    [InlineData("", "fallback:empty")]
    [InlineData("TEST-48-01-INERT-FICTITIOUS-SENTINEL", "fallback:malformed")]
    [InlineData("""{"errors":[{"code":"github_branch_publication_approval.conflict"}]} TEST-48-01-INERT-FICTITIOUS-SENTINEL""", "fallback:malformed")]
    [InlineData("""{"errors":[{"code":"github_branch_publication_approval.conflict",}]}""", "fallback:malformed")]
    [InlineData("""["TEST-48-01-INERT-FICTITIOUS-SENTINEL"]""", "fallback:malformed")]
    [InlineData("""{"errors":{"code":"github_branch_publication_approval.conflict"}}""", "fallback:malformed")]
    [InlineData("""{"errors":["TEST-48-01-INERT-FICTITIOUS-SENTINEL"]}""", "fallback:malformed")]
    [InlineData("""{"errors":[{"code":409}]}""", "fallback:malformed")]
    [InlineData("""{"errors":[{"code":null}]}""", "fallback:malformed")]
    [InlineData("""{"errors":[{"code":["github_branch_publication_approval.conflict"]}]}""", "fallback:malformed")]
    [InlineData("""{"title":"TEST-48-01-INERT-FICTITIOUS-SENTINEL"}""", "fallback:missing")]
    [InlineData("""{"Errors":[{"code":"github_branch_publication_approval.conflict"}]}""", "fallback:missing")]
    [InlineData("""{"errors":[]}""", "fallback:missing")]
    [InlineData("""{"errors":[{"Code":"github_branch_publication_approval.conflict"}]}""", "fallback:missing")]
    [InlineData("""{"errors":[{"code":"github_branch_publication_approval.conflict"}],"errors":[]}""", "fallback:ambiguous")]
    [InlineData("""{"errors":[{"code":"github_branch_publication_approval.conflict","code":"github_branch_publication_approval.unavailable"}]}""", "fallback:ambiguous")]
    [InlineData("""{"errors":[{"code":"github_branch_publication_approval.conflict"},{"code":"github_branch_publication_approval.conflict"}]}""", "fallback:ambiguous")]
    [InlineData("""{"errors":[{"code":"TEST-48-01-INERT-FICTITIOUS-SENTINEL"}]}""", "fallback:unknown")]
    [InlineData("""{"errors":[{"code":"github_branch_publication_approval.TEST-48-01-INERT-FICTITIOUS-SENTINEL"}]}""", "fallback:unknown")]
    [InlineData("""{"errors":[{"code":"GITHUB_BRANCH_PUBLICATION_APPROVAL.CONFLICT"}]}""", "fallback:unknown")]
    [InlineData("""{"errors":[{"code":"github_branch_publication_approval.conflict "}]}""", "fallback:unknown")]
    [InlineData("""{"errors":[{"code":"github_observation_target.conflict"}]}""", "fallback:unknown")]
    [InlineData("""{"errors":[{"code":"github_branch_publication_approval.immutable"}]}""", "fallback:unknown")]
    [InlineData("""{"errors":[{"code":""}]}""", "fallback:unknown")]
    public void Diagnostic_problem_data_that_is_not_one_exact_allowlisted_code_gets_a_fixed_fallback(string body, string expected)
    {
        Assert.Equal(expected, DiagnosticCodeOf(body));
        AssertDiscloses(DescribeResponses([Submission(2, HttpStatusCode.Conflict, body)]), records: 1);
    }

    [Fact]
    public void Diagnostic_escapes_are_decoded_once_and_never_admit_an_inexact_code()
    {
        var escapedConflict = $$"""{"errors":[{"code":"github_branch_publication_approval.con{{Backslash}}u0066lict"}]}""";
        var escapedSurrogate = $$"""{"errors":[{"code":"{{Backslash}}uD800{{Sentinel}}"}]}""";
        var rawSurrogate = "{\"errors\":[{\"code\":\"" + (char)0xD800 + Sentinel + "\"}]}";
        var nullCharacter = $$"""{"errors":[{"code":"github_branch_publication_approval.conflict{{Backslash}}u0000"}]}""";

        Assert.Equal(Conflict, DiagnosticCodeOf(escapedConflict));
        Assert.Equal(UnknownFallback, DiagnosticCodeOf(escapedSurrogate));
        Assert.Equal(UnknownFallback, DiagnosticCodeOf(rawSurrogate));
        Assert.Equal(UnknownFallback, DiagnosticCodeOf(nullCharacter));
    }

    [Fact]
    public void Diagnostic_input_is_bounded_to_8_KiB_of_UTF8_and_a_JSON_depth_of_8()
    {
        Assert.Equal(Conflict, DiagnosticCodeOf(Padded(ProblemBody(Conflict), MaxDiagnosticInputBytes, 'a')));
        Assert.Equal(OversizedFallback, DiagnosticCodeOf(Padded(ProblemBody(Conflict), MaxDiagnosticInputBytes + 1, 'a')));

        // Two UTF-8 bytes per character: fewer than 8192 characters, more than 8192 bytes.
        var multiByte = Padded(ProblemBody(Conflict), MaxDiagnosticInputBytes + 2, (char)0xE9);
        Assert.True(multiByte.Length <= MaxDiagnosticInputBytes);
        Assert.Equal(OversizedFallback, DiagnosticCodeOf(multiByte));
        Assert.Equal(OversizedFallback, DiagnosticCodeOf(new string('{', 1_000_000)));

        Assert.Equal(Conflict, DiagnosticCodeOf(Nested(ProblemBody(Conflict), MaxDiagnosticJsonDepth)));
        Assert.Equal(MalformedFallback, DiagnosticCodeOf(Nested(ProblemBody(Conflict), MaxDiagnosticJsonDepth + 1)));
    }

    [Fact]
    public void Diagnostic_at_most_sixteen_records_are_written_each_within_256_UTF8_bytes()
    {
        var longest = DiagnosticCodes.OrderByDescending(code => code.Length).First();
        var submissions = Enumerable.Range(0, 17)
            .Select(index => Submission(int.MinValue, (HttpStatusCode)(index % 2 == 0 ? int.MinValue : int.MaxValue), ProblemBody(longest)))
            .ToArray();

        var lines = DescribeResponses(submissions).Split(Environment.NewLine);

        Assert.Equal(1 + MaxDiagnosticRecords, lines.Length);
        Assert.Contains("17 of 17 submissions returned a response", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("request=15 ", lines[^1], StringComparison.Ordinal);
        Assert.All(lines, line => Assert.True(Encoding.UTF8.GetByteCount(line) <= MaxDiagnosticRecordBytes, line));
    }

    private static (int Host, Task<ReceiptReading> Response) Submission(int host, HttpStatusCode status, string body) =>
        (host, Task.FromResult(new ReceiptReading(status, body)));

    private static string ProblemBody(string code) => $$"""
        {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"{{Sentinel}}","status":409,"detail":"{{Sentinel}}",
        "instance":"/api/runs/{{Sentinel}}","traceId":"{{Sentinel}}","{{Sentinel}}":["{{Sentinel}}"],
        "errors":[{"code":"{{code}}","message":"{{Sentinel}}","type":"{{Sentinel}}"}]}
        """;

    /// <summary>Appends a padding member so the whole body is exactly <paramref name="bytes"/> UTF-8 bytes (or one more when an odd
    /// remainder meets a two-byte padding character).</summary>
    private static string Padded(string body, int bytes, char padding)
    {
        var prefix = body[..^1] + ",\"padding\":\"";
        const string suffix = "\"}";
        var remaining = bytes - Encoding.UTF8.GetByteCount(prefix + suffix);
        var width = Encoding.UTF8.GetByteCount(new string(padding, 1));
        var count = (remaining + width - 1) / width;
        var padded = prefix + new string(padding, count) + suffix;
        Assert.InRange(Encoding.UTF8.GetByteCount(padded), bytes, bytes + 1);
        return padded;
    }

    /// <summary>Adds a member whose arrays bring the document to exactly <paramref name="depth"/> nested containers.</summary>
    private static string Nested(string body, int depth) =>
        body[..^1] + ",\"nested\":" + new string('[', depth - 1) + "1" + new string(']', depth - 1) + "}";

    private static void AssertDiscloses(string diagnostics, int records)
    {
        var lines = diagnostics.Split(Environment.NewLine);
        Assert.Equal(1 + records, lines.Length);
        Assert.All(lines.Skip(1), line => Assert.Matches(@"^request=\d+ host=-?\d+ status=-?\d+ code=[a-z_.:]+$", line));
        Assert.All(lines, line => Assert.True(Encoding.UTF8.GetByteCount(line) <= MaxDiagnosticRecordBytes, line));
        Assert.DoesNotContain(Sentinel, diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("TEST-48-01", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("{", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("\"", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/", diagnostics, StringComparison.Ordinal);
    }

    private sealed class CapturedOutput : ITestOutputHelper
    {
        public List<string> Lines { get; } = [];

        public void WriteLine(string message) => Lines.Add(message);

        public void WriteLine(string format, params object[] args) => Lines.Add(string.Format(CultureInfo.InvariantCulture, format, args));
    }
}
