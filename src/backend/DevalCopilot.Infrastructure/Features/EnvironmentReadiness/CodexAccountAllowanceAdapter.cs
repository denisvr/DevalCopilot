using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexAccountAllowance;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// Observes a Codex ChatGPT account-allowance snapshot by speaking the documented Codex App
/// Server JSON-RPC protocol (stdio JSONL, the required <c>initialize</c>/<c>initialized</c>
/// handshake, then the sole <c>account/rateLimits/read</c> read method) directly against the
/// same already-vetted local Codex CLI launch target every other Codex adapter uses.
///
/// <para>
/// The shared <see cref="CodexProcessInvoker"/> cannot be reused here: it is a deliberately
/// one-shot contract (write the complete stdin payload, close it, then wait for the process to
/// exit on its own), while the App Server is a long-running duplex JSON-RPC peer that this
/// adapter must itself terminate once its one bounded read-only exchange completes. This class
/// preserves the same launch-target revalidation, no-shell/no-PATH-search argument passing,
/// restricted environment allowlist, finite timeout, bounded output capture, cancellation
/// propagation, and process-tree cleanup that contract already established — it is a second,
/// narrow instance of those same protections, never a weaker one.
/// </para>
///
/// <para>
/// Never passes an experimental, bypass, or mutating flag; never opens a listening socket;
/// never reads a CLI auth file or supplies a token — the App Server locates the CLI's own
/// existing local authentication itself, exactly as every other Codex invocation already does.
/// This is a read-only observation of an existing snapshot, never resume eligibility, invocation
/// eligibility, or authority to invoke anything else.
/// </para>
/// </summary>
public sealed class CodexAccountAllowanceAdapter(TimeProvider timeProvider, TimeSpan? invocationTimeout = null) : ICodexAccountAllowanceAdapter
{
    private const int InitializeRequestId = 0;
    private const int AccountRateLimitsReadRequestId = 1;

    private const int MaxTotalCapturedBytes = 64 * 1024;
    private const int MaxLineBytes = 16 * 1024;
    private const int MaxBuckets = 16;

    /// <summary>A bounded ceiling on how many times this adapter asks the stream for another
    /// chunk while waiting for one correlated response — a safety net against a pathological
    /// peer that keeps sending a trickle of unrelated notifications forever without ever
    /// exhausting the byte budget in one read.</summary>
    private const int MaxFillAttempts = 256;

    private static readonly TimeSpan DefaultInvocationTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PostKillWait = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _invocationTimeout = invocationTimeout ?? DefaultInvocationTimeout;

    private const string InitializeRequestJson =
        """{"method":"initialize","id":0,"params":{"clientInfo":{"name":"DevalCopilot","title":"DevalCopilot","version":"1.0.0"},"capabilities":{}}}""";

    private const string InitializedNotificationJson = """{"method":"initialized","params":{}}""";

    private const string AccountRateLimitsReadRequestJson = """{"method":"account/rateLimits/read","id":1}""";

    public async Task<CodexAccountAllowanceObservation> ObserveAsync(
        string executablePath, string? scriptPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsAcceptableLaunchComponent(executablePath) || (scriptPath is not null && !IsAcceptableLaunchComponent(scriptPath)))
        {
            return CodexAccountAllowanceObservation.Unknown;
        }

        string workingDirectory;
        try
        {
            workingDirectory = HostScratchDirectory.EnsureExists();
            if (AgentInvocationScratchDirectory.PathOrAnyAncestorHasReparsePoint(workingDirectory))
            {
                return CodexAccountAllowanceObservation.Unknown;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CodexAccountAllowanceObservation.Unknown;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (scriptPath is not null)
        {
            startInfo.ArgumentList.Add(scriptPath);
        }

        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--stdio");

        startInfo.EnvironmentVariables.Clear();
        foreach (var (key, value) in BuildEnvironmentAllowlist())
        {
            startInfo.EnvironmentVariables[key] = value;
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return CodexAccountAllowanceObservation.Unknown;
        }

        using var timeoutSource = new CancellationTokenSource(_invocationTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        // Never surfaced, never trusted for anything — only drained so the child can never
        // block on a full stderr pipe while this adapter is reading stdout.
        var stderrDrain = DrainAndDiscardAsync(process.StandardError.BaseStream, linkedSource.Token);

        var observation = CodexAccountAllowanceObservation.Unknown;
        var cleanupSucceeded = false;
        try
        {
            observation = await ExchangeAsync(process, linkedSource.Token).ConfigureAwait(false);
            // A peer that already exited with an error is not a successful observation, even
            // if it wrote a plausible response just before failing.
            if (process.HasExited && process.ExitCode != 0)
            {
                observation = CodexAccountAllowanceObservation.Unknown;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // Only this adapter's own internal timeout can reach here (the caller's token is
            // re-checked and rethrown above) — a timeout is a safe, closed Unknown, never an
            // exception the caller must handle.
            observation = CodexAccountAllowanceObservation.Unknown;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            observation = CodexAccountAllowanceObservation.Unknown;
        }
        finally
        {
            cleanupSucceeded = await StopOwnedProcessAsync(process, linkedSource, stderrDrain).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return cleanupSucceeded ? observation : CodexAccountAllowanceObservation.Unknown;
    }

    private async Task<CodexAccountAllowanceObservation> ExchangeAsync(Process process, CancellationToken cancellationToken)
    {
        var scanner = new BoundedJsonLineScanner(process.StandardOutput.BaseStream, MaxTotalCapturedBytes, MaxLineBytes);

        await WriteLineAsync(process, InitializeRequestJson, cancellationToken).ConfigureAwait(false);
        var initializeResponse = await ReadCorrelatedResponseAsync(scanner, InitializeRequestId, cancellationToken).ConfigureAwait(false);
        if (initializeResponse is not { } initializeElement || !IsWellFormedSuccessResponse(initializeElement))
        {
            return CodexAccountAllowanceObservation.Unknown;
        }

        await WriteLineAsync(process, InitializedNotificationJson, cancellationToken).ConfigureAwait(false);
        await WriteLineAsync(process, AccountRateLimitsReadRequestJson, cancellationToken).ConfigureAwait(false);

        var rateLimitsResponse =
            await ReadCorrelatedResponseAsync(scanner, AccountRateLimitsReadRequestId, cancellationToken).ConfigureAwait(false);
        if (rateLimitsResponse is not { } rateLimitsElement || !IsWellFormedSuccessResponse(rateLimitsElement))
        {
            return CodexAccountAllowanceObservation.Unknown;
        }

        return ExtractObservation(rateLimitsElement, timeProvider.GetUtcNow());
    }

    private static async Task<bool> StopOwnedProcessAsync(
        Process process, CancellationTokenSource linkedSource, Task stderrDrain)
    {
        linkedSource.Cancel();
        var killed = true;
        try
        {
            ProcessTreeTermination.KillIfStillRunning(process);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            killed = false;
        }

        using var postKillTimeout = new CancellationTokenSource(PostKillWait);
        try
        {
            await process.WaitForExitAsync(postKillTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            killed = false;
        }

        try
        {
            await stderrDrain.WaitAsync(PostKillWait).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or TimeoutException)
        {
            killed = false;
        }

        return killed;
    }

    /// <summary>
    /// Waits for a complete, well-formed JSON object whose <c>id</c> matches
    /// <paramref name="expectedId"/>, reading only as much as is needed to decide. Every other
    /// object with a different id or a notification with no id is ignored; malformed JSONL
    /// fails the exchange closed. If more than one distinct reply for the same id is observed in the lines already
    /// available before this method decides, neither is trusted: a single invocation has exactly
    /// one true reply per request, so disagreement means the value cannot be trusted. Returns
    /// <see langword="null"/> when the stream is exhausted (end of stream or the total capture
    /// budget is spent) before any reply for this id arrives.
    /// </summary>
    private static async Task<JsonElement?> ReadCorrelatedResponseAsync(
        BoundedJsonLineScanner scanner, int expectedId, CancellationToken cancellationToken)
    {
        var matches = new List<JsonElement>();
        var fillAttempts = 0;

        while (true)
        {
            foreach (var line in scanner.DrainCompleteLines())
            {
                if (!TryReadMatchingReply(line, expectedId, out var match, out var malformed))
                {
                    if (malformed)
                    {
                        return null;
                    }

                    continue;
                }

                matches.Add(match);
            }

            if (matches.Count > 0)
            {
                break;
            }

            if (scanner.IsExhausted || fillAttempts >= MaxFillAttempts)
            {
                return null;
            }

            fillAttempts++;
            await scanner.FillAsync(cancellationToken).ConfigureAwait(false);
        }

        var distinctRawText = matches.Select(match => match.GetRawText()).Distinct().Count();
        return distinctRawText == 1 ? matches[0] : null;
    }

    private static bool TryReadMatchingReply(string line, int expectedId, out JsonElement match, out bool malformed)
    {
        match = default;
        malformed = false;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            malformed = true;
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                malformed = true;
                return false;
            }

            if (!document.RootElement.TryGetProperty("id", out var idElement))
            {
                // A notification has a method but no id; any other id-less object is not a
                // documented App Server message and cannot safely be treated as harmless noise.
                malformed = !document.RootElement.TryGetProperty("method", out var method)
                    || method.ValueKind != JsonValueKind.String;
                return false;
            }

            if (idElement.ValueKind != JsonValueKind.Number || !idElement.TryGetInt32(out var id))
            {
                malformed = true;
                return false;
            }

            if (id != expectedId)
            {
                return false;
            }

            match = document.RootElement.Clone();
            return true;
        }
    }

    /// <summary>A response line is trusted only when it is a complete JSON object that carries
    /// this exchange's own <c>result</c> property and no <c>error</c> — a line with neither (or
    /// with an <c>error</c>) is never treated as success, and a genuine JSON-RPC error is never
    /// silently coerced into an empty success.</summary>
    private static bool IsWellFormedSuccessResponse(JsonElement response) =>
        !response.TryGetProperty("error", out _)
        && response.TryGetProperty("result", out var result)
        && result.ValueKind == JsonValueKind.Object;

    /// <summary>The most limit-id characters this projection trusts. Generous for a short
    /// provider-assigned identifier, conservative against an oversized or adversarial key ever
    /// reaching API, logs, or UI.</summary>
    private const int MaxLimitIdLength = 64;

    /// <summary>
    /// Projects only the documented, bounded fields this slice displays. Prefers
    /// <c>rateLimitsByLimitId</c> when it is present as a non-empty object — a map from each
    /// bounded, validated limit id to its own snapshot (an object carrying <c>primary</c> and/or
    /// <c>secondary</c>) — over the legacy <c>rateLimits</c> object, which is itself one such
    /// snapshot with no id of its own; never both, so a bucket is never counted twice and the two
    /// views are never combined into one invented aggregate. Every bucket is represented on its
    /// own; a malformed, duplicated, or excessive bucket map fails closed instead of silently
    /// omitting provider-reported buckets. A response that yields no usable window is reported as
    /// <see cref="CodexAccountAllowanceObservation.Unknown"/> rather than an <c>Observed</c>
    /// snapshot with nothing to show.
    /// </summary>
    private static CodexAccountAllowanceObservation ExtractObservation(JsonElement response, DateTimeOffset retrievedAtUtc)
    {
        if (!response.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
        {
            return CodexAccountAllowanceObservation.Unknown;
        }

        var buckets = new List<CodexAllowanceBucket>();

        if (result.TryGetProperty("rateLimitsByLimitId", out var byLimitId)
            && byLimitId.ValueKind != JsonValueKind.Null)
        {
            if (byLimitId.ValueKind != JsonValueKind.Object)
            {
                return CodexAccountAllowanceObservation.Unknown;
            }

            var limitIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in byLimitId.EnumerateObject())
            {
                if (buckets.Count >= MaxBuckets
                    || !IsAcceptedLimitId(entry.Name)
                    || !limitIds.Add(entry.Name)
                    || entry.Value.ValueKind != JsonValueKind.Object)
                {
                    return CodexAccountAllowanceObservation.Unknown;
                }

                buckets.Add(ReadBucket(entry.Name, entry.Value));
            }
        }
        else if (result.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
        {
            buckets.Add(ReadBucket(limitId: null, legacy));
        }

        return buckets.Count == 0 || !buckets.Any(bucket => bucket.Primary is not null || bucket.Secondary is not null)
            ? CodexAccountAllowanceObservation.Unknown
            : new CodexAccountAllowanceObservation(true, retrievedAtUtc, buckets);
    }

    /// <summary>A provider-assigned limit id is untrusted text: bounded in length and restricted
    /// to a safe identifier character set before it is ever kept, so a malformed or adversarial
    /// key can never reach API, logs, or UI.</summary>
    private static bool IsAcceptedLimitId(string limitId) =>
        limitId.Length is > 0 and <= MaxLimitIdLength
        && limitId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static CodexAllowanceBucket ReadBucket(string? limitId, JsonElement snapshot)
    {
        var primary = TryReadWindow(snapshot, "primary");
        var secondary = TryReadWindow(snapshot, "secondary");

        return new CodexAllowanceBucket(limitId, primary, secondary);
    }

    /// <summary>
    /// A window's <c>usedPercent</c> is the one field required for the window to exist at all:
    /// missing or out of the inclusive [0, 100] range means no window, not a guessed or clamped
    /// value. <c>windowDurationMins</c> (nullable integer) and <c>resetsAt</c> (nullable Unix-
    /// seconds integer) are independently optional per the documented response: each is
    /// projected as <see langword="null"/> when absent or reported in a shape this projection
    /// does not trust, without invalidating the rest of the window.
    /// </summary>
    private static CodexAllowanceWindow? TryReadWindow(JsonElement container, string key)
    {
        if (!container.TryGetProperty(key, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!window.TryGetProperty("usedPercent", out var usedPercentElement)
            || usedPercentElement.ValueKind != JsonValueKind.Number
            || !usedPercentElement.TryGetInt32(out var usedPercent)
            || usedPercent < 0
            || usedPercent > 100)
        {
            return null;
        }

        var windowDurationMins = TryReadOptionalNonNegativeInteger(window, "windowDurationMins");
        var resetsAtUtc = TryReadOptionalUnixSecondsInstant(window, "resetsAt");

        return new CodexAllowanceWindow(usedPercent, windowDurationMins, resetsAtUtc);
    }

    /// <summary>Absent, JSON <c>null</c>, or a value this reader does not trust all resolve to
    /// <see langword="null"/> alike — an optional field's own absence and its malformed presence
    /// are indistinguishable to a reader that must not guess either way.</summary>
    private static int? TryReadOptionalNonNegativeInteger(JsonElement container, string key)
    {
        if (!container.TryGetProperty(key, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value) || value < 0)
        {
            return null;
        }

        return value;
    }

    private static DateTimeOffset? TryReadOptionalUnixSecondsInstant(JsonElement container, string key)
    {
        if (!container.TryGetProperty(key, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var unixSeconds))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static async Task WriteLineAsync(Process process, string json, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await process.StandardInput.BaseStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await process.StandardInput.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DrainAndDiscardAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new byte[4096];
            while (await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) > 0)
            {
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    /// <summary>Mirrors <c>CodexProcessInvoker</c>'s own revalidation exactly: never a searched,
    /// invented, or PATH-resolved value — must already be a fully qualified path to an existing
    /// file with no reparse point anywhere from the filesystem root down to it.</summary>
    private static bool IsAcceptableLaunchComponent(string path) =>
        Path.IsPathFullyQualified(path)
        && File.Exists(path)
        && !AgentInvocationScratchDirectory.PathOrAnyAncestorHasReparsePoint(path);

    /// <summary>Mirrors <c>CodexProcessInvoker</c>'s own environment allowlist exactly: only the
    /// non-secret OS/profile-location values required to locate existing local Codex
    /// authentication — never PATH, a provider token, or an arbitrary environment entry.</summary>
    private static Dictionary<string, string> BuildEnvironmentAllowlist()
    {
        var environment = new Dictionary<string, string>();

        var userProfile = System.Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrEmpty(userProfile))
        {
            environment["USERPROFILE"] = userProfile;
        }

        var codexHome = System.Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrEmpty(codexHome))
        {
            environment["CODEX_HOME"] = codexHome;
        }

        return environment;
    }
}
