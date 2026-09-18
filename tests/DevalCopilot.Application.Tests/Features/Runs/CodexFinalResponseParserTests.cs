using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Exercises <see cref="CodexFinalResponseParser.TryParse"/> in isolation — the pure,
/// dependency-free protocol/schema gate a Codex final response must pass before
/// <c>RecordAgentAttemptResultCommandHandler</c> ever considers appending a Proposal.
/// </summary>
public sealed class CodexFinalResponseParserTests
{
    private const string ValidJson =
        """
        {
            "summary": "Add the ledger table and its query.",
            "scope": "Ledger",
            "implementationSteps": "Add the table then the query",
            "risks": "Unbounded content",
            "verificationPlan": "Tests",
            "escalationPoints": "None expected"
        }
        """;

    [Fact]
    public void TryParse_returns_a_validated_proposal_for_a_well_formed_response()
    {
        var proposal = CodexFinalResponseParser.TryParse(ValidJson);

        Assert.NotNull(proposal);
        Assert.Equal("Add the ledger table and its query.", proposal.Summary);

        using var structuredContent = JsonDocument.Parse(proposal.StructuredContentJson);
        var root = structuredContent.RootElement;
        Assert.Equal("Ledger", root.GetProperty("scope").GetString());
        Assert.Equal("Add the table then the query", root.GetProperty("implementationSteps").GetString());
        Assert.Equal("Unbounded content", root.GetProperty("risks").GetString());
        Assert.Equal("Tests", root.GetProperty("verificationPlan").GetString());
        Assert.Equal("None expected", root.GetProperty("escalationPoints").GetString());
        // The top-level summary is never duplicated into the structured content payload.
        Assert.False(root.TryGetProperty("summary", out _));
    }

    [Fact]
    public void TryParse_is_insensitive_to_the_order_fields_appear_in()
    {
        var reordered =
            """
            {
                "escalationPoints": "None expected",
                "verificationPlan": "Tests",
                "risks": "Unbounded content",
                "implementationSteps": "Add the table then the query",
                "scope": "Ledger",
                "summary": "Add the ledger table and its query."
            }
            """;

        var proposal = CodexFinalResponseParser.TryParse(reordered);

        Assert.NotNull(proposal);
        Assert.Equal("Add the ledger table and its query.", proposal.Summary);
    }

    [Fact]
    public void TryParse_returns_null_for_malformed_json()
    {
        Assert.Null(CodexFinalResponseParser.TryParse("{ not json"));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"a plain string\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void TryParse_returns_null_for_a_non_object_root(string json)
    {
        Assert.Null(CodexFinalResponseParser.TryParse(json));
    }

    [Theory]
    [InlineData("summary")]
    [InlineData("scope")]
    [InlineData("implementationSteps")]
    [InlineData("risks")]
    [InlineData("verificationPlan")]
    [InlineData("escalationPoints")]
    public void TryParse_returns_null_when_a_required_field_is_missing(string fieldToRemove)
    {
        using var document = JsonDocument.Parse(ValidJson);
        var remaining = document.RootElement.EnumerateObject()
            .Where(property => property.Name != fieldToRemove)
            .ToDictionary(property => property.Name, property => property.Value.GetString());
        var json = JsonSerializer.Serialize(remaining);

        Assert.Null(CodexFinalResponseParser.TryParse(json));
    }

    [Fact]
    public void TryParse_returns_null_for_an_unknown_extra_field()
    {
        var withExtraField =
            """
            {
                "summary": "Add the ledger table and its query.",
                "scope": "Ledger",
                "implementationSteps": "Add the table then the query",
                "risks": "Unbounded content",
                "verificationPlan": "Tests",
                "escalationPoints": "None expected",
                "unexpectedField": "should never be accepted"
            }
            """;

        Assert.Null(CodexFinalResponseParser.TryParse(withExtraField));
    }

    [Fact]
    public void TryParse_returns_null_for_a_duplicated_field()
    {
        var withDuplicateField =
            """
            {
                "summary": "Add the ledger table and its query.",
                "summary": "A second, conflicting summary.",
                "scope": "Ledger",
                "implementationSteps": "Add the table then the query",
                "risks": "Unbounded content",
                "verificationPlan": "Tests",
                "escalationPoints": "None expected"
            }
            """;

        Assert.Null(CodexFinalResponseParser.TryParse(withDuplicateField));
    }

    [Fact]
    public void TryParse_returns_null_when_a_field_value_is_not_a_string()
    {
        var withNumericField =
            """
            {
                "summary": "Add the ledger table and its query.",
                "scope": "Ledger",
                "implementationSteps": "Add the table then the query",
                "risks": "Unbounded content",
                "verificationPlan": "Tests",
                "escalationPoints": 12345
            }
            """;

        Assert.Null(CodexFinalResponseParser.TryParse(withNumericField));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_returns_null_for_a_blank_field_value(string blankValue)
    {
        var withBlankField = JsonSerializer.Serialize(new
        {
            summary = "Add the ledger table and its query.",
            scope = "Ledger",
            implementationSteps = "Add the table then the query",
            risks = "Unbounded content",
            verificationPlan = "Tests",
            escalationPoints = blankValue,
        });

        Assert.Null(CodexFinalResponseParser.TryParse(withBlankField));
    }

    /// <summary>
    /// Documents actual, current behavior rather than assumed behavior: the parser validates
    /// shape (field set, string type, non-blank) but enforces no length bound at all — unlike
    /// <c>CollaborationMessageContentPolicy</c>, which independently caps every structured-content
    /// field at 900 characters and the summary at 600. A value here that would later be rejected
    /// by that Domain policy is not caught at this layer; see
    /// <c>RecordAgentAttemptResultCommandHandlerTests</c> for what actually happens to it
    /// downstream.
    /// </summary>
    [Fact]
    public void TryParse_fails_closed_for_an_overlong_summary()
    {
        var overlongSummary = new string('x', 601);
        var withOverlongSummary = JsonSerializer.Serialize(new
        {
            summary = overlongSummary,
            scope = "Add a table",
            implementationSteps = "Add the table then the query",
            risks = "None material",
            verificationPlan = "Tests",
            escalationPoints = "None expected",
        });

        Assert.Null(CodexFinalResponseParser.TryParse(withOverlongSummary));
    }

    [Fact]
    public void TryParse_fails_closed_for_an_overlong_structured_field()
    {
        var overlongField = new string('x', 901);
        var withOverlongField = JsonSerializer.Serialize(new
        {
            summary = "A concise summary",
            scope = overlongField,
            implementationSteps = "Add the table then the query",
            risks = "None material",
            verificationPlan = "Tests",
            escalationPoints = "None expected",
        });

        Assert.Null(CodexFinalResponseParser.TryParse(withOverlongField));
    }

    [Theory]
    [InlineData("summary")]
    [InlineData("scope")]
    public void TryParse_fails_closed_for_content_that_looks_unsafe(string fieldName)
    {
        var values = new Dictionary<string, string>
        {
            ["summary"] = "A concise summary",
            ["scope"] = "Add a table",
            ["implementationSteps"] = "Add the table then the query",
            ["risks"] = "None material",
            ["verificationPlan"] = "Tests",
            ["escalationPoints"] = "None expected",
        };
        values[fieldName] = "Uses the API key from the environment";

        var json = JsonSerializer.Serialize(values);

        Assert.Null(CodexFinalResponseParser.TryParse(json));
    }
}
