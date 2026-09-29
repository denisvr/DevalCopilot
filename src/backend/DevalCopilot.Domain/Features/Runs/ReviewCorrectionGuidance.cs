using System.Text;
using System.Text.Json;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The bounded, optional human guidance that may accompany the one explicit authorization of an
/// additional review correction. It is carried in the existing <c>HumanInstruction</c> structured
/// content: the fixed <c>instruction</c> field states the authorization and the <c>rationale</c>
/// field holds either <see cref="DefaultRationale"/> (a bodyless authorization) or the exact
/// accepted guidance. Guidance is advisory context for the Implementer, never an instruction that
/// can change the objective, findings, role, workspace, permissions, or budgets. The lexical safety
/// check it inherits from <see cref="CollaborationMessageContentPolicy"/> is a best-effort filter
/// only; it does not guarantee that a secret or sensitive value is absent.
/// </summary>
public static class ReviewCorrectionGuidance
{
    public const int MaximumLength = 600;

    public const string FixedInstruction = "Authorize one additional review-correction attempt.";

    /// <summary>The rationale of every bodyless authorization, including every one recorded before
    /// guidance existed. An authorization whose rationale equals this carries no guidance.</summary>
    public const string DefaultRationale = "Continue only after explicit human authorization.";

    /// <summary>
    /// Deterministically normalizes: Unicode form C, line endings to <c>\n</c>, surrounding whitespace
    /// trimmed. Returns <see langword="null"/> when the result is blank, longer than
    /// <see cref="MaximumLength"/>, contains a control character other than <c>\n</c>, is not valid
    /// Unicode, or fails the shared unsafe-text check. Never throws and never echoes the input.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Invalid UTF-16 (an unpaired surrogate) is rejected rather than silently replaced, so the
        // persisted text is always exactly what was accepted.
        for (var index = 0; index < raw.Length; index++)
        {
            if (char.IsHighSurrogate(raw[index]) && index + 1 < raw.Length && char.IsLowSurrogate(raw[index + 1]))
            {
                index++;
            }
            else if (char.IsSurrogate(raw[index]))
            {
                return null;
            }
        }

        string normalized;
        try
        {
            normalized = raw.Normalize(NormalizationForm.FormC)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Trim();
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (normalized.Length == 0 || normalized.Length > MaximumLength)
        {
            return null;
        }

        foreach (var character in normalized)
        {
            if (char.IsControl(character) && character != '\n')
            {
                return null;
            }
        }

        // The default rationale is reserved for the bodyless authorization: accepting it as guidance
        // would persist bytes that read back as "no guidance" and drop the accepted text from the manifest.
        if (string.Equals(normalized, DefaultRationale, StringComparison.Ordinal))
        {
            return null;
        }

        return CollaborationMessageContentPolicy.IsSafeSummary(normalized) ? normalized : null;
    }

    /// <summary>Builds the HumanInstruction structured content for an accepted rationale (already
    /// normalized guidance, or <see cref="DefaultRationale"/>).</summary>
    public static string BuildStructuredContentJson(string rationale) =>
        JsonSerializer.Serialize(new { instruction = FixedInstruction, rationale });

    /// <summary>Reads the rationale of a persisted HumanInstruction. Returns <see langword="null"/> unless
    /// the content is exactly the canonical form this type writes: the bytes of
    /// <see cref="BuildStructuredContentJson"/> for either <see cref="DefaultRationale"/> or a rationale
    /// that is its own <see cref="Normalize"/> result (so within the length bound, safe, free of control
    /// characters, valid Unicode, form C, trimmed, and not the reserved value). Anything else — including
    /// shape-valid but overlong, unsafe, noncanonical, or differently encoded content — is refused, so a
    /// corrupted stored value can never reach a provider manifest or an idempotent success.</summary>
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
                || (!string.Equals(value, DefaultRationale, StringComparison.Ordinal)
                    && !string.Equals(Normalize(value), value, StringComparison.Ordinal))
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

    public static bool IsGuidance(string rationale) =>
        !string.Equals(rationale, DefaultRationale, StringComparison.Ordinal);
}
