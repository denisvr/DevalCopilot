using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

/// <summary>
/// The fixture's own, fixed copy of the response schema of every contract it serves (embedded files under <c>Schemas</c>), and the
/// check that the schema an adapter supplied is bounded JSON that agrees with the copy of the selected role's contract. It is
/// deliberately not derived from the production schema builders: a changed or weakened adapter schema must fail the journey instead
/// of being served. Comparison is structural: object property order is irrelevant, and so is the order of string arrays
/// (<c>required</c>, <c>enum</c>, a <c>type</c> list). Nothing of the supplied schema is ever echoed into a refusal.
/// </summary>
public static class ResponseSchemas
{
    public const int MaximumBytes = 16_384;
    private const int MaximumDepth = 16;

    private static readonly IReadOnlyDictionary<string, string[]> ServedContracts = new Dictionary<string, string[]>
    {
        ["codex"] = ["Proposal", "ChallengeResolution", "VerificationDiagnosis", "ImplementationReview"],
        ["claude"] = ["CriticalReview", "ImplementationReport", "ReviewCorrection"],
    };

    /// <summary>Reads the schema file the real Codex invoker supplies (its invocation-owned scratch file), bounded and read-only.</summary>
    public static string ReadBoundedFile(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            throw Refuse("The output schema file is missing.");
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[MaximumBytes + 1];
            var length = 0;
            int read;
            while (length < buffer.Length && (read = stream.Read(buffer, length, buffer.Length - length)) > 0)
            {
                length += read;
            }

            return length > MaximumBytes ? throw Refuse("The output schema is larger than the accepted bound.") : new UTF8Encoding(false).GetString(buffer, 0, length);
        }
        catch (IOException)
        {
            throw Refuse("The output schema file could not be read.");
        }
        catch (UnauthorizedAccessException)
        {
            throw Refuse("The output schema file could not be read.");
        }
    }

    /// <summary>Refuses unless <paramref name="supplied"/> is bounded JSON equal in structure to the fixed schema of the contract.</summary>
    public static void RequireAgreement(string role, string contract, string supplied)
    {
        if (!ServedContracts.TryGetValue(role, out var served) || !served.Contains(contract, StringComparer.Ordinal))
        {
            throw Refuse("The manifest contract is not served by this double.");
        }

        if (Encoding.UTF8.GetByteCount(supplied) > MaximumBytes)
        {
            throw Refuse("The output schema is larger than the accepted bound.");
        }

        using var expected = JsonDocument.Parse(LoadExpected(contract));
        JsonDocument actual;
        try
        {
            actual = JsonDocument.Parse(supplied, new JsonDocumentOptions { MaxDepth = MaximumDepth });
        }
        catch (JsonException)
        {
            throw Refuse("The output schema is not valid JSON.");
        }

        using (actual)
        {
            if (actual.RootElement.ValueKind != JsonValueKind.Object || !Agrees(expected.RootElement, actual.RootElement))
            {
                throw Refuse("The output schema does not match the fixed schema of the selected contract.");
            }
        }
    }

    private static string LoadExpected(string contract)
    {
        using var stream = typeof(ResponseSchemas).Assembly.GetManifestResourceStream($"schemas/{contract}.schema.json")
            ?? throw Refuse("The fixed schema of the contract is not available.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    private static bool Agrees(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            return false;
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                var count = 0;
                foreach (var property in actual.EnumerateObject())
                {
                    count++;
                    if (!names.Add(property.Name)
                        || !expected.TryGetProperty(property.Name, out var counterpart)
                        || !Agrees(counterpart, property.Value))
                    {
                        return false;
                    }
                }

                return count == expected.EnumerateObject().Count();
            case JsonValueKind.Array:
                var expectedItems = expected.EnumerateArray().ToArray();
                var actualItems = actual.EnumerateArray().ToArray();
                if (expectedItems.Length != actualItems.Length)
                {
                    return false;
                }

                if (expectedItems.All(item => item.ValueKind == JsonValueKind.String))
                {
                    return actualItems.All(item => item.ValueKind == JsonValueKind.String)
                        && expectedItems.Select(item => item.GetString()).Order(StringComparer.Ordinal)
                            .SequenceEqual(actualItems.Select(item => item.GetString()).Order(StringComparer.Ordinal));
                }

                return expectedItems.Zip(actualItems).All(pair => Agrees(pair.First, pair.Second));
            case JsonValueKind.String:
                return string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal);
            case JsonValueKind.Number:
                return decimal.TryParse(expected.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var wanted)
                    && decimal.TryParse(actual.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var supplied)
                    && wanted == supplied;
            default:
                return true;
        }
    }

    private static FixtureRefusal Refuse(string reason) => new(FixtureRefusal.UnsupportedInvocation, reason);
}
