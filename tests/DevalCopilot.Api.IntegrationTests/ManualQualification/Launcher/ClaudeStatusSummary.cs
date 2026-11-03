using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The only text lot A prints or keeps, composed field by field from closed names, numbers and flags: no raw output, account,
/// e-mail, configuration path, environment name or value, or credential has a way in. As a last guard the finished text is replaced by
/// a fixed minimal verdict if it carries a drive path or any forbidden value. Its schema is independent of the exchange summary.
/// </summary>
public static partial class ClaudeStatusSummary
{
    public const int SchemaVersion = 1;
    public const string Authorization = "owner-qualification-profile-01-lot-a-post-login";

    public static string Render(
        string sessionId,
        ClaudeStatusRun run,
        ClaudeBinaryFacts binary,
        ClaudeStatusEnvironmentFacts ordinary,
        ClaudeStatusEnvironmentFacts filtered,
        ProcessAdmissionFacts admission,
        CleanupReport cleanup,
        IReadOnlyList<string> forbiddenFragments)
    {
        var text = Compose(sessionId, run, binary, ordinary, filtered, admission, cleanup);
        var unsafeText = DrivePath().IsMatch(text)
            || forbiddenFragments.Any(fragment =>
                !string.IsNullOrEmpty(fragment) && text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        return unsafeText
            ? $"{{\"statusSchemaVersion\":{SchemaVersion},\"result\":\"Failed\",\"code\":\"SummaryRefused\"}}"
            : text;
    }

    private static string Compose(
        string sessionId,
        ClaudeStatusRun run,
        ClaudeBinaryFacts binary,
        ClaudeStatusEnvironmentFacts ordinary,
        ClaudeStatusEnvironmentFacts filtered,
        ProcessAdmissionFacts admission,
        CleanupReport cleanup)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("statusSchemaVersion", SchemaVersion);
            writer.WriteString("authorization", Authorization);
            writer.WriteString("session", Token(sessionId));
            writer.WriteString("decision", run.Decision.ToString());
            WriteNullable(writer, "stopCode", run.StopCode);
            writer.WriteStartObject("binary");
            writer.WriteBoolean("verified", binary.Verified);
            writer.WriteNumber("sizeBytes", binary.SizeBytes);
            writer.WriteString("sha256", Hex(binary.Sha256));
            writer.WriteEndObject();
            writer.WriteStartObject("environment");
            WriteEnvironment(writer, "ordinary", ordinary);
            WriteEnvironment(writer, "filtered", filtered);
            writer.WriteEndObject();
            writer.WriteStartArray("observations");
            WriteObservation(writer, run.Ordinary);
            WriteObservation(writer, run.Filtered);
            writer.WriteEndArray();
            writer.WriteStartObject("comparison");
            writer.WriteString("configDirectory", run.ConfigDirectory.ToString());
            writer.WriteEndObject();
            writer.WriteStartObject("processAdmission");
            writer.WriteNumber("totalAdmitted", admission.TotalAdmitted);
            writer.WriteNumber("totalRefused", admission.TotalRefused);
            WriteNullable(writer, "closedBy", admission.ClosedBy);
            writer.WriteStartArray("pending");
            foreach (var pending in admission.PendingClasses)
            {
                writer.WriteStringValue(Token(pending));
            }

            writer.WriteEndArray();
            WriteCounts(writer, "admitted", admission.Admitted);
            WriteCounts(writer, "refused", admission.Refused);
            writer.WriteEndObject();
            writer.WriteBoolean("rawOutputPersisted", false);
            writer.WriteBoolean("descendantShutdownClaimed", false);
            writer.WriteStartObject("cleanup");
            writer.WriteBoolean("ownedRootRemoved", cleanup.Removed);
            writer.WriteString("reason", Token(cleanup.Reason));
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteEnvironment(Utf8JsonWriter writer, string name, ClaudeStatusEnvironmentFacts facts)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("keyCount", facts.KeyCount);
        writer.WriteBoolean("hasUserProfile", facts.HasUserProfile);
        writer.WriteBoolean("hasClaudeConfigDirectory", facts.HasClaudeConfigDirectory);
        writer.WriteBoolean("hasAnthropicVariable", facts.HasAnthropicVariable);
        writer.WriteBoolean("hasClaudeVariable", facts.HasClaudeVariable);
        writer.WriteEndObject();
    }

    private static void WriteObservation(Utf8JsonWriter writer, ClaudeStatusObservation observation)
    {
        writer.WriteStartObject();
        writer.WriteString("context", Token(observation.Context));
        writer.WriteString("physical", observation.Physical.ToString());
        WriteNullable(writer, "exitCode", observation.ExitCode);
        WriteNullable(writer, "durationMilliseconds", observation.DurationMilliseconds);
        writer.WriteStartObject("ioCompletion");
        WriteNullable(writer, "standardOutput", observation.StandardOutputCompletion);
        WriteNullable(writer, "standardError", observation.StandardErrorCompletion);
        writer.WriteBoolean("complete", observation.IoComplete);
        writer.WriteBoolean("standardOutputTruncated", observation.StandardOutputTruncated);
        writer.WriteBoolean("standardErrorTruncated", observation.StandardErrorTruncated);
        writer.WriteEndObject();
        writer.WriteString("reported", observation.State.ToString());
        writer.WriteString("authMethod", Token(observation.AuthMethod));
        writer.WriteEndObject();
    }

    private static void WriteCounts(Utf8JsonWriter writer, string name, IReadOnlyDictionary<string, int> counts)
    {
        writer.WriteStartObject(name);
        foreach (var (key, count) in counts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WriteNumber(Token(key), count);
        }

        writer.WriteEndObject();
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, Token(value));
        }
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, long? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteNumber(name, value.Value);
        }
    }

    private static string Token(string value) => ClosedToken().IsMatch(value) ? value : "unrecognized";

    private static string Hex(string value) => HexDigest().IsMatch(value) ? value : "unrecognized";

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,96}$")]
    private static partial Regex ClosedToken();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex HexDigest();

    [GeneratedRegex("[A-Za-z]:[/\\\\]")]
    private static partial Regex DrivePath();
}
