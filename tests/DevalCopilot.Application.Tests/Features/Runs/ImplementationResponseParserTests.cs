using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class ImplementationResponseParserTests
{
    private static string ValidJson(object? overrides = null)
    {
        var document = new Dictionary<string, object>
        {
            ["summary"] = "Implemented the ledger table and its query.",
            ["changedRelativePaths"] = new[] { "src/backend/Foo.cs", "tests/Foo.Tests.cs" },
            ["implementationNotes"] = "Added the migration and the query handler.",
            ["unexpectedDiscoveries"] = "",
            ["remainingRisks"] = "",
            ["recommendedVerification"] = "Run the backend test suite.",
        };

        if (overrides is not null)
        {
            foreach (var property in overrides.GetType().GetProperties())
            {
                document[property.Name] = property.GetValue(overrides)!;
            }
        }

        return JsonSerializer.Serialize(document);
    }

    [Fact]
    public void TryParse_accepts_a_well_formed_report()
    {
        var report = ImplementationResponseParser.TryParse(ValidJson());

        Assert.NotNull(report);
        Assert.Equal(2, report!.ChangedRelativePaths.Count);
        Assert.Contains("src/backend/Foo.cs", report.ChangedRelativePaths);
    }

    [Fact]
    public void TryParse_rejects_malformed_json()
    {
        Assert.Null(ImplementationResponseParser.TryParse("{ not json"));
    }

    [Fact]
    public void TryParse_rejects_an_unknown_top_level_field()
    {
        var json = ValidJson(new { extraField = "x" });
        Assert.Null(ImplementationResponseParser.TryParse(json));
    }

    [Theory]
    [InlineData(@"C:\repos\foo.cs")]
    [InlineData("/etc/passwd")]
    [InlineData("../outside.cs")]
    [InlineData("a/../../b.cs")]
    public void TryParse_rejects_a_changed_path_that_is_not_safely_repository_relative(string unsafePath)
    {
        var json = ValidJson(new { changedRelativePaths = new[] { unsafePath } });
        Assert.Null(ImplementationResponseParser.TryParse(json));
    }

    [Fact]
    public void TryParse_rejects_duplicate_changed_paths()
    {
        var json = ValidJson(new { changedRelativePaths = new[] { "src/Foo.cs", "src/Foo.cs" } });
        Assert.Null(ImplementationResponseParser.TryParse(json));
    }

    [Fact]
    public void TryParse_accepts_an_empty_changed_paths_list()
    {
        var json = ValidJson(new { changedRelativePaths = Array.Empty<string>() });
        var report = ImplementationResponseParser.TryParse(json);

        Assert.NotNull(report);
        Assert.Empty(report!.ChangedRelativePaths);
    }

    [Fact]
    public void TryParse_rejects_a_blank_implementation_notes_field()
    {
        var json = ValidJson(new { implementationNotes = "   " });
        Assert.Null(ImplementationResponseParser.TryParse(json));
    }

    [Fact]
    public void TryParse_accepts_a_blank_unexpected_discoveries_and_remaining_risks_field()
    {
        var json = ValidJson(new { unexpectedDiscoveries = "", remainingRisks = "" });
        Assert.NotNull(ImplementationResponseParser.TryParse(json));
    }
}
