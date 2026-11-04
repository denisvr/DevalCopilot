using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The only text a session prints or keeps. It is composed field by field from closed names, numbers, flags, plain version tokens
/// and opaque identities, so a path, a credential, a provider output, a manifest or an account identity has no way in. As a last
/// guard the finished text is refused, and replaced by a fixed minimal verdict, if it carries a drive path or any forbidden value.
/// Schema 4 replaces the single message member of a stage with the bounded list of the produced messages the timeline listed for the
/// accepted attempt (at most <see cref="StageMessageBounds.MaximumListed"/>): per message its opaque identity, closed type, reply
/// target, author, provenance, whether it belongs to the accepted attempt and its two closed comparisons (the public message-evidence
/// route and the stored message row), beside the stage's complete message count and the closed comparison of the complete distinct
/// identity set with the stored rows. The count and the list are null when the timeline was never enumerated, which is never the same
/// as an observed zero with an empty list. A stage reading that could not have been observed (an undefined state, a negative count,
/// a list that does not match its count and the listing cap, or a comparison performed beyond the evidence bound of its role and
/// outcome) is refused the same way. Schema 3 added each available stage's recorded adapter contract version, its recorded I/O
/// completion facts with the derived required-completion flag, its closed invocation diagnostic with its vocabulary version, the
/// closed attempt-evidence route comparison (read even when no message exists) and the closed process-admission counts. Schema 2
/// added each available stage's run attempt counts, its nullable produced-message count and its closed evidence-route comparison
/// state. Schema 1, 2 and 3 summaries are historical and never read or rewritten.
/// </summary>
public static partial class SafeSummary
{
    public const int SchemaVersion = 4;

    public static string Render(QualificationReport report, IReadOnlyList<string> forbiddenFragments)
    {
        var text = Compose(report);
        var unsafeText = !IsObservable(report.Planner) || !IsObservable(report.Reviewer)
            || DrivePath().IsMatch(text)
            || forbiddenFragments.Any(fragment =>
                !string.IsNullOrEmpty(fragment) && text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        return unsafeText
            ? $"{{\"schemaVersion\":{SchemaVersion},\"result\":\"Failed\",\"code\":\"SummaryRefused\"}}"
            : text;
    }

    /// <summary>The fixed verdict when the whole sequence's outer bound elapsed while the session was still running: nothing but
    /// the identity, the closed admission counts and the statement that nothing was stopped or removed from here. It claims no
    /// stage reading, no source integrity and no shutdown.</summary>
    public static string RenderOuterDeadline(string sessionId, ProcessAdmissionFacts admission, string expiredDuring = "Session")
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("authorization", InvocationLedger.Authorization);
            writer.WriteString("session", Token(sessionId));
            writer.WriteString("result", QualificationResult.Failed.ToString());
            writer.WriteString("code", "OuterDeadlineExceeded");
            writer.WriteString("expiredDuring", Token(expiredDuring));
            writer.WriteBoolean("finalizationComplete", false);
            WriteAdmission(writer, admission);
            writer.WriteStartObject("shutdown");
            writer.WriteBoolean("hostStopped", false);
            writer.WriteBoolean("childProcessesProvenStopped", false);
            writer.WriteString("childProcessProof", "NotAttempted");
            writer.WriteEndObject();
            writer.WriteStartObject("cleanup");
            writer.WriteBoolean("ownedRootRemoved", false);
            writer.WriteString("reason", "OuterDeadlineExceeded");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>An unavailable reading is written as null and needs no check; an available one must carry only states the reader
    /// can produce.</summary>
    private static bool IsObservable(StageReading? reading)
    {
        if (reading is null || !reading.AttemptFound)
        {
            return true;
        }

        if (!Enum.IsDefined(reading.MessageSetComparison)
            || !Enum.IsDefined(reading.AttemptEvidenceRouteComparison)
            || reading.AttemptsInRun < 0
            || reading.DispatchedAttemptsInRun < 0
            || reading.MessageCount < 0)
        {
            return false;
        }

        // A list can exist only for an enumerated count, holds exactly the messages that count and the listing cap allow, and each of
        // its comparisons is a defined one made for a known message; the evidence route was asked for no more than the bound allows.
        // The set comparison is made whenever the timeline was enumerated (an observed empty timeline included) and only then.
        var expectedListed = Math.Min(reading.MessageCount ?? 0, StageMessageBounds.MaximumListed);
        var compared = reading.Messages.Count(message => message.EvidenceRouteComparison != EvidenceRouteComparisonStatus.NotAttempted);
        return reading.Messages.Count == expectedListed
            && (reading.MessageCount is null) == (reading.MessageSetComparison == EvidenceRouteComparisonStatus.NotAttempted)
            && compared <= StageMessageBounds.Maximum(reading.Role, reading.Outcome)
            && reading.Messages.All(message => message.Id != Guid.Empty
                && Enum.IsDefined(message.EvidenceRouteComparison)
                && Enum.IsDefined(message.PersistedMetadataComparison));
    }

    private static string Compose(QualificationReport report)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("authorization", Token(report.Authorization));
            writer.WriteString("session", Token(report.SessionId));
            writer.WriteString("result", report.Result.ToString());
            writer.WriteString("code", Token(report.Code));
            WriteNullable(writer, "verdict", report.Verdict);
            WriteTargets(writer, report.Targets);
            WriteAllowances(writer, report);
            WriteAdmission(writer, report.Admission);
            WriteStage(writer, "planner", report.Planner, null, report.PlannerAcceptedAttemptId);
            // The Proposal of the lineage claims is the sole message of the planner attempt the planner POST was accepted for.
            var proposal = report.Planner is { } planner && planner.AttemptId == report.PlannerAcceptedAttemptId
                ? planner.SoleMessage?.Id
                : null;
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
        writer.WriteNumber("attemptsInRun", reading.AttemptsInRun);
        writer.WriteNumber("dispatchedAttemptsInRun", reading.DispatchedAttemptsInRun);
        writer.WriteString("provider", Token(reading.Provider));
        writer.WriteString("role", Token(reading.Role));
        writer.WriteString("contract", Token(reading.Contract));
        writer.WriteString("status", Token(reading.Status));
        WriteNullable(writer, "outcome", reading.Outcome);
        writer.WriteString("adapterContractVersion", Token(reading.AdapterContractVersion));
        writer.WriteBoolean("dispatched", reading.Dispatched);
        writer.WriteStartObject("process");
        WriteNullable(writer, "outcome", reading.ProcessOutcome);
        WriteNullable(writer, "exitCode", reading.ExitCode);
        WriteNullable(writer, "durationMilliseconds", reading.DurationMilliseconds);
        writer.WriteEndObject();
        writer.WriteStartObject("ioCompletion");
        WriteNullable(writer, "standardOutput", reading.StandardOutputCompletion);
        WriteNullable(writer, "standardError", reading.StandardErrorCompletion);
        WriteNullable(writer, "standardInput", reading.StandardInputDelivery);
        writer.WriteBoolean("requiredComplete", reading.RequiredIoComplete);
        writer.WriteEndObject();
        writer.WriteStartObject("invocationDiagnostic");
        writer.WriteString("state", Token(reading.InvocationDiagnosticState));
        WriteNullable(writer, "category", reading.InvocationDiagnosticCategory);
        WriteNullable(writer, "version", reading.InvocationDiagnosticVersion);
        writer.WriteEndObject();
        WriteNullable(writer, "messageCount", reading.MessageCount);
        WriteMessages(writer, reading, acceptedAttemptId);
        writer.WriteStartObject("lineage");
        writer.WriteNumber("recordedInputs", reading.InputMessageIds.Count);
        var bound = proposalMessageId is not null;
        // A claim about the whole set needs the whole set: an observed, non-empty and untruncated listing, every reply answering it.
        var completeListing = reading.MessageCount is > 0 && reading.Messages.Count == reading.MessageCount;
        writer.WriteBoolean(
            "repliesToThePersistedProposal",
            bound && completeListing && reading.Messages.All(message => message.InReplyToMessageId == proposalMessageId));
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
        writer.WriteString("evidenceRouteComparison", reading.EvidenceRouteComparison.ToString());
        writer.WriteBoolean("attemptEvidenceRouteAgrees", reading.AttemptEvidenceRouteAgrees);
        writer.WriteString("attemptEvidenceRouteComparison", reading.AttemptEvidenceRouteComparison.ToString());
        writer.WriteEndObject();
    }

    /// <summary>The bounded message list. Null (not an empty array) when the timeline was never enumerated, so that an unobserved
    /// count is never read as an observed zero; an observed zero is an empty array.</summary>
    private static void WriteMessages(Utf8JsonWriter writer, StageReading reading, Guid? acceptedAttemptId)
    {
        if (reading.MessageCount is not { } count)
        {
            writer.WriteNull("messages");
            writer.WriteNull("messagesTruncated");
            writer.WriteString("messageSetComparison", reading.MessageSetComparison.ToString());
            return;
        }

        writer.WriteStartArray("messages");
        foreach (var message in reading.Messages)
        {
            writer.WriteStartObject();
            writer.WriteString("id", message.Id);
            WriteNullable(writer, "type", message.Type);
            WriteNullable(writer, "inReplyTo", message.InReplyToMessageId);
            WriteNullable(writer, "actor", message.Actor);
            WriteNullable(writer, "provenance", message.Provenance);
            // Against the attempt the POST was accepted for, never against the attempt that happened to be observed.
            writer.WriteBoolean("belongsToAcceptedAttempt", acceptedAttemptId is not null && message.AttemptId == acceptedAttemptId);
            writer.WriteString("evidenceRouteComparison", message.EvidenceRouteComparison.ToString());
            writer.WriteString("persistedMetadataComparison", message.PersistedMetadataComparison.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteBoolean("messagesTruncated", count > reading.Messages.Count);
        writer.WriteString("messageSetComparison", reading.MessageSetComparison.ToString());
    }

    private static void WriteAdmission(Utf8JsonWriter writer, ProcessAdmissionFacts facts)
    {
        writer.WriteStartObject("processAdmission");
        writer.WriteNumber("totalAdmitted", facts.TotalAdmitted);
        writer.WriteNumber("totalRefused", facts.TotalRefused);
        WriteNullable(writer, "closedBy", facts.ClosedBy);
        WriteTokens(writer, "pending", facts.PendingClasses);
        WriteCounts(writer, "admitted", facts.Admitted);
        WriteCounts(writer, "refused", facts.Refused);
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
        writer.WriteStartObject("closure");
        writer.WriteBoolean("environmentDisposed", report.EnvironmentDisposed == true);
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
