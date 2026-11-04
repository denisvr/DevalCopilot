using System.Text.Json;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The one transport schema the Claude CriticalReviewer is given (CLI argument and both manifest forms). The Claude tool input
/// rejects a root union, so the schema is a flat closed object whose two optional members are exclusive only by description; the
/// exact union and every content rule stay with <see cref="ClaudeCriticalReviewResponseParser"/>. These tests pin that shape,
/// show which payloads the transport schema alone lets through, and show that the parser still refuses every one of them.
/// All payloads are fabricated.
/// </summary>
public sealed class ClaudeCriticalReviewTransportSchemaTests
{
    private static readonly string[] UnionKeywords = ["oneOf", "anyOf", "allOf"];

    private static JsonDocument Schema() => JsonDocument.Parse(JsonSerializer.Serialize(ClaudeCriticalReviewOutputSchema.BuildSchemaDocument()));

    private static string Challenge(int index) =>
        $$"""
        {"summary":"Gap {{index}}","disputedItem":"Item {{index}}","materialImpact":"Impact {{index}}","reasoning":"Reason {{index}}","alternativeOrQuestion":"Ask {{index}}"}
        """;

    private const string ValidAccept =
        """{"decision":"accept","summary":"The scope matches the objective.","rationale":"The steps cover the stated risks."}""";

    private static string ValidChallenge(int count = 2) =>
        $$"""{"decision":"challenge","summary":"The plan has gaps.","challenges":[{{string.Join(",", Enumerable.Range(1, count).Select(Challenge))}}]}""";

    [Fact]
    public void The_root_is_one_flat_closed_object_with_only_decision_and_summary_required()
    {
        using var document = Schema();
        var root = document.RootElement;

        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["decision", "summary"], root.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).Order().ToArray());
        Assert.Equal(
            ["challenges", "decision", "rationale", "summary"],
            root.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        var decision = root.GetProperty("properties").GetProperty("decision");
        Assert.Equal("string", decision.GetProperty("type").GetString());
        Assert.Equal(["accept", "challenge"], decision.GetProperty("enum").EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.False(decision.TryGetProperty("const", out _));
    }

    [Fact]
    public void No_union_keyword_appears_anywhere_in_the_schema_and_the_root_has_no_composition_at_all()
    {
        using var document = Schema();

        Assert.False(ContainsKeyword(document.RootElement, UnionKeywords));
        foreach (var rootComposition in new[] { "oneOf", "anyOf", "allOf", "not", "if", "then", "else", "$ref", "const" })
        {
            Assert.False(document.RootElement.TryGetProperty(rootComposition, out _), rootComposition);
        }
    }

    private static bool ContainsKeyword(JsonElement element, string[] keywords) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Any(property =>
            keywords.Contains(property.Name, StringComparer.Ordinal) || ContainsKeyword(property.Value, keywords)),
        JsonValueKind.Array => element.EnumerateArray().Any(item => ContainsKeyword(item, keywords)),
        _ => false,
    };

    private static readonly string[] UnsupportedKeywords = ["minLength", "maxLength", "maxItems"];

    [Fact]
    public void The_keywords_the_tool_input_does_not_accept_are_absent_at_every_depth_and_only_min_items_remains()
    {
        using var document = Schema();

        Assert.False(ContainsKeyword(document.RootElement, UnsupportedKeywords));
        foreach (var keyword in UnsupportedKeywords)
        {
            Assert.DoesNotContain($"\"{keyword}\"", document.RootElement.GetRawText(), StringComparison.Ordinal);
        }

        var challenges = document.RootElement.GetProperty("properties").GetProperty("challenges");
        Assert.Equal(ClaudeCriticalReviewOutputSchema.MinimumChallenges, challenges.GetProperty("minItems").GetInt32());
        Assert.Equal(1, ClaudeCriticalReviewOutputSchema.MinimumChallenges);
    }

    [Fact]
    public void The_bounds_are_the_current_content_policy_and_are_stated_in_the_descriptions()
    {
        using var document = Schema();
        var properties = document.RootElement.GetProperty("properties");

        var summary = properties.GetProperty("summary");
        var rationale = properties.GetProperty("rationale");
        var challenges = properties.GetProperty("challenges");
        var item = challenges.GetProperty("items");
        Assert.False(item.GetProperty("additionalProperties").GetBoolean());
        string[] itemFields = ["summary", "disputedItem", "materialImpact", "reasoning", "alternativeOrQuestion"];
        Assert.Equal(itemFields, item.GetProperty("required").EnumerateArray().Select(field => field.GetString()!).ToArray());
        Assert.Equal(itemFields, item.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());
        foreach (var field in itemFields)
        {
            var bound = field == "summary" ? CollaborationMessageContentPolicy.MaximumSummaryLength : 900;
            Assert.Contains($"1 to {bound} characters", item.GetProperty("properties").GetProperty(field).GetProperty("description").GetString());
        }

        Assert.Equal(600, CollaborationMessageContentPolicy.MaximumSummaryLength);
        Assert.Contains($"1 to {CollaborationMessageContentPolicy.MaximumSummaryLength} characters", summary.GetProperty("description").GetString());
        Assert.Contains("1 to 900 characters", rationale.GetProperty("description").GetString());
        Assert.Equal(5, ClaudeCriticalReviewOutputSchema.MaximumChallenges);
        Assert.Contains("1 to 5 challenges", challenges.GetProperty("description").GetString());
    }

    [Fact]
    public void The_exclusivity_of_rationale_and_challenges_is_described_at_the_root_and_on_both_members()
    {
        using var document = Schema();
        var root = document.RootElement;
        var properties = root.GetProperty("properties");

        var rootText = root.GetProperty("description").GetString()!;
        Assert.Contains("decision accept, include rationale and omit challenges", rootText);
        Assert.Contains("decision challenge, include challenges and omit rationale", rootText);
        Assert.Contains("Required when decision is accept", properties.GetProperty("rationale").GetProperty("description").GetString());
        Assert.Contains("omitted when decision is challenge", properties.GetProperty("rationale").GetProperty("description").GetString());
        Assert.Contains("Required when decision is challenge", properties.GetProperty("challenges").GetProperty("description").GetString());
        Assert.Contains("omitted when decision is accept", properties.GetProperty("challenges").GetProperty("description").GetString());
        Assert.DoesNotContain("rationale", root.GetProperty("required").GetRawText());
        Assert.DoesNotContain("challenges", root.GetProperty("required").GetRawText());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Both_valid_forms_satisfy_the_transport_schema_and_the_parser_accepts_them(int challengeCount)
    {
        using var document = Schema();

        foreach (var json in new[] { ValidAccept, ValidChallenge(challengeCount) })
        {
            using var response = JsonDocument.Parse(json);
            Assert.True(Conforms(document.RootElement, response.RootElement), json);
            Assert.NotNull(ClaudeCriticalReviewResponseParser.TryParse(json));
        }

        Assert.True(ClaudeCriticalReviewResponseParser.TryParse(ValidAccept)!.IsAcceptance);
        Assert.False(ClaudeCriticalReviewResponseParser.TryParse(ValidChallenge(challengeCount))!.IsAcceptance);
    }

    public static TheoryData<string, string> MixedOrIncomplete => new()
    {
        { "accept carrying challenges", """{"decision":"accept","summary":"S","rationale":"R","challenges":[{"summary":"a","disputedItem":"b","materialImpact":"c","reasoning":"d","alternativeOrQuestion":"e"}]}""" },
        { "challenge carrying a rationale", """{"decision":"challenge","summary":"S","rationale":"R","challenges":[{"summary":"a","disputedItem":"b","materialImpact":"c","reasoning":"d","alternativeOrQuestion":"e"}]}""" },
        { "accept without a rationale", """{"decision":"accept","summary":"S"}""" },
        { "challenge without challenges", """{"decision":"challenge","summary":"S"}""" },
        { "challenge with an empty set", """{"decision":"challenge","summary":"S","challenges":[]}""" },
        { "accept carrying challenges and no rationale","""{"decision":"accept","summary":"S","challenges":[{"summary":"a","disputedItem":"b","materialImpact":"c","reasoning":"d","alternativeOrQuestion":"e"}]}""" },
    };

    [Theory]
    [MemberData(nameof(MixedOrIncomplete))]
    public void A_mixed_or_incomplete_payload_is_refused_by_the_parser_whatever_the_transport_schema_lets_through(string description, string json)
    {
        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
        Assert.False(string.IsNullOrEmpty(description));
    }

    [Fact]
    public void The_flat_schema_does_not_itself_reject_an_exclusivity_violation_so_the_parser_is_the_only_gate()
    {
        using var document = Schema();
        using var mixed = JsonDocument.Parse(
            """{"decision":"accept","summary":"S","rationale":"R","challenges":[{"summary":"a","disputedItem":"b","materialImpact":"c","reasoning":"d","alternativeOrQuestion":"e"}]}""");
        using var incomplete = JsonDocument.Parse("""{"decision":"challenge","summary":"S"}""");

        Assert.True(Conforms(document.RootElement, mixed.RootElement));
        Assert.True(Conforms(document.RootElement, incomplete.RootElement));
        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(mixed.RootElement.GetRawText()));
        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(incomplete.RootElement.GetRawText()));
    }

    [Theory]
    [InlineData("""{"summary":"S","rationale":"R"}""")]
    [InlineData("""{"decision":"accept","rationale":"R"}""")]
    [InlineData("""{"decision":"approve","summary":"S","rationale":"R"}""")]
    [InlineData("""{"decision":"accept","summary":"S","rationale":"R","extra":"x"}""")]
    [InlineData("""{"decision":"accept","summary":"S","rationale":7}""")]
    public void A_payload_outside_the_transport_schema_is_also_refused_by_the_parser(string json)
    {
        using var document = Schema();
        using var response = JsonDocument.Parse(json);

        Assert.False(Conforms(document.RootElement, response.RootElement));
        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
    }

    [Theory]
    [InlineData("summary", "The password is rotated.")]
    [InlineData("rationale", "Read the secret from the host.")]
    [InlineData("summary", "Contains the C:\\ drive marker.")]
    public void An_unsafe_accept_is_refused_even_though_the_transport_schema_accepts_its_shape(string field, string text)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["decision"] = "accept",
            ["summary"] = field == "summary" ? text : "Safe summary.",
            ["rationale"] = field == "rationale" ? text : "Safe rationale.",
        });
        using var document = Schema();
        using var response = JsonDocument.Parse(json);

        Assert.True(Conforms(document.RootElement, response.RootElement));
        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
    }

    [Fact]
    public void An_unsafe_challenge_item_is_refused_even_though_the_transport_schema_accepts_its_shape()
    {
        var json = JsonSerializer.Serialize(new
        {
            decision = "challenge",
            summary = "Gaps.",
            challenges = new[]
            {
                new { summary = "Gap", disputedItem = "The api_key handling", materialImpact = "I", reasoning = "R", alternativeOrQuestion = "A" },
            },
        });
        using var document = Schema();
        using var response = JsonDocument.Parse(json);

        Assert.True(Conforms(document.RootElement, response.RootElement));
        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
    }

    [Theory]
    [InlineData("summary", "")]
    [InlineData("summary", "   ")]
    [InlineData("rationale", "")]
    [InlineData("rationale", "   ")]
    [InlineData("summary", "OVERLONG-601")]
    [InlineData("rationale", "OVERLONG-901")]
    public void An_empty_or_overlong_accept_text_is_refused_by_the_parser_even_though_the_schema_states_no_length(string field, string text)
    {
        var value = text switch
        {
            "OVERLONG-601" => new string('s', 601),
            "OVERLONG-901" => new string('r', 901),
            _ => text,
        };
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["decision"] = "accept",
            ["summary"] = field == "summary" ? value : "Safe summary.",
            ["rationale"] = field == "rationale" ? value : "Safe rationale.",
        });
        using var document = Schema();
        using var response = JsonDocument.Parse(json);

        Assert.True(Conforms(document.RootElement, response.RootElement));
        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
    }

    [Theory]
    [InlineData("summary", "")]
    [InlineData("disputedItem", "  ")]
    [InlineData("materialImpact", "")]
    [InlineData("reasoning", "  ")]
    [InlineData("alternativeOrQuestion", "")]
    [InlineData("summary", "OVERLONG-601")]
    [InlineData("disputedItem", "OVERLONG-901")]
    [InlineData("materialImpact", "OVERLONG-901")]
    [InlineData("reasoning", "OVERLONG-901")]
    [InlineData("alternativeOrQuestion", "OVERLONG-901")]
    public void An_empty_or_overlong_challenge_text_is_refused_by_the_parser_even_though_the_schema_states_no_length(string field, string text)
    {
        var value = text switch
        {
            "OVERLONG-601" => new string('s', 601),
            "OVERLONG-901" => new string('r', 901),
            _ => text,
        };
        var item = new Dictionary<string, string>
        {
            ["summary"] = "Gap",
            ["disputedItem"] = "Item",
            ["materialImpact"] = "Impact",
            ["reasoning"] = "Reason",
            ["alternativeOrQuestion"] = "Ask",
        };
        item[field] = value;
        var json = JsonSerializer.Serialize(new { decision = "challenge", summary = "Gaps.", challenges = new[] { item } });
        using var document = Schema();
        using var response = JsonDocument.Parse(json);

        Assert.True(Conforms(document.RootElement, response.RootElement));
        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
    }

    [Fact]
    public void An_overlong_challenge_set_summary_is_refused_by_the_parser_even_though_the_schema_states_no_length()
    {
        var json = JsonSerializer.Serialize(new
        {
            decision = "challenge",
            summary = new string('s', 601),
            challenges = new[] { new { summary = "Gap", disputedItem = "I", materialImpact = "M", reasoning = "R", alternativeOrQuestion = "A" } },
        });
        using var document = Schema();
        using var response = JsonDocument.Parse(json);

        Assert.True(Conforms(document.RootElement, response.RootElement));
        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(20)]
    public void More_than_five_challenges_are_refused_by_the_parser_even_though_the_schema_states_no_maximum(int count)
    {
        var json = ValidChallenge(count);
        using var document = Schema();
        using var response = JsonDocument.Parse(json);

        Assert.True(Conforms(document.RootElement, response.RootElement));
        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
    }

    [Fact]
    public void An_empty_challenge_set_is_outside_the_schema_by_min_items_and_refused_by_the_parser()
    {
        const string json = """{"decision":"challenge","summary":"S","challenges":[]}""";
        using var document = Schema();
        using var response = JsonDocument.Parse(json);

        Assert.False(Conforms(document.RootElement, response.RootElement));
        Assert.Null(ClaudeCriticalReviewResponseParser.TryParse(json));
    }

    /// <summary>A minimal evaluator of exactly the keywords this schema uses (no length or maximum keyword exists), so a payload can be checked against the real document.</summary>
    private static bool Conforms(JsonElement schema, JsonElement value)
    {
        switch (schema.GetProperty("type").GetString())
        {
            case "object":
                if (value.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                var properties = schema.GetProperty("properties");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!properties.TryGetProperty(property.Name, out var child) || !seen.Add(property.Name) || !Conforms(child, property.Value))
                    {
                        return false;
                    }
                }

                return schema.GetProperty("required").EnumerateArray().All(required => seen.Contains(required.GetString()!));
            case "array":
                if (value.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                return value.GetArrayLength() >= schema.GetProperty("minItems").GetInt32()
                    && value.EnumerateArray().All(item => Conforms(schema.GetProperty("items"), item));
            case "string":
                if (value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                var text = value.GetString()!;
                return !schema.TryGetProperty("enum", out var allowed) || allowed.EnumerateArray().Any(item => item.GetString() == text);
            default:
                return false;
        }
    }
}
