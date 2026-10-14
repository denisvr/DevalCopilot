using System.Text;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The operation-owned admission rules of the check-run output inspection (ADR-0047): the request identifier, the safe check name, the
/// closed literal enumerations, the output text contract and its byte bounds, the check association and the node shape. Pure and
/// independent of any provider or database. Every special character is built from its code point so the source stays ASCII.
/// </summary>
public sealed class GetGitHubCheckRunOutputObservationPolicyTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private const string Repo = "R_kgDOExample01";

    private static string C(int codePoint) => char.ConvertFromUtf32(codePoint);

    private static string Repeat(int codePoint, int count) => string.Concat(Enumerable.Repeat(C(codePoint), count));

    private static GitHubCheckRunAssociation Association(
        string id = "CR_1", string runRepo = Repo, string suite = "CS_1", string suiteRepo = Repo, string sha = Sha) =>
        new(id, runRepo, suite, suiteRepo, sha);

    private static GitHubCheckRunDetail Detail(
        string name = "build", string status = "COMPLETED", string? conclusion = "FAILURE", GitHubCheckRunOutputText? output = null) =>
        new(name, status, conclusion, output ?? new GitHubCheckRunOutputText("t", "s", "x"));

    [Theory]
    [InlineData("a", true)]
    [InlineData("CR_kwDOABC123-xyz", true)]
    [InlineData("CR_kwDO/abc+def=", true)]
    [InlineData("_-/+=", true)]
    [InlineData("0", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData(" a", false)]
    [InlineData("a ", false)]
    [InlineData("has space", false)]
    [InlineData("a.b", false)]
    [InlineData("a:b", false)]
    [InlineData("a%41", false)]
    [InlineData("a\tb", false)]
    [InlineData("a\nb", false)]
    [InlineData("a\\b", false)]
    [InlineData("a\"b", false)]
    [InlineData("a,b", false)]
    public void The_request_identifier_is_one_to_256_ascii_letters_digits_and_four_punctuation_characters(string value, bool admitted)
    {
        Assert.Equal(admitted, GitHubCheckRunOutputPolicy.IsCheckRunId(value));
    }

    [Fact]
    public void The_request_identifier_has_exact_length_bounds_and_only_ascii()
    {
        Assert.True(GitHubCheckRunOutputPolicy.IsCheckRunId(new string('a', 256)));
        Assert.False(GitHubCheckRunOutputPolicy.IsCheckRunId(new string('a', 257)));
        Assert.False(GitHubCheckRunOutputPolicy.IsCheckRunId(null));
        Assert.False(GitHubCheckRunOutputPolicy.IsCheckRunId("R_k" + C(0xE9)));
        Assert.False(GitHubCheckRunOutputPolicy.IsCheckRunId(C(0xFF21)));
        Assert.False(GitHubCheckRunOutputPolicy.IsCheckRunId("a" + C(0)));
    }

    [Theory]
    [InlineData("build")]
    [InlineData(" leading and trailing ")]
    [InlineData("quote \" and backslash \\ and slash / end")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("[link](https://evil.example) **bold**")]
    public void A_safe_check_name_admits_ascii_markup_as_inert_text(string value)
    {
        Assert.True(GitHubCheckRunOutputPolicy.IsName(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\tb")]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    public void A_blank_or_control_check_name_is_refused_including_tab_and_line_breaks(string value)
    {
        Assert.False(GitHubCheckRunOutputPolicy.IsName(value));
    }

    [Fact]
    public void A_check_name_has_an_exact_utf8_byte_bound_and_no_format_separator_or_replacement_character()
    {
        Assert.True(GitHubCheckRunOutputPolicy.IsName(new string('a', 256)));
        Assert.False(GitHubCheckRunOutputPolicy.IsName(new string('a', 257)));
        Assert.True(GitHubCheckRunOutputPolicy.IsName(Repeat(0xE9, 128)));
        Assert.False(GitHubCheckRunOutputPolicy.IsName(Repeat(0xE9, 129)));
        Assert.True(GitHubCheckRunOutputPolicy.IsName(Repeat(0x1F600, 64)));
        Assert.False(GitHubCheckRunOutputPolicy.IsName(Repeat(0x1F600, 65)));
        foreach (var codePoint in new[] { 0x0000, 0x007F, 0x0085, 0x00AD, 0x200B, 0x202E, 0xFEFF, 0x2028, 0x2029, 0xFFFD })
        {
            Assert.False(GitHubCheckRunOutputPolicy.IsName("a" + C(codePoint) + "b"));
        }

        Assert.False(GitHubCheckRunOutputPolicy.IsName("a" + (char)0xD800 + "b"));
        Assert.False(GitHubCheckRunOutputPolicy.IsName("a" + (char)0xDC00 + "b"));
        Assert.False(GitHubCheckRunOutputPolicy.IsName(null));
    }

    [Theory]
    [InlineData("COMPLETED", true)]
    [InlineData("IN_PROGRESS", true)]
    [InlineData("PENDING", true)]
    [InlineData("QUEUED", true)]
    [InlineData("REQUESTED", true)]
    [InlineData("WAITING", true)]
    [InlineData("completed", false)]
    [InlineData("SUCCESS", false)]
    [InlineData("", false)]
    [InlineData(" COMPLETED", false)]
    public void Exactly_the_six_check_run_status_literals_are_admitted(string value, bool admitted)
    {
        Assert.Equal(admitted, GitHubCheckRunOutputPolicy.IsStatus(value));
    }

    [Theory]
    [InlineData("ACTION_REQUIRED", true)]
    [InlineData("CANCELLED", true)]
    [InlineData("FAILURE", true)]
    [InlineData("NEUTRAL", true)]
    [InlineData("SKIPPED", true)]
    [InlineData("STALE", true)]
    [InlineData("STARTUP_FAILURE", true)]
    [InlineData("SUCCESS", true)]
    [InlineData("TIMED_OUT", true)]
    [InlineData("success", false)]
    [InlineData("COMPLETED", false)]
    [InlineData("", false)]
    public void Exactly_the_nine_conclusion_literals_are_admitted(string value, bool admitted)
    {
        Assert.Equal(admitted, GitHubCheckRunOutputPolicy.IsConclusion(value));
    }

    [Theory]
    [InlineData("Repository", true)]
    [InlineData("CheckRun", true)]
    [InlineData("_Hidden1", true)]
    [InlineData("", false)]
    [InlineData("1Type", false)]
    [InlineData("Bad-Type", false)]
    [InlineData("Bad Type", false)]
    [InlineData("Bad\\u0041", false)]
    public void A_graphql_type_name_has_only_the_literal_name_shape(string value, bool admitted)
    {
        Assert.Equal(admitted, GitHubCheckRunOutputPolicy.IsTypeName(value));
    }

    [Fact]
    public void A_type_name_is_bounded()
    {
        Assert.True(GitHubCheckRunOutputPolicy.IsTypeName(new string('T', 64)));
        Assert.False(GitHubCheckRunOutputPolicy.IsTypeName(new string('T', 65)));
        Assert.False(GitHubCheckRunOutputPolicy.IsTypeName(null));
    }

    [Fact]
    public void Output_text_may_be_null_empty_or_whitespace_and_keeps_tab_line_feed_and_carriage_return()
    {
        Assert.True(GitHubCheckRunOutputPolicy.IsOutputField(null, 16));
        Assert.True(GitHubCheckRunOutputPolicy.IsOutputField(string.Empty, 16));
        Assert.True(GitHubCheckRunOutputPolicy.IsOutputField("   ", 16));
        Assert.True(GitHubCheckRunOutputPolicy.IsOutputField("a\tb\r\nc\nd\re", 16));
        Assert.True(GitHubCheckRunOutputPolicy.IsOutputField("<b>x</b> [l](u) \\ \" '", 32));
    }

    [Fact]
    public void Output_text_refuses_every_other_control_format_separator_replacement_and_malformed_character()
    {
        foreach (var codePoint in new[]
                 {
                     0x0000, 0x0001, 0x0008, 0x000B, 0x000C, 0x000E, 0x001B, 0x001F, 0x007F, 0x0085, 0x009F, 0x00AD, 0x200B, 0x200E, 0x202E,
                     0x2066, 0xFEFF, 0x2028, 0x2029, 0xFFFD,
                 })
        {
            Assert.False(GitHubCheckRunOutputPolicy.IsOutputField("a" + C(codePoint) + "b", 64), $"U+{codePoint:X4}");
        }

        Assert.False(GitHubCheckRunOutputPolicy.IsOutputField("a" + (char)0xD800 + "b", 64));
        Assert.False(GitHubCheckRunOutputPolicy.IsOutputField("a" + (char)0xDC00 + "b", 64));
        Assert.False(GitHubCheckRunOutputPolicy.IsOutputField("a" + (char)0xDC00 + (char)0xD800 + "b", 64));
        Assert.True(GitHubCheckRunOutputPolicy.IsOutputField("pair " + C(0x1F600) + C(0xE9) + C(0x65E5), 64));
    }

    [Fact]
    public void Output_text_has_exact_utf8_byte_bounds_before_anything_is_truncated()
    {
        Assert.True(GitHubCheckRunOutputPolicy.IsOutputField(new string('a', 16), 16));
        Assert.False(GitHubCheckRunOutputPolicy.IsOutputField(new string('a', 17), 16));
        Assert.True(GitHubCheckRunOutputPolicy.IsOutputField(Repeat(0xE9, 8), 16));
        Assert.False(GitHubCheckRunOutputPolicy.IsOutputField(Repeat(0xE9, 9), 16));
        Assert.True(GitHubCheckRunOutputPolicy.IsOutputField(Repeat(0x1F600, 4), 16));
        Assert.False(GitHubCheckRunOutputPolicy.IsOutputField(Repeat(0x1F600, 5), 16));
        Assert.Equal(16, Encoding.UTF8.GetByteCount(Repeat(0x1F600, 4)));
    }

    [Fact]
    public void An_output_enforces_each_field_bound_and_the_combined_bound_with_each_field_at_its_own_limit()
    {
        var title = new string('t', GitHubCheckRunOutputPolicy.MaxTitleBytes);
        var summary = new string('s', GitHubCheckRunOutputPolicy.MaxSummaryBytes);
        var text = new string('x', GitHubCheckRunOutputPolicy.MaxTextBytes);

        Assert.Equal(256, GitHubCheckRunOutputPolicy.MaxTitleBytes);
        Assert.Equal(8192, GitHubCheckRunOutputPolicy.MaxSummaryBytes);
        Assert.Equal(16384, GitHubCheckRunOutputPolicy.MaxTextBytes);
        Assert.Equal(24576, GitHubCheckRunOutputPolicy.MaxCombinedOutputBytes);

        Assert.True(GitHubCheckRunOutputPolicy.IsOutput(new GitHubCheckRunOutputText(title, null, null)));
        Assert.True(GitHubCheckRunOutputPolicy.IsOutput(new GitHubCheckRunOutputText(null, summary, null)));
        Assert.True(GitHubCheckRunOutputPolicy.IsOutput(new GitHubCheckRunOutputText(null, null, text)));
        Assert.True(GitHubCheckRunOutputPolicy.IsOutput(new GitHubCheckRunOutputText(null, summary, text)));
        // The three maxima sum to more than the combined bound, so all three at their own limit is refused, one byte under is admitted.
        Assert.True(GitHubCheckRunOutputPolicy.IsOutput(new GitHubCheckRunOutputText(title, summary, text[..^256])));
        Assert.False(GitHubCheckRunOutputPolicy.IsOutput(new GitHubCheckRunOutputText(title, summary, text[..^255])));
        Assert.False(GitHubCheckRunOutputPolicy.IsOutput(new GitHubCheckRunOutputText(title, summary, text)));
        Assert.False(GitHubCheckRunOutputPolicy.IsOutput(new GitHubCheckRunOutputText(title + "t", null, null)));
        Assert.False(GitHubCheckRunOutputPolicy.IsOutput(new GitHubCheckRunOutputText(null, summary + "s", null)));
        Assert.False(GitHubCheckRunOutputPolicy.IsOutput(new GitHubCheckRunOutputText(null, null, text + "x")));
    }

    [Fact]
    public void An_output_of_three_null_fields_is_admitted_and_a_missing_output_object_is_not()
    {
        Assert.True(GitHubCheckRunOutputPolicy.IsOutput(new GitHubCheckRunOutputText(null, null, null)));
        Assert.True(GitHubCheckRunOutputPolicy.IsOutput(new GitHubCheckRunOutputText(string.Empty, "  ", "\n")));
        Assert.False(GitHubCheckRunOutputPolicy.IsOutput(null));
        Assert.False(GitHubCheckRunOutputPolicy.IsOutput(new GitHubCheckRunOutputText("a" + C(0x202E), null, null)));
    }

    [Fact]
    public void An_association_needs_the_requested_check_id_coherent_repositories_valid_identifiers_and_a_lowercase_commit()
    {
        Assert.True(GitHubCheckRunOutputPolicy.IsAssociation(Association(), "CR_1"));
        Assert.False(GitHubCheckRunOutputPolicy.IsAssociation(Association(), "CR_2"));
        Assert.False(GitHubCheckRunOutputPolicy.IsAssociation(Association(id: "cr_1"), "CR_1"));
        Assert.False(GitHubCheckRunOutputPolicy.IsAssociation(Association(runRepo: "R_other"), "CR_1"));
        Assert.False(GitHubCheckRunOutputPolicy.IsAssociation(Association(suiteRepo: "R_other"), "CR_1"));
        Assert.False(GitHubCheckRunOutputPolicy.IsAssociation(Association(runRepo: "bad id", suiteRepo: "bad id"), "CR_1"));
        Assert.False(GitHubCheckRunOutputPolicy.IsAssociation(Association(suite: "bad id"), "CR_1"));
        Assert.False(GitHubCheckRunOutputPolicy.IsAssociation(Association(sha: Sha.ToUpperInvariant()), "CR_1"));
        Assert.False(GitHubCheckRunOutputPolicy.IsAssociation(Association(sha: Sha[..39]), "CR_1"));
        Assert.False(GitHubCheckRunOutputPolicy.IsAssociation(null, "CR_1"));
    }

    [Fact]
    public void A_detail_validates_the_name_status_conclusion_and_output_independently_without_any_pairing_rule()
    {
        Assert.True(GitHubCheckRunOutputPolicy.IsDetail(Detail()));
        Assert.True(GitHubCheckRunOutputPolicy.IsDetail(Detail(status: "IN_PROGRESS", conclusion: "SUCCESS")));
        Assert.True(GitHubCheckRunOutputPolicy.IsDetail(Detail(status: "COMPLETED", conclusion: null)));
        Assert.False(GitHubCheckRunOutputPolicy.IsDetail(Detail(name: "a\nb")));
        Assert.False(GitHubCheckRunOutputPolicy.IsDetail(Detail(status: "DONE")));
        Assert.False(GitHubCheckRunOutputPolicy.IsDetail(Detail(conclusion: "passed")));
        Assert.False(GitHubCheckRunOutputPolicy.IsDetail(Detail(output: new GitHubCheckRunOutputText(new string('t', 257), null, null))));
        Assert.False(GitHubCheckRunOutputPolicy.IsDetail(null));
    }

    [Fact]
    public void A_node_is_null_a_check_run_with_its_association_or_another_type_with_nothing_else()
    {
        Assert.True(GitHubCheckRunOutputPolicy.IsNode(null, "CR_1", requireDetail: false));
        Assert.True(GitHubCheckRunOutputPolicy.IsNode(null, "CR_1", requireDetail: true));

        var run = new GitHubCheckRunNode("CheckRun", Association(), null);
        Assert.True(GitHubCheckRunOutputPolicy.IsNode(run, "CR_1", requireDetail: false));
        Assert.False(GitHubCheckRunOutputPolicy.IsNode(run, "CR_1", requireDetail: true));
        Assert.False(GitHubCheckRunOutputPolicy.IsNode(run, "CR_2", requireDetail: false));
        Assert.False(GitHubCheckRunOutputPolicy.IsNode(run with { Association = null }, "CR_1", requireDetail: false));

        var withDetail = run with { Detail = Detail() };
        Assert.True(GitHubCheckRunOutputPolicy.IsNode(withDetail, "CR_1", requireDetail: true));
        Assert.False(GitHubCheckRunOutputPolicy.IsNode(withDetail, "CR_1", requireDetail: false));
        Assert.False(GitHubCheckRunOutputPolicy.IsNode(run with { Detail = Detail(status: "DONE") }, "CR_1", requireDetail: true));

        var other = new GitHubCheckRunNode("Repository", null, null);
        Assert.True(GitHubCheckRunOutputPolicy.IsNode(other, "CR_1", requireDetail: false));
        Assert.True(GitHubCheckRunOutputPolicy.IsNode(other, "CR_1", requireDetail: true));
        Assert.False(GitHubCheckRunOutputPolicy.IsNode(other with { Association = Association() }, "CR_1", requireDetail: false));
        Assert.False(GitHubCheckRunOutputPolicy.IsNode(other with { Detail = Detail() }, "CR_1", requireDetail: true));
        Assert.False(GitHubCheckRunOutputPolicy.IsNode(new GitHubCheckRunNode("Bad-Type", null, null), "CR_1", requireDetail: false));
    }
}
