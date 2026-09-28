using System.Text.Json;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;

namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// Observes a picker-visible Codex model and reasoning-effort catalog by speaking the documented
/// Codex App Server <c>model/list</c> method (<c>https://learn.chatgpt.com/docs/app-server</c>)
/// directly against the same already-vetted local Codex CLI launch target every other Codex
/// adapter uses, reusing <see cref="CodexAppServerSession"/> and <see cref="CodexAppServerChannel"/>
/// for the launch, handshake, request, and cleanup mechanics <see cref="CodexAccountAllowanceAdapter"/>
/// already established.
///
/// <para>
/// Always requests <c>includeHidden: false</c> so only picker-visible models are asked for; any
/// entry the provider still marks <c>hidden: true</c> is discarded defensively rather than
/// trusted into the projection. Pages are requested with a bounded page size and followed only
/// through a bounded number of <c>nextCursor</c> continuations — a provider that still claims more
/// pages exist once that bound is reached is never presented as a silently truncated but
/// otherwise "complete" catalog: the whole observation fails closed to
/// <see cref="CodexModelCatalogObservation.Unknown"/> instead.
/// </para>
///
/// <para>
/// A model's own <c>id</c> is treated like the account-allowance adapter's limit id: bounded,
/// restricted to a safe identifier character set, and required to be unique across every page: a
/// missing, oversized, malformed, or duplicate id fails the whole catalog closed, never just that
/// one entry. <c>displayName</c> is validated against control and bidirectional-formatting
/// characters and falls back to the model's own (already-validated) id when it is missing,
/// oversized, or unsafe — never failing the entry. <c>supportedReasoningEfforts</c> and
/// <c>defaultReasoningEffort</c> are descriptive data, not identifiers, but a malformed or
/// duplicate individual effort makes the whole <c>supportedReasoningEfforts</c> field for that
/// entry <see langword="null"/> (Unknown) rather than silently presenting a partial list with the
/// bad element dropped; a reported default that is not itself among a known supported-effort list
/// is likewise projected as <see langword="null"/> rather than an internally inconsistent claim.
/// </para>
///
/// <para>
/// This is catalog evidence only — never model or effort selection, invocation arguments, account
/// authentication, or a guarantee that a listed model remains available at dispatch.
/// </para>
/// </summary>
public sealed class CodexModelCatalogAdapter(TimeProvider timeProvider, TimeSpan? invocationTimeout = null) : ICodexModelCatalogAdapter
{
    private const int MaxTotalCapturedBytes = 64 * 1024;
    private const int MaxLineBytes = 16 * 1024;

    private const int MaxPages = 8;
    private const int PageSize = 50;
    private const int MaxTotalEntries = 200;
    private const int MaxCursorLength = 256;
    private const int MaxModelIdLength = 128;
    private const int MaxDisplayNameLength = 200;
    private const int MaxReasoningEffortLength = 32;
    private const int MaxSupportedReasoningEffortsPerEntry = 8;

    /// <summary>The Unicode bidirectional-formatting control characters this projection refuses
    /// to carry in a displayed model name: each can reorder or hide adjacent rendered text without
    /// changing the underlying characters, which a plain "reject control characters" check alone
    /// would not catch (none of them is itself a C0/C1 control character). Expressed with
    /// C#'s \uXXXX escape syntax rather than the actual invisible characters, so the
    /// source remains reviewable.
    /// Includes U+061C ARABIC LETTER MARK alongside the more commonly cited LRM/RLM, embedding
    /// (LRE/RLE/PDF), override (LRO/RLO), and isolate (LRI/RLI/FSI/PDI) characters.</summary>
    private static readonly char[] BidiFormattingCharacters =
    [
        '\u061C',
        '\u200E', '\u200F',
        '\u202A', '\u202B', '\u202C', '\u202D', '\u202E',
        '\u2066', '\u2067', '\u2068', '\u2069',
    ];

    private readonly TimeSpan _invocationTimeout = invocationTimeout ?? CodexAppServerSession.DefaultInvocationTimeout;

    public Task<CodexModelCatalogObservation> ObserveAsync(
        string executablePath, string? scriptPath, CancellationToken cancellationToken) =>
        CodexAppServerSession.RunAsync(
            executablePath,
            scriptPath,
            _invocationTimeout,
            MaxTotalCapturedBytes,
            MaxLineBytes,
            CodexModelCatalogObservation.Unknown,
            ExchangeAsync,
            cancellationToken);

    private async Task<CodexModelCatalogObservation> ExchangeAsync(CodexAppServerChannel channel, CancellationToken cancellationToken)
    {
        var models = new List<CodexModelCatalogEntry>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var processedCount = 0;
        string? cursor = null;

        for (var page = 1; page <= MaxPages; page++)
        {
            var response = await channel.SendModelListAsync(page, cursor, PageSize, cancellationToken).ConfigureAwait(false);
            if (response is not { } element || !CodexAppServerSession.IsWellFormedSuccessResponse(element))
            {
                return CodexModelCatalogObservation.Unknown;
            }

            if (!TryReadPage(element, models, seenIds, ref processedCount, out var nextCursor))
            {
                return CodexModelCatalogObservation.Unknown;
            }

            if (nextCursor is null)
            {
                return models.Count == 0
                    ? CodexModelCatalogObservation.Unknown
                    : new CodexModelCatalogObservation(true, timeProvider.GetUtcNow(), models);
            }

            cursor = nextCursor;
        }

        // The provider still claims more pages exist after the bounded page-count cap: never
        // present a silently truncated catalog as if it were complete.
        return CodexModelCatalogObservation.Unknown;
    }

    /// <summary>
    /// Reads one <c>model/list</c> response page: its <c>result.data</c> array of model entries
    /// and its <c>result.nextCursor</c> continuation. A malformed top-level shape, an oversized
    /// total entry count across every page so far, a malformed entry, a malformed or oversized
    /// <c>nextCursor</c>, or a duplicate model id fails the whole page (and therefore the whole
    /// catalog) closed.
    /// </summary>
    private static bool TryReadPage(
        JsonElement response,
        List<CodexModelCatalogEntry> models,
        HashSet<string> seenIds,
        ref int processedCount,
        out string? nextCursor)
    {
        nextCursor = null;

        if (!response.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var entry in data.EnumerateArray())
        {
            processedCount++;
            if (processedCount > MaxTotalEntries || entry.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!TryReadEntry(entry, seenIds, out var visibleEntry))
            {
                return false;
            }

            if (visibleEntry is { } model)
            {
                models.Add(model);
            }
        }

        if (!result.TryGetProperty("nextCursor", out var nextCursorElement) || nextCursorElement.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (nextCursorElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var cursor = nextCursorElement.GetString();
        if (string.IsNullOrEmpty(cursor) || cursor.Length > MaxCursorLength)
        {
            return false;
        }

        nextCursor = cursor;
        return true;
    }

    /// <summary>
    /// Reads one model entry. <paramref name="visibleEntry"/> is <see langword="null"/> for a
    /// well-formed but hidden entry (silently discarded, never surfaced); the method returns
    /// <see langword="false"/> only when the entry cannot be trusted at all (a missing, oversized,
    /// malformed, or duplicate id; a malformed <c>hidden</c> flag; or an excessive
    /// <c>supportedReasoningEfforts</c> list).
    /// </summary>
    private static bool TryReadEntry(JsonElement entry, HashSet<string> seenIds, out CodexModelCatalogEntry? visibleEntry)
    {
        visibleEntry = null;

        if (!entry.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var id = idElement.GetString();
        if (!IsAcceptedIdentifier(id, MaxModelIdLength) || !seenIds.Add(id!))
        {
            // A missing/oversized/malformed id, or one already seen on this or an earlier page,
            // means the response cannot be trusted: a single catalog read has exactly one entry
            // per id.
            return false;
        }

        bool hidden;
        if (!entry.TryGetProperty("hidden", out var hiddenElement))
        {
            hidden = false;
        }
        else if (hiddenElement.ValueKind == JsonValueKind.True)
        {
            hidden = true;
        }
        else if (hiddenElement.ValueKind == JsonValueKind.False)
        {
            hidden = false;
        }
        else
        {
            return false;
        }

        if (hidden)
        {
            return true;
        }

        var displayName = TryReadDisplayName(entry, id!);
        var supportedReasoningEfforts = TryReadSupportedReasoningEfforts(entry, out var effortsFatal);
        if (effortsFatal)
        {
            return false;
        }

        var defaultReasoningEffort = TryReadDefaultReasoningEffort(entry, supportedReasoningEfforts);

        visibleEntry = new CodexModelCatalogEntry(id!, displayName, supportedReasoningEfforts, defaultReasoningEffort);
        return true;
    }

    /// <summary>Falls back to the model's own already-validated id whenever <c>displayName</c> is
    /// missing, oversized, blank, or carries a control or bidirectional-formatting character —
    /// never failing the entry over descriptive display text.</summary>
    private static string TryReadDisplayName(JsonElement entry, string fallbackId)
    {
        if (entry.TryGetProperty("displayName", out var displayNameElement) && displayNameElement.ValueKind == JsonValueKind.String)
        {
            var displayName = displayNameElement.GetString();
            if (IsSafeDisplayName(displayName))
            {
                return displayName!;
            }
        }

        return fallbackId;
    }

    private static bool IsSafeDisplayName(string? displayName) =>
        !string.IsNullOrWhiteSpace(displayName)
        && displayName.Length <= MaxDisplayNameLength
        && displayName.All(character => !char.IsControl(character))
        && !displayName.Any(character => BidiFormattingCharacters.Contains(character));

    /// <summary>
    /// Reads <c>supportedReasoningEfforts</c> as a whole: either every element is a well-formed,
    /// bounded, non-duplicate effort identifier, or the entire field is projected as
    /// <see langword="null"/> (Unknown) — this list is never presented with a malformed or
    /// duplicate element silently skipped, since a partial list would misrepresent what the
    /// provider actually reported. <paramref name="fatal"/> is set only for a genuinely excessive
    /// or wrongly-shaped list, which fails the whole catalog closed exactly like an oversized
    /// bucket map elsewhere in this application; an absent or explicitly empty list is not fatal
    /// and returns an empty (not null) result.
    /// </summary>
    private static IReadOnlyList<string>? TryReadSupportedReasoningEfforts(JsonElement entry, out bool fatal)
    {
        fatal = false;

        if (!entry.TryGetProperty("supportedReasoningEfforts", out var effortsElement) || effortsElement.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (effortsElement.ValueKind != JsonValueKind.Array)
        {
            fatal = true;
            return null;
        }

        var efforts = new List<string>();
        var seenEfforts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effortEntry in effortsElement.EnumerateArray())
        {
            if (efforts.Count >= MaxSupportedReasoningEffortsPerEntry)
            {
                // Excessive is treated like an oversized bucket map: fatal to the whole catalog,
                // not merely Unknown for this one field.
                fatal = true;
                return null;
            }

            if (effortEntry.ValueKind != JsonValueKind.Object
                || !effortEntry.TryGetProperty("reasoningEffort", out var effortNameElement)
                || effortNameElement.ValueKind != JsonValueKind.String)
            {
                // A malformed individual element makes the whole list untrustworthy — never a
                // partial list with the bad element silently dropped.
                return null;
            }

            var effortName = effortNameElement.GetString();
            if (!IsAcceptedIdentifier(effortName, MaxReasoningEffortLength) || !seenEfforts.Add(effortName!))
            {
                return null;
            }

            efforts.Add(effortName!);
        }

        return efforts;
    }

    /// <summary>A reported default is projected only when it is itself a bounded, valid identifier
    /// AND a member of this entry's own known (non-Unknown) supported-effort list — an internally
    /// inconsistent or unverifiable default is Unknown, never an invented or unchecked value.</summary>
    private static string? TryReadDefaultReasoningEffort(JsonElement entry, IReadOnlyList<string>? supportedReasoningEfforts)
    {
        if (!entry.TryGetProperty("defaultReasoningEffort", out var element) || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = element.GetString();
        if (!IsAcceptedIdentifier(value, MaxReasoningEffortLength))
        {
            return null;
        }

        return supportedReasoningEfforts is not null && supportedReasoningEfforts.Contains(value, StringComparer.Ordinal)
            ? value
            : null;
    }

    /// <summary>Shared bounded-identifier acceptance check for both a model id and a reasoning-
    /// effort identifier: untrusted provider text, restricted to a safe character set before it is
    /// ever kept, so a malformed or adversarial value can never reach API, logs, or UI.</summary>
    private static bool IsAcceptedIdentifier(string? identifier, int maxLength) =>
        !string.IsNullOrEmpty(identifier)
        && identifier.Length <= maxLength
        && identifier.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
