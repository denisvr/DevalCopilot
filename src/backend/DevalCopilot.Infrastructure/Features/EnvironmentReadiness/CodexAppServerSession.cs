using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// The shared, bounded local Codex App Server duplex JSON-RPC session lifecycle every read-only
/// App Server observation adapter in this application reuses: launch-target revalidation,
/// no-shell/no-PATH-search argument passing, the restricted <c>USERPROFILE</c>/<c>CODEX_HOME</c>-
/// only environment allowlist, the explicit <c>--stdio</c> argument, the required
/// <c>initialize</c>/<c>initialized</c> handshake, a finite timeout, bounded output capture,
/// cancellation propagation, and process-tree cleanup. Narrowly extracted out of the original
/// <c>CodexAccountAllowanceAdapter</c> so a second read-only App Server observation (the Codex
/// model/reasoning-effort catalog) never has to copy this contract; only the request(s) exchanged
/// after the handshake, and their response parsing, vary by caller. This never becomes a
/// general-purpose App Server RPC escape hatch: it still only ever performs the caller-supplied,
/// already-bounded exchange, and it never sends a mutating or thread/turn method itself.
/// </summary>
internal static class CodexAppServerSession
{
    private const int InitializeRequestId = 0;

    /// <summary>A bounded ceiling on how many times a caller asks the stream for another chunk
    /// while waiting for one correlated response — a safety net against a pathological peer that
    /// keeps sending a trickle of unrelated notifications forever without ever exhausting the
    /// byte budget in one read.</summary>
    internal const int MaxFillAttempts = 256;

    internal static readonly TimeSpan DefaultInvocationTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PostKillWait = TimeSpan.FromSeconds(5);

    private const string InitializeRequestJson =
        """{"method":"initialize","id":0,"params":{"clientInfo":{"name":"DevalCopilot","title":"DevalCopilot","version":"1.0.0"},"capabilities":{}}}""";

    private const string InitializedNotificationJson = """{"method":"initialized","params":{}}""";

    /// <summary>
    /// Launches the App Server, performs the required handshake, invokes
    /// <paramref name="exchangeAfterHandshake"/> for the caller-specific read-only exchange, then
    /// unconditionally terminates the process it started. Every failure mode — an unacceptable
    /// launch target, a scratch-directory failure, a process-start failure, a failed handshake, a
    /// non-zero exit, this session's own internal timeout, or a failed process-tree cleanup —
    /// resolves to <paramref name="unknownResult"/>; only the caller's own cancellation is
    /// rethrown, always after cleanup has been attempted.
    /// </summary>
    internal static async Task<T> RunAsync<T>(
        string executablePath,
        string? scriptPath,
        TimeSpan invocationTimeout,
        int maxTotalCapturedBytes,
        int maxLineBytes,
        T unknownResult,
        Func<CodexAppServerChannel, CancellationToken, Task<T>> exchangeAfterHandshake,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsAcceptableLaunchComponent(executablePath) || (scriptPath is not null && !IsAcceptableLaunchComponent(scriptPath)))
        {
            return unknownResult;
        }

        string workingDirectory;
        try
        {
            workingDirectory = HostScratchDirectory.EnsureExists();
            if (AgentInvocationScratchDirectory.PathOrAnyAncestorHasReparsePoint(workingDirectory))
            {
                return unknownResult;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return unknownResult;
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
            return unknownResult;
        }

        using var timeoutSource = new CancellationTokenSource(invocationTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        // Never surfaced, never trusted for anything — only drained so the child can never
        // block on a full stderr pipe while this adapter is reading stdout.
        var stderrDrain = DrainAndDiscardAsync(process.StandardError.BaseStream, linkedSource.Token);

        var result = unknownResult;
        var cleanupSucceeded = false;
        try
        {
            var scanner = new BoundedJsonLineScanner(process.StandardOutput.BaseStream, maxTotalCapturedBytes, maxLineBytes);

            await WriteLineAsync(process, InitializeRequestJson, linkedSource.Token).ConfigureAwait(false);
            var initializeResponse =
                await ReadCorrelatedResponseAsync(scanner, InitializeRequestId, linkedSource.Token).ConfigureAwait(false);

            if (initializeResponse is { } initializeElement && IsWellFormedSuccessResponse(initializeElement))
            {
                await WriteLineAsync(process, InitializedNotificationJson, linkedSource.Token).ConfigureAwait(false);
                var channel = new CodexAppServerChannel(process, scanner);
                result = await exchangeAfterHandshake(channel, linkedSource.Token).ConfigureAwait(false);
            }

            // A peer that already exited with an error is not a successful observation, even if
            // it wrote a plausible response just before failing.
            if (process.HasExited && process.ExitCode != 0)
            {
                result = unknownResult;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // Only this session's own internal timeout can reach here (the caller's token is
            // re-checked and rethrown above) — a timeout is a safe, closed result, never an
            // exception the caller must handle.
            result = unknownResult;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            result = unknownResult;
        }
        finally
        {
            cleanupSucceeded = await StopOwnedProcessAsync(process, linkedSource, stderrDrain).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return cleanupSucceeded ? result : unknownResult;
    }

    private static async Task<bool> StopOwnedProcessAsync(Process process, CancellationTokenSource linkedSource, Task stderrDrain)
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
    /// object with a different id or a notification with no id is ignored; malformed JSONL fails
    /// the exchange closed. If more than one distinct reply for the same id is observed in the
    /// lines already available before this method decides, neither is trusted: a single request
    /// has exactly one true reply, so disagreement means the value cannot be trusted. Returns
    /// <see langword="null"/> when the stream is exhausted (end of stream or the total capture
    /// budget is spent) before any reply for this id arrives.
    /// </summary>
    internal static async Task<JsonElement?> ReadCorrelatedResponseAsync(
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
    internal static bool IsWellFormedSuccessResponse(JsonElement response) =>
        !response.TryGetProperty("error", out _)
        && response.TryGetProperty("result", out var result)
        && result.ValueKind == JsonValueKind.Object;

    internal static async Task WriteLineAsync(Process process, string json, CancellationToken cancellationToken)
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
