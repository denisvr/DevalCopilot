using System.Text.Json;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.RecordVerificationDiagnosisResult;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Exercises <see cref="VerificationDiagnosisResponseParser.TryParse"/> in isolation: the pure protocol/schema gate a Codex
/// verification-diagnosis final response must pass before anything is recorded (ADR-0018). Exactly one of two shapes is
/// valid and there is no approval shape at all.
/// </summary>
public sealed class VerificationDiagnosisResponseParserTests
{
    private static Dictionary<string, object?> Finding(int index = 0, string? path = "src/Foo.cs") => new()
    {
        ["severity"] = "high",
        ["category"] = "correctness",
        ["summary"] = $"Finding summary {index}",
        ["evidence"] = $"Evidence text {index}",
        ["requiredChange"] = $"Required change text {index}",
        ["affectedRelativePath"] = path,
    };

    private static Dictionary<string, object?> Escalation(string? decision = "Decide the tool version") => new()
    {
        ["unresolvedDecision"] = decision,
        ["options"] = "Keep it or change it",
        ["consequences"] = "The check keeps failing",
        ["evidence"] = "The same failure repeats",
        ["recommendedChoice"] = "Change it",
    };

    private static string Findings(int count = 1) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["outcome"] = "findings",
        ["summary"] = "Overall diagnosis summary",
        ["findings"] = Enumerable.Range(0, count).Select(index => Finding(index, $"src/Foo{index}.cs")).ToArray(),
        ["escalation"] = null,
    });

    private static string Escalated() => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["outcome"] = "escalation",
        ["summary"] = "The failure needs a human decision",
        ["findings"] = Array.Empty<object>(),
        ["escalation"] = Escalation(),
    });

    private static string WithFinding(Action<Dictionary<string, object?>> change)
    {
        var finding = Finding();
        change(finding);
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["outcome"] = "findings",
            ["summary"] = "Overall diagnosis summary",
            ["findings"] = new[] { finding },
            ["escalation"] = null,
        });
    }

    private static string WithEscalation(Action<Dictionary<string, object?>> change)
    {
        var escalation = Escalation();
        change(escalation);
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["outcome"] = "escalation",
            ["summary"] = "The failure needs a human decision",
            ["findings"] = Array.Empty<object>(),
            ["escalation"] = escalation,
        });
    }

    [Fact]
    public void A_single_finding_response_is_a_validated_findings_diagnosis()
    {
        var diagnosis = VerificationDiagnosisResponseParser.TryParse(Findings());

        Assert.NotNull(diagnosis);
        Assert.False(diagnosis.IsEscalation);
        Assert.Null(diagnosis.Escalation);
        Assert.Equal("Overall diagnosis summary", diagnosis.Summary);
        var finding = Assert.Single(diagnosis.Findings);
        Assert.Equal("high", finding.Severity);
        Assert.Equal("correctness", finding.Category);
        Assert.Equal("Finding summary 0", finding.Summary);
        Assert.Equal("Evidence text 0", finding.Evidence);
        Assert.Equal("Required change text 0", finding.RequiredChange);
        Assert.Equal("src/Foo0.cs", finding.AffectedRelativePath);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public void One_to_ten_findings_are_kept_in_provider_order(int count)
    {
        var diagnosis = VerificationDiagnosisResponseParser.TryParse(Findings(count));

        Assert.NotNull(diagnosis);
        Assert.Equal(Enumerable.Range(0, count).Select(index => $"Finding summary {index}"), diagnosis.Findings.Select(f => f.Summary));
    }

    [Fact]
    public void Zero_or_eleven_findings_are_rejected()
    {
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(Findings(0)));
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(Findings(11)));
    }

    [Fact]
    public void A_null_affected_path_is_accepted()
    {
        var diagnosis = VerificationDiagnosisResponseParser.TryParse(WithFinding(finding => finding["affectedRelativePath"] = null));

        Assert.NotNull(diagnosis);
        Assert.Null(diagnosis.Findings[0].AffectedRelativePath);
    }

    [Fact]
    public void A_well_formed_escalation_response_is_a_validated_escalation_with_no_findings()
    {
        var diagnosis = VerificationDiagnosisResponseParser.TryParse(Escalated());

        Assert.NotNull(diagnosis);
        Assert.True(diagnosis.IsEscalation);
        Assert.Empty(diagnosis.Findings);
        Assert.Equal("The failure needs a human decision", diagnosis.Summary);
        Assert.Equal(
            new ValidatedDiagnosisEscalation(
                "Decide the tool version", "Keep it or change it", "The check keeps failing", "The same failure repeats", "Change it"),
            diagnosis.Escalation);
    }

    // ---- Mixed, approval-like, and malformed shapes -----------------------------------------------------------------

    [Fact]
    public void A_findings_outcome_that_also_carries_an_escalation_is_rejected()
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["outcome"] = "findings",
            ["summary"] = "s",
            ["findings"] = new[] { Finding() },
            ["escalation"] = Escalation(),
        });

        Assert.Null(VerificationDiagnosisResponseParser.TryParse(json));
    }

    [Fact]
    public void An_escalation_outcome_that_also_carries_findings_is_rejected()
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["outcome"] = "escalation",
            ["summary"] = "s",
            ["findings"] = new[] { Finding() },
            ["escalation"] = Escalation(),
        });

        Assert.Null(VerificationDiagnosisResponseParser.TryParse(json));
    }

    [Fact]
    public void A_findings_outcome_without_findings_and_an_escalation_outcome_without_an_escalation_are_rejected()
    {
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["outcome"] = "findings",
            ["summary"] = "s",
            ["findings"] = Array.Empty<object>(),
            ["escalation"] = null,
        })));
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["outcome"] = "escalation",
            ["summary"] = "s",
            ["findings"] = Array.Empty<object>(),
            ["escalation"] = null,
        })));
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("changesRequested")]
    [InlineData("approval")]
    [InlineData("Findings")]
    [InlineData("")]
    public void An_unrecognized_or_approval_like_outcome_is_rejected(string outcome)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["outcome"] = outcome,
            ["summary"] = "s",
            ["findings"] = new[] { Finding() },
            ["escalation"] = null,
        });

        Assert.Null(VerificationDiagnosisResponseParser.TryParse(json));
    }

    [Fact]
    public void An_ordinary_review_approval_shape_with_rationale_and_residual_risks_is_rejected()
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["outcome"] = "approved",
            ["summary"] = "Looks good",
            ["rationale"] = "Fine",
            ["residualRisks"] = "None",
            ["findings"] = Array.Empty<object>(),
        });

        Assert.Null(VerificationDiagnosisResponseParser.TryParse(json));
    }

    [Fact]
    public void An_unknown_extra_top_level_field_or_a_missing_field_is_rejected()
    {
        var withExtra = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["outcome"] = "findings",
            ["summary"] = "s",
            ["findings"] = new[] { Finding() },
            ["escalation"] = null,
            ["approved"] = true,
        });
        var missingEscalation = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["outcome"] = "findings",
            ["summary"] = "s",
            ["findings"] = new[] { Finding() },
        });
        var missingSummary = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["outcome"] = "findings",
            ["findings"] = new[] { Finding() },
            ["escalation"] = null,
        });

        Assert.Null(VerificationDiagnosisResponseParser.TryParse(withExtra));
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(missingEscalation));
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(missingSummary));
    }

    [Fact]
    public void A_duplicate_top_level_field_is_rejected()
    {
        const string json =
            "{\"outcome\":\"findings\",\"summary\":\"s\",\"summary\":\"t\",\"findings\":[],\"escalation\":null}";

        Assert.Null(VerificationDiagnosisResponseParser.TryParse(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"findings\"")]
    [InlineData("{\"outcome\":\"findings\"")]
    public void Malformed_json_or_a_non_object_root_is_rejected(string json)
    {
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(json));
    }

    [Fact]
    public void Wrongly_typed_top_level_values_are_rejected()
    {
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(
            "{\"outcome\":1,\"summary\":\"s\",\"findings\":[],\"escalation\":null}"));
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(
            "{\"outcome\":\"findings\",\"summary\":7,\"findings\":[],\"escalation\":null}"));
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(
            "{\"outcome\":\"findings\",\"summary\":\"s\",\"findings\":{},\"escalation\":null}"));
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(
            "{\"outcome\":\"findings\",\"summary\":\"s\",\"findings\":[],\"escalation\":\"x\"}"));
    }

    [Fact]
    public void A_blank_summary_is_rejected()
    {
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["outcome"] = "findings",
                ["summary"] = "   ",
                ["findings"] = new[] { Finding() },
                ["escalation"] = null,
            })));
    }

    // ---- Findings: closed values and safe content -----------------------------------------------------------------------

    [Fact]
    public void An_unknown_or_missing_finding_field_is_rejected()
    {
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(WithFinding(finding => finding["extra"] = "x")));
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(WithFinding(finding => finding.Remove("requiredChange"))));
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(WithFinding(finding => finding.Remove("affectedRelativePath"))));
    }

    [Theory]
    [InlineData("severity", "urgent")]
    [InlineData("severity", "")]
    [InlineData("category", "style")]
    [InlineData("category", "")]
    public void An_unrecognized_severity_or_category_is_rejected(string field, string value)
    {
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(WithFinding(finding => finding[field] = value)));
    }

    [Theory]
    [InlineData("summary")]
    [InlineData("evidence")]
    [InlineData("requiredChange")]
    public void A_blank_or_non_string_finding_text_is_rejected(string field)
    {
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(WithFinding(finding => finding[field] = "  ")));
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(WithFinding(finding => finding[field] = 5)));
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(WithFinding(finding => finding[field] = null)));
    }

    [Theory]
    [InlineData("C:/repo/src/Foo.cs")]
    [InlineData("C:\\repo\\Foo.cs")]
    [InlineData("/etc/passwd")]
    [InlineData("../outside.cs")]
    [InlineData("src/../../outside.cs")]
    [InlineData("src//Foo.cs")]
    [InlineData("./Foo.cs")]
    [InlineData("src\\Foo.cs")]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unsafe_absolute_traversal_or_empty_affected_path_is_rejected(string path)
    {
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(WithFinding(finding => finding["affectedRelativePath"] = path)));
    }

    [Fact]
    public void An_overlong_affected_path_is_rejected_and_the_maximum_is_accepted()
    {
        var accepted = string.Join('/', Enumerable.Repeat(new string('a', 50), 9)) + "/f";
        Assert.True(accepted.Length <= 512);
        Assert.NotNull(VerificationDiagnosisResponseParser.TryParse(WithFinding(finding => finding["affectedRelativePath"] = accepted)));
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(WithFinding(finding => finding["affectedRelativePath"] = new string('a', 513))));
    }

    [Theory]
    [InlineData("summary")]
    [InlineData("evidence")]
    [InlineData("requiredChange")]
    public void Forbidden_content_words_in_a_finding_field_are_rejected(string field)
    {
        foreach (var word in new[] { "environment", "credential", "Password", "secret", "api key", "api_key", "bearer abc", "C:\\x" })
        {
            Assert.Null(VerificationDiagnosisResponseParser.TryParse(
                WithFinding(finding => finding[field] = $"The failure involves the {word} here")));
        }
    }

    [Fact]
    public void Overlong_finding_text_is_rejected_at_the_content_policy_bounds()
    {
        Assert.NotNull(VerificationDiagnosisResponseParser.TryParse(WithFinding(finding => finding["evidence"] = new string('e', 900))));
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(WithFinding(finding => finding["evidence"] = new string('e', 901))));
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(WithFinding(finding => finding["summary"] = new string('s', 601))));
    }

    [Fact]
    public void A_duplicate_finding_is_rejected_rather_than_collapsed()
    {
        var finding = Finding(0, "src/A.cs");
        var other = Finding(0, "src/B.cs");
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["outcome"] = "findings",
            ["summary"] = "s",
            ["findings"] = new[] { finding, other },
            ["escalation"] = null,
        });

        Assert.Null(VerificationDiagnosisResponseParser.TryParse(json));
    }

    [Fact]
    public void A_finding_object_that_is_not_an_object_is_rejected()
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["outcome"] = "findings",
            ["summary"] = "s",
            ["findings"] = new object[] { "text" },
            ["escalation"] = null,
        });

        Assert.Null(VerificationDiagnosisResponseParser.TryParse(json));
    }

    // ---- Escalation: closed fields and safe content ---------------------------------------------------------------------

    [Fact]
    public void An_escalation_with_an_unknown_or_missing_field_is_rejected()
    {
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(WithEscalation(escalation => escalation["extra"] = "x")));
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(WithEscalation(escalation => escalation.Remove("recommendedChoice"))));
    }

    [Theory]
    [InlineData("unresolvedDecision")]
    [InlineData("options")]
    [InlineData("consequences")]
    [InlineData("evidence")]
    [InlineData("recommendedChoice")]
    public void A_blank_or_non_string_escalation_field_is_rejected(string field)
    {
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(WithEscalation(escalation => escalation[field] = " ")));
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(WithEscalation(escalation => escalation[field] = 1)));
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(WithEscalation(escalation => escalation[field] = null)));
    }

    [Theory]
    [InlineData("unresolvedDecision")]
    [InlineData("options")]
    [InlineData("consequences")]
    [InlineData("evidence")]
    [InlineData("recommendedChoice")]
    public void Forbidden_content_words_in_an_escalation_field_are_rejected(string field)
    {
        foreach (var word in new[] { "environment", "credential", "password", "secret", "api key", "C:\\tools" })
        {
            Assert.Null(VerificationDiagnosisResponseParser.TryParse(
                WithEscalation(escalation => escalation[field] = $"We must decide about the {word} now")));
        }
    }

    [Fact]
    public void Forbidden_content_words_in_the_top_level_summary_are_rejected()
    {
        Assert.Null(VerificationDiagnosisResponseParser.TryParse(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["outcome"] = "findings",
            ["summary"] = "The environment is wrong",
            ["findings"] = new[] { Finding() },
            ["escalation"] = null,
        })));
    }

    [Fact]
    public void The_recorded_structured_content_of_a_finding_and_an_escalation_is_exactly_the_policy_fields()
    {
        var diagnosis = VerificationDiagnosisResponseParser.TryParse(Findings())!;
        var finding = VerificationDiagnosisResponseParser.SerializeFinding(diagnosis.Findings[0]);
        var escalation = VerificationDiagnosisResponseParser.SerializeEscalation(
            VerificationDiagnosisResponseParser.TryParse(Escalated())!.Escalation!);

        Assert.Equal(
            ["severity", "category", "evidence", "requiredChange"],
            JsonDocument.Parse(finding).RootElement.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("src/Foo", finding, StringComparison.Ordinal);
        Assert.Equal(
            ["unresolvedDecision", "options", "consequences", "evidence", "recommendedChoice"],
            JsonDocument.Parse(escalation).RootElement.EnumerateObject().Select(p => p.Name));
    }
}
