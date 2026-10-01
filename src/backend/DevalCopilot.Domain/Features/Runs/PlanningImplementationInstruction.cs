using System.Text.Json;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The fixed host instruction and the one required, bounded human rationale of the explicit authorization
/// of one implementation claim for an escalated final plan (ADR-0016). They travel in the existing
/// <c>HumanInstruction</c> structured content (<c>instruction</c> and <c>rationale</c>, the protocol shape
/// that already exists); the instruction is fixed host text and never accepted from a caller. The rationale
/// shares the compatible normalized-text policy of the other human guidance (<see cref="BoundedGuidanceText"/>),
/// at most 600 UTF-16 code units, and is advisory: it cannot widen the plan or any permission. The lexical
/// safety check it inherits is a best-effort filter, not a guarantee that a secret is absent.
/// </summary>
public static class PlanningImplementationInstruction
{
    public const int MaximumLength = BoundedGuidanceText.MaximumLength;

    public const string FixedInstruction = "Authorize one implementation claim for the final escalated plan.";

    /// <summary>The fixed summary of the recorded HumanInstruction message.</summary>
    public const string Summary = "Human authorized one implementation claim for the final escalated plan.";

    /// <summary>Normalizes the rationale (Unicode form C, LF line endings, trimmed). Returns
    /// <see langword="null"/> for blank, overlong, invalid or unsafe text. Never throws and never echoes the input.</summary>
    public static string? Normalize(string? raw) => BoundedGuidanceText.Normalize(raw);

    public static string BuildStructuredContentJson(string rationale) =>
        JsonSerializer.Serialize(new { instruction = FixedInstruction, rationale });

    /// <summary>Reads the rationale of a persisted HumanInstruction, or <see langword="null"/> unless the content is
    /// exactly the canonical form <see cref="BuildStructuredContentJson"/> writes for a rationale that is its own
    /// normalized form. Anything else is refused, so a corrupted value never reaches a manifest or a success.</summary>
    public static string? TryReadRationale(string structuredContentJson)
    {
        try
        {
            using var document = JsonDocument.Parse(structuredContentJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2)
            {
                return null;
            }

            if (!root.TryGetProperty("instruction", out var instruction)
                || instruction.ValueKind != JsonValueKind.String
                || !string.Equals(instruction.GetString(), FixedInstruction, StringComparison.Ordinal)
                || !root.TryGetProperty("rationale", out var rationale)
                || rationale.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = rationale.GetString();
            if (string.IsNullOrWhiteSpace(value)
                || !string.Equals(Normalize(value), value, StringComparison.Ordinal)
                || !string.Equals(BuildStructuredContentJson(value), structuredContentJson, StringComparison.Ordinal))
            {
                return null;
            }

            return value;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
