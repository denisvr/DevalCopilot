using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The only text a session prints or keeps. It is composed field by field from closed names, numbers, flags, plain version tokens
/// and opaque identities, so a path, a credential, a provider output, a manifest or an account identity has no way in. As a last
/// guard the finished text is refused, and replaced by a fixed minimal verdict, if it carries a drive path or any forbidden value.
/// </summary>
public static partial class SafeSummary
{
    public const int SchemaVersion = 1;

    public static string Render(QualificationReport report, IReadOnlyList<string> forbiddenFragments)
    {
        var text = Compose(report);
        var unsafeText = DrivePath().IsMatch(text)
            || forbiddenFragments.Any(fragment =>
                !string.IsNullOrEmpty(fragment) && text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        return unsafeText
            ? $"{{\"schemaVersion\":{SchemaVersion},\"result\":\"Failed\",\"code\":\"SummaryRefused\"}}"
            : text;
    }

    private static string Compose(QualificationReport report)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("authorization", InvocationLedger.Authorization);
            writer.WriteString("session", Token(report.SessionId));
            writer.WriteString("result", report.Result.ToString());
            writer.WriteString("code", Token(report.Code));
            WriteNullable(writer, "verdict", report.Verdict);
            WriteTargets(writer, report.Targets);
            WriteAllowances(writer, report);
            WriteStage(writer, "planner", report.Planner, null, report.PlannerAcceptedAttemptId);
            var proposal = report.Planner?.MessageId;
            WriteStage(writer, "reviewer", report.Reviewer, proposal, report.ReviewerAcceptedAttemptId);
            writer.WriteStartObject("source");
            writer.WriteNumber("checks", report.SourceChecks);
            writer.WriteBoolean("proven", report.SourceProven);
            writer.WriteBoolean("unchanged", report.SourceProven && report.SourceDifferences.Count == 0);
            WriteTokens(writer, "differences", report.SourceDifferences);
            writer.WriteEndObject();
            WriteEnd(writer, report);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteTargets(Utf8JsonWriter writer, TargetJudgement? targets)
    {
        writer.WriteStartArray("targets");
        foreach (var target in targets?.Targets ?? [])
        {
            writer.WriteStartObject();
            writer.WriteString("provider", Token(target.Provider));
            writer.WriteBoolean("verifiedInstalled", target.Real);
            writer.WriteString("code", Token(target.Code));
            WriteNullable(writer, "launchKind", target.LaunchKind);
            WriteNullable(writer, "observedVersion", target.Real ? Version(target.Version) : null);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteAllowances(Utf8JsonWriter writer, QualificationReport report)
    {
        writer.WriteStartObject("allowances");
        WriteAllowance(writer, "planner", report, AllowanceRole.Planner);
        WriteAllowance(writer, "reviewer", report, AllowanceRole.Reviewer);
        var unused = new List<string>();
        if (!report.PlannerConsumed)
        {
            unused.Add("planner");
        }

        if (!report.ReviewerConsumed)
        {
            unused.Add("reviewer");
        }

        WriteTokens(writer, "unused", unused);
        writer.WriteEndObject();
    }

    private static void WriteAllowance(
        Utf8JsonWriter writer,
        string name,
        QualificationReport report,
        AllowanceRole role)
    {
        var planner = role == AllowanceRole.Planner;
        var accepted = planner ? report.PlannerAcceptedAttemptId : report.ReviewerAcceptedAttemptId;
        var state = report.InvocationState(role);
        writer.WriteStartObject(name);
        writer.WriteBoolean("consumedBeforePost", planner ? report.PlannerConsumed : report.ReviewerConsumed);
        writer.WriteBoolean("postAttempted", planner ? report.PlannerSubmitted : report.ReviewerSubmitted);
        WriteNullable(writer, "acceptedAttemptId", accepted);
        writer.WriteBoolean("dispatched", state is "ExecutionObserved" or "DispatchedNoExecutionEvidence");
        writer.WriteBoolean("executionObserved", state == "ExecutionObserved");
        writer.WriteString("invocationState", state);
        writer.WriteEndObject();
    }

    private static void WriteStage(
        Utf8JsonWriter writer,
        string name,
        StageReading? reading,
        Guid? proposalMessageId,
        Guid? acceptedAttemptId)
    {
        if (reading is null || !reading.AttemptFound)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        WriteNullable(writer, "acceptedAttemptId", acceptedAttemptId);
        writer.WriteString("attemptId", reading.AttemptId);
        var matches = acceptedAttemptId is not null && reading.AttemptId == acceptedAttemptId;
        writer.WriteBoolean("attemptIdMatchesAccepted", matches);
        writer.WriteNumber("attemptNumber", reading.AttemptNumber);
        writer.WriteString("provider", Token(reading.Provider));
        writer.WriteString("role", Token(reading.Role));
        writer.WriteString("contract", Token(reading.Contract));
        writer.WriteString("status", Token(reading.Status));
        WriteNullable(writer, "outcome", reading.Outcome);
        writer.WriteBoolean("dispatched", reading.Dispatched);
        writer.WriteStartObject("process");
        WriteNullable(writer, "outcome", reading.ProcessOutcome);
        WriteNullable(writer, "exitCode", reading.ExitCode);
        WriteNullable(writer, "durationMilliseconds", reading.DurationMilliseconds);
        writer.WriteEndObject();
        writer.WriteStartObject("message");
        WriteNullable(writer, "id", reading.MessageId);
        WriteNullable(writer, "type", reading.MessageType);
        WriteNullable(writer, "inReplyTo", reading.InReplyToMessageId);
        WriteNullable(writer, "actor", reading.MessageActor);
        WriteNullable(writer, "provenance", reading.MessageProvenance);
        writer.WriteEndObject();
        writer.WriteStartObject("lineage");
        writer.WriteNumber("recordedInputs", reading.InputMessageIds.Count);
        var bound = proposalMessageId is not null;
        writer.WriteBoolean("repliesToThePersistedProposal", bound && reading.InReplyToMessageId == proposalMessageId);
        writer.WriteBoolean(
            "inputIsExactlyThatProposal",
            bound && reading.InputMessageIds.SequenceEqual([proposalMessageId!.Value]));
        writer.WriteBoolean(
            "sealedInputNamesThatProposal",
            bound && reading.ManifestProposalMessageId == proposalMessageId);
        writer.WriteEndObject();
        writer.WriteStartObject("sealed");
        writer.WriteBoolean("manifestPresent", reading.ManifestPresent);
        writer.WriteBoolean("manifestBytesAndHashAgree", reading.ManifestBytesAgree);
        writer.WriteBoolean("manifestContractAgrees", reading.ManifestContractAgrees);
        writer.WriteBoolean("manifestObjectiveAgrees", reading.ManifestObjectiveAgrees);
        writer.WriteNumber("artifacts", reading.ArtifactCount);
        writer.WriteNumber("artifactsAgreeingWithTheirHash", reading.ArtifactsAgreeing);
        writer.WriteEndObject();
        writer.WriteBoolean("statusRouteAgrees", reading.StatusRouteAgrees);
        writer.WriteBoolean("evidenceRouteAgrees", reading.EvidenceRouteAgrees);
        writer.WriteEndObject();
    }

    private static void WriteEnd(Utf8JsonWriter writer, QualificationReport report)
    {
        writer.WriteStartObject("shutdown");
        writer.WriteBoolean("hostStopped", report.Shutdown is { HostStopped: true });
        writer.WriteBoolean("childProcessesProvenStopped", report.Shutdown is { ChildrenProven: true });
        WriteNullable(writer, "childProcessProof", report.Shutdown?.Reason);
        writer.WriteEndObject();
        writer.WriteStartObject("cleanup");
        writer.WriteBoolean("ownedRootRemoved", report.Cleanup is { Removed: true });
        WriteNullable(writer, "reason", report.Cleanup is null ? null : Token(report.Cleanup.Reason));
        writer.WriteEndObject();
    }

    private static void WriteTokens(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(Token(value));
        }

        writer.WriteEndArray();
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

    private static void WriteNullable(Utf8JsonWriter writer, string name, Guid? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value.Value);
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

    private static string? Version(string? value) => value is not null && VersionToken().IsMatch(value) ? value : null;

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,96}$")]
    private static partial Regex ClosedToken();

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z.+-]{0,63}$")]
    private static partial Regex VersionToken();

    [GeneratedRegex("[A-Za-z]:[/\\\\]")]
    private static partial Regex DrivePath();
}
