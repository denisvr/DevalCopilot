using System.Text;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The operation-owned admission rules of the check-context observation (ADR-0045): identifiers, the label text contract, the closed
/// literal enumerations, a single context and the consistency of a first page. Pure and independent of any provider or database. Every
/// special character is built from its code point so the source stays ASCII and no tool or serializer can rewrite it.
/// </summary>
public sealed class GetGitHubCheckContextsObservationPolicyTests
{
    private const string Run = GitHubCheckContextsPolicy.CheckRunKind;
    private const string Status = GitHubCheckContextsPolicy.StatusContextKind;

    private static string C(int codePoint) => char.ConvertFromUtf32(codePoint);

    private static string Lone(int utf16Unit) => ((char)utf16Unit).ToString();

    private static string Repeat(int codePoint, int count) => string.Concat(Enumerable.Repeat(C(codePoint), count));

    private static GitHubCheckContext CheckRun(
        string id = "CR_1", string name = "build", string status = "COMPLETED", string? conclusion = "SUCCESS", string? suite = "CS_1") =>
        new(Run, id, name, status, conclusion, suite);

    private static GitHubCheckContext StatusContext(string id = "SC_1", string name = "ci/legacy", string state = "SUCCESS") =>
        new(Status, id, name, state, null, null);

    private static GitHubCheckContextsPage Page(int total, params GitHubCheckContext[] contexts) => new(total, contexts);

    private static GitHubCheckContext[] Distinct(int count) =>
        Enumerable.Range(0, count).Select(index => CheckRun(id: $"CR_{index}")).ToArray();

    [Theory]
    [InlineData("a", true)]
    [InlineData("CR_kwDOABC123-xyz", true)]
    [InlineData("SC_kwDO/abc+def=", true)]
    [InlineData("_-/+=", true)]
    [InlineData("0", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("has space", false)]
    [InlineData("a.b", false)]
    [InlineData("a:b", false)]
    [InlineData("a%41", false)]
    [InlineData("a\tb", false)]
    [InlineData("a\nb", false)]
    [InlineData("a\\b", false)]
    [InlineData("a\"b", false)]
    [InlineData("a<b", false)]
    public void An_opaque_identifier_is_one_to_256_ascii_letters_digits_and_four_punctuation_characters(string value, bool admitted)
    {
        Assert.Equal(admitted, GitHubCheckContextsPolicy.IsContextId(value));
    }

    [Fact]
    public void An_identifier_has_exact_length_bounds_and_only_ascii()
    {
        Assert.True(GitHubCheckContextsPolicy.IsContextId(new string('a', 256)));
        Assert.False(GitHubCheckContextsPolicy.IsContextId(new string('a', 257)));
        Assert.False(GitHubCheckContextsPolicy.IsContextId(null));
        Assert.False(GitHubCheckContextsPolicy.IsContextId("R_k" + C(0xE9)));
        Assert.False(GitHubCheckContextsPolicy.IsContextId(C(0xFF21)));
        Assert.False(GitHubCheckContextsPolicy.IsContextId("a" + C(0)));
        Assert.False(GitHubCheckContextsPolicy.IsContextId("a\\" + "u0041"));
    }

    [Theory]
    [InlineData("build")]
    [InlineData("build (ubuntu-latest, 3.12)")]
    [InlineData(" leading and trailing ")]
    [InlineData("quote \" and backslash \\ and slash / end")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("[link](https://evil.example) **bold**")]
    [InlineData("&amp; &lt;b&gt;")]
    [InlineData("a")]
    [InlineData("literal backslash-u text \\" + "u0041 stays text")]
    public void An_ascii_label_with_quotes_backslashes_and_markup_is_admitted_exactly(string value)
    {
        Assert.True(GitHubCheckContextsPolicy.IsLabel(value));
    }

    [Fact]
    public void A_well_formed_non_ascii_label_is_admitted_including_a_surrogate_pair_and_an_inner_no_break_space()
    {
        Assert.True(GitHubCheckContextsPolicy.IsLabel("un" + C(0xEF) + "c" + C(0xF6) + "d" + C(0xE9)));
        Assert.True(GitHubCheckContextsPolicy.IsLabel(C(0x65E5) + C(0x672C) + C(0x8A9E)));
        Assert.True(GitHubCheckContextsPolicy.IsLabel(C(0x0639) + C(0x0631) + C(0x0628)));
        Assert.True(GitHubCheckContextsPolicy.IsLabel("pair " + C(0x1F600) + " end"));
        Assert.True(GitHubCheckContextsPolicy.IsLabel("a" + C(0xA0) + "b"));
        Assert.True(GitHubCheckContextsPolicy.IsLabel(C(0xB7)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("a\tb")]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    public void A_blank_or_ascii_control_label_is_refused(string value)
    {
        Assert.False(GitHubCheckContextsPolicy.IsLabel(value));
    }

    [Theory]
    [InlineData(0x0000)]
    [InlineData(0x001F)]
    [InlineData(0x007F)]
    [InlineData(0x0085)]
    [InlineData(0x009F)]
    [InlineData(0x00AD)]
    [InlineData(0x200B)]
    [InlineData(0x200E)]
    [InlineData(0x202E)]
    [InlineData(0x2066)]
    [InlineData(0xFEFF)]
    [InlineData(0x2028)]
    [InlineData(0x2029)]
    [InlineData(0xFFFD)]
    [InlineData(0xE0001)]
    public void A_label_with_a_control_format_separator_or_replacement_character_is_refused_wherever_it_occurs(int codePoint)
    {
        Assert.False(GitHubCheckContextsPolicy.IsLabel("a" + C(codePoint) + "b"));
        Assert.False(GitHubCheckContextsPolicy.IsLabel(C(codePoint) + "ab"));
        Assert.False(GitHubCheckContextsPolicy.IsLabel("ab" + C(codePoint)));
    }

    [Theory]
    [InlineData(0x00A0)]
    [InlineData(0x2003)]
    [InlineData(0x3000)]
    public void A_label_that_is_only_unicode_whitespace_is_blank_and_refused(int codePoint)
    {
        Assert.False(GitHubCheckContextsPolicy.IsLabel(C(codePoint)));
        Assert.False(GitHubCheckContextsPolicy.IsLabel(C(codePoint) + C(codePoint)));
    }

    [Fact]
    public void A_label_with_a_lone_or_misordered_surrogate_is_refused()
    {
        Assert.False(GitHubCheckContextsPolicy.IsLabel("a" + Lone(0xD800) + "b"));
        Assert.False(GitHubCheckContextsPolicy.IsLabel("a" + Lone(0xDC00) + "b"));
        Assert.False(GitHubCheckContextsPolicy.IsLabel("a" + Lone(0xDC00) + Lone(0xD800) + "b"));
        Assert.False(GitHubCheckContextsPolicy.IsLabel(Lone(0xD800)));
        Assert.False(GitHubCheckContextsPolicy.IsLabel(Lone(0xDBFF) + "x"));
        Assert.False(GitHubCheckContextsPolicy.IsLabel("x" + Lone(0xD83D)));
    }

    [Fact]
    public void A_label_is_bounded_by_utf8_bytes_not_characters()
    {
        Assert.False(GitHubCheckContextsPolicy.IsLabel(null));
        Assert.True(GitHubCheckContextsPolicy.IsLabel(new string('a', 256)));
        Assert.False(GitHubCheckContextsPolicy.IsLabel(new string('a', 257)));
        Assert.Equal(256, Encoding.UTF8.GetByteCount(Repeat(0xE9, 128)));
        Assert.True(GitHubCheckContextsPolicy.IsLabel(Repeat(0xE9, 128)));
        Assert.False(GitHubCheckContextsPolicy.IsLabel(Repeat(0xE9, 129)));
        Assert.True(GitHubCheckContextsPolicy.IsLabel(Repeat(0x65E5, 85) + "a"));
        Assert.False(GitHubCheckContextsPolicy.IsLabel(Repeat(0x65E5, 86)));
        Assert.True(GitHubCheckContextsPolicy.IsLabel(Repeat(0x1F600, 64)));
        Assert.False(GitHubCheckContextsPolicy.IsLabel(Repeat(0x1F600, 65)));
    }

    [Theory]
    [InlineData("COMPLETED", true)]
    [InlineData("IN_PROGRESS", true)]
    [InlineData("PENDING", true)]
    [InlineData("QUEUED", true)]
    [InlineData("REQUESTED", true)]
    [InlineData("WAITING", true)]
    [InlineData("completed", false)]
    [InlineData("Completed", false)]
    [InlineData(" COMPLETED", false)]
    [InlineData("COMPLETED ", false)]
    [InlineData("SUCCESS", false)]
    [InlineData("FAILURE", false)]
    [InlineData("ERROR", false)]
    [InlineData("EXPECTED", false)]
    [InlineData("STARTUP_FAILURE", false)]
    [InlineData("IN-PROGRESS", false)]
    [InlineData("RUNNING", false)]
    [InlineData("", false)]
    public void A_check_run_status_is_exactly_one_of_the_six_literal_values(string value, bool admitted)
    {
        Assert.Equal(admitted, GitHubCheckContextsPolicy.IsCheckRunStatus(value));
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
    [InlineData("SUCCESS ", false)]
    [InlineData("CANCELED", false)]
    [InlineData("COMPLETED", false)]
    [InlineData("ERROR", false)]
    [InlineData("PENDING", false)]
    [InlineData("PASSED", false)]
    [InlineData("GREEN", false)]
    [InlineData("TIMEOUT", false)]
    [InlineData("", false)]
    public void A_check_run_conclusion_is_exactly_one_of_the_nine_literal_values(string value, bool admitted)
    {
        Assert.Equal(admitted, GitHubCheckContextsPolicy.IsCheckRunConclusion(value));
    }

    [Theory]
    [InlineData("ERROR", true)]
    [InlineData("EXPECTED", true)]
    [InlineData("FAILURE", true)]
    [InlineData("PENDING", true)]
    [InlineData("SUCCESS", true)]
    [InlineData("success", false)]
    [InlineData(" SUCCESS", false)]
    [InlineData("COMPLETED", false)]
    [InlineData("QUEUED", false)]
    [InlineData("NEUTRAL", false)]
    [InlineData("SUCCESSFUL", false)]
    [InlineData("PASSED", false)]
    [InlineData("", false)]
    public void A_status_context_state_is_exactly_one_of_the_five_literal_values(string value, bool admitted)
    {
        Assert.Equal(admitted, GitHubCheckContextsPolicy.IsStatusContextState(value));
    }

    [Fact]
    public void An_absent_value_is_never_an_admitted_literal()
    {
        Assert.False(GitHubCheckContextsPolicy.IsCheckRunStatus(null));
        Assert.False(GitHubCheckContextsPolicy.IsCheckRunConclusion(null));
        Assert.False(GitHubCheckContextsPolicy.IsStatusContextState(null));
    }

    [Fact]
    public void A_check_run_is_admitted_with_every_status_and_a_null_or_literal_conclusion_independently()
    {
        foreach (var status in new[] { "COMPLETED", "IN_PROGRESS", "PENDING", "QUEUED", "REQUESTED", "WAITING" })
        {
            foreach (var conclusion in new string?[]
                     {
                         null, "ACTION_REQUIRED", "CANCELLED", "FAILURE", "NEUTRAL", "SKIPPED", "STALE", "STARTUP_FAILURE", "SUCCESS",
                         "TIMED_OUT",
                     })
            {
                Assert.True(GitHubCheckContextsPolicy.IsContext(CheckRun(status: status, conclusion: conclusion)), $"{status} {conclusion}");
            }
        }
    }

    [Fact]
    public void A_status_context_is_admitted_with_each_state_and_no_conclusion_or_suite()
    {
        foreach (var state in new[] { "ERROR", "EXPECTED", "FAILURE", "PENDING", "SUCCESS" })
        {
            Assert.True(GitHubCheckContextsPolicy.IsContext(StatusContext(state: state)), state);
        }
    }

    [Fact]
    public void A_context_that_breaks_its_kind_shape_is_refused()
    {
        Assert.False(GitHubCheckContextsPolicy.IsContext(null));
        Assert.False(GitHubCheckContextsPolicy.IsContext(CheckRun(suite: null)));
        Assert.False(GitHubCheckContextsPolicy.IsContext(CheckRun(suite: "")));
        Assert.False(GitHubCheckContextsPolicy.IsContext(CheckRun(suite: "has space")));
        Assert.False(GitHubCheckContextsPolicy.IsContext(CheckRun(suite: new string('a', 257))));
        Assert.False(GitHubCheckContextsPolicy.IsContext(CheckRun(status: "SUCCESS")));
        Assert.False(GitHubCheckContextsPolicy.IsContext(CheckRun(status: "completed")));
        Assert.False(GitHubCheckContextsPolicy.IsContext(CheckRun(conclusion: "success")));
        Assert.False(GitHubCheckContextsPolicy.IsContext(CheckRun(conclusion: "COMPLETED")));
        Assert.False(GitHubCheckContextsPolicy.IsContext(CheckRun(conclusion: "")));
        Assert.False(GitHubCheckContextsPolicy.IsContext(CheckRun(id: "bad id")));
        Assert.False(GitHubCheckContextsPolicy.IsContext(CheckRun(name: "bad\nname")));
        Assert.False(GitHubCheckContextsPolicy.IsContext(StatusContext(state: "COMPLETED")));
        Assert.False(GitHubCheckContextsPolicy.IsContext(StatusContext(state: "success")));
        Assert.False(GitHubCheckContextsPolicy.IsContext(StatusContext(id: "bad id")));
        Assert.False(GitHubCheckContextsPolicy.IsContext(StatusContext(name: C(0x200B))));
        Assert.False(GitHubCheckContextsPolicy.IsContext(StatusContext() with { Conclusion = "SUCCESS" }));
        Assert.False(GitHubCheckContextsPolicy.IsContext(StatusContext() with { CheckSuiteId = "CS_1" }));
        Assert.False(GitHubCheckContextsPolicy.IsContext(CheckRun() with { Kind = "Unknown" }));
        Assert.False(GitHubCheckContextsPolicy.IsContext(CheckRun() with { Kind = "checkrun" }));
        Assert.False(GitHubCheckContextsPolicy.IsContext(CheckRun() with { Kind = "CheckRun " }));
        Assert.False(GitHubCheckContextsPolicy.IsContext(CheckRun() with { Kind = string.Empty }));
    }

    [Fact]
    public void A_page_has_exactly_the_lesser_of_fifty_and_the_total_as_its_entries()
    {
        Assert.True(GitHubCheckContextsPolicy.IsPage(Page(0)));
        Assert.True(GitHubCheckContextsPolicy.IsPage(Page(1, CheckRun())));
        Assert.True(GitHubCheckContextsPolicy.IsPage(new GitHubCheckContextsPage(50, Distinct(50))));
        Assert.True(GitHubCheckContextsPolicy.IsPage(new GitHubCheckContextsPage(51, Distinct(50))));
        Assert.True(GitHubCheckContextsPolicy.IsPage(new GitHubCheckContextsPage(int.MaxValue, Distinct(50))));

        Assert.False(GitHubCheckContextsPolicy.IsPage(null));
        Assert.False(GitHubCheckContextsPolicy.IsPage(Page(1)));
        Assert.False(GitHubCheckContextsPolicy.IsPage(Page(0, CheckRun())));
        Assert.False(GitHubCheckContextsPolicy.IsPage(new GitHubCheckContextsPage(3, Distinct(2))));
        Assert.False(GitHubCheckContextsPolicy.IsPage(new GitHubCheckContextsPage(2, Distinct(3))));
        Assert.False(GitHubCheckContextsPolicy.IsPage(new GitHubCheckContextsPage(50, Distinct(49))));
        Assert.False(GitHubCheckContextsPolicy.IsPage(new GitHubCheckContextsPage(51, Distinct(49))));
        Assert.False(GitHubCheckContextsPolicy.IsPage(new GitHubCheckContextsPage(51, Distinct(51))));
        Assert.False(GitHubCheckContextsPolicy.IsPage(new GitHubCheckContextsPage(-1, [])));
        Assert.False(GitHubCheckContextsPolicy.IsPage(new GitHubCheckContextsPage(int.MinValue, [])));
        Assert.False(GitHubCheckContextsPolicy.IsPage(new GitHubCheckContextsPage(1, null!)));
    }

    [Fact]
    public void A_page_rejects_duplicate_identifiers_ordinally_but_keeps_repeated_names_and_shared_suites_in_provider_order()
    {
        Assert.False(GitHubCheckContextsPolicy.IsPage(Page(2, CheckRun(id: "CR_1", name: "a"), CheckRun(id: "CR_1", name: "b"))));
        Assert.False(GitHubCheckContextsPolicy.IsPage(Page(2, CheckRun(id: "X_1"), StatusContext(id: "X_1"))));
        Assert.True(GitHubCheckContextsPolicy.IsPage(Page(2, CheckRun(id: "CR_a"), CheckRun(id: "CR_A"))));
        Assert.True(GitHubCheckContextsPolicy.IsPage(Page(
            3, CheckRun(id: "CR_1", name: "build", suite: "CS_1"), CheckRun(id: "CR_2", name: "build", suite: "CS_1"),
            StatusContext(id: "SC_1", name: "build"))));
    }

    [Fact]
    public void A_page_with_one_invalid_or_absent_entry_is_refused_whole()
    {
        Assert.False(GitHubCheckContextsPolicy.IsPage(Page(2, CheckRun(id: "CR_1"), CheckRun(id: "CR_2", conclusion: "success"))));
        Assert.False(GitHubCheckContextsPolicy.IsPage(Page(2, CheckRun(id: "CR_1"), null!)));
        Assert.False(GitHubCheckContextsPolicy.IsPage(Page(2, CheckRun(id: "CR_1"), StatusContext(id: "SC_2", state: "QUEUED"))));
    }
}
