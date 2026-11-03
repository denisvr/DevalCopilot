using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

// A deterministic executable test double for DevalCopilot.Infrastructure.IntegrationTests: it
// never behaves differently across environments or runs, so tests assert on exact captured
// output, exit codes, and timing rather than tolerating a real tool's variability.
if (args.Length == 0)
{
    await Console.Error.WriteLineAsync("Usage: ProcessExecutionFixture <mode> [args...]");
    return 64;
}

// Interpreter-style launch: when the first argument is an existing "*.fixture-script" file, the
// mode and its arguments are read from that file and every remaining argument is ignored. This
// lets an adapter with a fixed provider argument contract (e.g. a script launch target followed by
// provider flags) still drive a deterministic mode such as sleep-ms or exit-code.
if (args[0].EndsWith(".fixture-script", StringComparison.OrdinalIgnoreCase) && File.Exists(args[0]))
{
    args = File.ReadAllText(args[0]).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

// A second, distinct interpreter-style launch matching the Node-script launch shape exactly:
// argument 0 is a script file path, followed by fixed "app-server --stdio" arguments the caller
// always passes alongside it. This lets a duplex JSON-RPC adapter test drive a fully scripted,
// per-test-unique interaction — reading N lines of whatever the adapter under test wrote, then
// writing back an exact scripted reply — without any special-cased argument the adapter itself
// would never actually pass in production.
if (args.Length == 3 && args[1] == "app-server" && args[2] == "--stdio" && File.Exists(args[0]))
{
    return await RunAppServerScriptAsync(args[0]);
}

// The launcher's Git version observation double: a copy of this executable that receives exactly "--version" and finds its scripted
// behavior in a file beside it (the executable's own name with the ".observation" extension). Without that file the argument stays an
// unknown mode, exactly as before.
if (args.Length == 1 && args[0] == "--version" && Environment.ProcessPath is { } own
    && File.Exists(Path.ChangeExtension(own, ".observation")))
{
    return RunVersionObservation(Path.ChangeExtension(own, ".observation"));
}

switch (args[0])
{
    case "exit-code":
        return int.Parse(args[1]);

    case "echo-args":
        // Printed as one JSON line rather than one line per argument, so an argument
        // containing embedded whitespace, quotes, or a newline still round-trips exactly.
        Console.WriteLine(JsonSerializer.Serialize(args[1..]));
        return 0;

    case "sleep-ms":
        Thread.Sleep(int.Parse(args[1]));
        return 0;

    case "write-handle-then-sleep":
        // Used only by the "overflow-held" version observation double: after a short pause this descendant writes the given number of
        // bytes straight to an inherited handle (the numeric value its parent passed), then sleeps holding every inherited handle.
        Thread.Sleep(300);
        NativeHandles.WriteTo(new IntPtr(long.Parse(args[1])), int.Parse(args[2]));
        Thread.Sleep(int.Parse(args[3]));
        return 0;

    case "sleep-after-marker":
        // Writes its marker (a path relative to the working directory) before sleeping, so a test can request cancellation only
        // once this real child process has demonstrably started instead of racing a wall-clock timer against the caller's own
        // preparation work.
        File.WriteAllText(args[1], Environment.ProcessId.ToString());
        Thread.Sleep(int.Parse(args[2]));
        return 0;

    case "write-bytes":
        WriteBytes(Console.OpenStandardOutput(), int.Parse(args[1]));
        WriteBytes(Console.OpenStandardError(), int.Parse(args[2]));
        return 0;

    case "write-then-sleep-then-write":
        // Lets a test observe genuinely partial stdout (the process is still alive and has
        // not exited) before the remainder arrives, rather than racing a fixture that writes
        // everything and exits near-instantly.
        WriteBytes(Console.OpenStandardOutput(), int.Parse(args[1]));
        Console.Out.Flush();
        Thread.Sleep(int.Parse(args[2]));
        WriteBytes(Console.OpenStandardOutput(), int.Parse(args[3]));
        return 0;

    case "print-env":
        Console.WriteLine(Environment.GetEnvironmentVariable(args[1]) ?? "<unset>");
        return 0;

    case "auth":
        // The isolated GitHub CLI double: reads the scripted behavior from the working directory, records the argv and
        // environment it was launched with, and answers like a (filtered) `gh auth status`. No account, credential or network.
        return await RunGitHubAuthDoubleAsync(args, "gh-auth");

    case "api":
        // The same isolated GitHub CLI double for the repository identity query (`gh api graphql ...`): its own scripted behavior and
        // invocation record, no account, credential or network.
        return await RunGitHubAuthDoubleAsync(args, "gh-api");

    case "spawn-tree":
        return SpawnTree(int.Parse(args[1]));

    case "orphan-holds-pipes":
        // ADR-0072: writes a marker line, starts a descendant that inherits the selected redirected output handle(s) and sleeps,
        // records the descendant's process id in a file, then exits zero at once. The parent's exit is observed normally while the
        // inherited pipe never reaches end-of-file, so only a bounded I/O completion window can end the capture.
        return OrphanHoldsPipes(args[1], args[2], int.Parse(args[3]));

    case "append-marker":
        // One line appended per invocation — lets a test count real executions rather than
        // inferring it from state that a single run could also produce.
        File.AppendAllText(args[1], Guid.NewGuid() + Environment.NewLine);
        return 0;

    case "echo-stdin":
        // Reads standard input through to its natural end and writes the exact same bytes back
        // to stdout — lets a test assert byte-for-byte stdin transmission (including non-ASCII
        // UTF-8 content and literal shell metacharacters) without this fixture ever interpreting
        // the bytes as anything but opaque data.
        await EchoStandardInputAsync();
        return 0;

    case "sleep-then-echo-stdin":
        // Sleeps before touching standard input at all, so a caller writing a large payload can
        // observe the write genuinely still in flight (blocked on the OS pipe buffer) for the
        // whole sleep duration — used to test cancellation/timeout while a stdin write is still
        // in progress.
        Thread.Sleep(int.Parse(args[1]));
        await EchoStandardInputAsync();
        return 0;

    default:
        await Console.Error.WriteLineAsync($"Unknown mode: {args[0]}");
        return 64;
}

static int RunVersionObservation(string scriptPath)
{
    const string Version = "git version 2.50.0\n";
    var parts = File.ReadAllText(scriptPath).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    switch (parts[0])
    {
        case "normal":
            Console.Out.Write(Version);
            return 0;

        case "stdout-bytes":
            // A valid version line followed by that many further standard output bytes.
            Console.Out.Write(Version);
            Console.Out.Flush();
            WriteBytes(Console.OpenStandardOutput(), int.Parse(parts[1]));
            return 0;

        case "stderr-bytes":
            // A valid version line, then that many standard error bytes.
            Console.Out.Write(Version);
            Console.Out.Flush();
            WriteBytes(Console.OpenStandardError(), int.Parse(parts[1]));
            return 0;

        case "delay-ms":
            Thread.Sleep(int.Parse(parts[1]));
            Console.Out.Write(Version);
            return 0;

        case "exit-code":
            Console.Out.Write(Version);
            return int.Parse(parts[1]);

        case "overflow-held":
            // The version line, then this process exits zero at once while a descendant that inherited BOTH redirected streams
            // sleeps holding them and, shortly after, writes the given number of bytes to standard error: an overflow on one stream
            // while the other stays open after the root exited (parts: pid file, bytes, sleep milliseconds).
            Console.Out.Write(Version);
            Console.Out.Flush();
            var overflowStart = new ProcessStartInfo { FileName = Environment.ProcessPath!, UseShellExecute = false, CreateNoWindow = true };
            overflowStart.ArgumentList.Add("write-handle-then-sleep");
            overflowStart.ArgumentList.Add(NativeHandles.StandardHandleValue(NativeHandles.StandardErrorHandle).ToString());
            overflowStart.ArgumentList.Add(parts[2]);
            overflowStart.ArgumentList.Add(parts[3]);
            using (var overflowing = Process.Start(overflowStart)!)
            {
                File.WriteAllText(parts[1], overflowing.Id.ToString());
            }

            return 0;

        case "hold":
            // The version line, then a descendant that inherits ONLY the named redirected stream (the other one is made
            // non-inheritable first) and sleeps, its process id recorded; this process exits zero at once. The held stream never
            // reaches end-of-file while the other one does, so each stream's completion is independently observable.
            Console.Out.Write(Version);
            Console.Out.Flush();
            NativeHandles.DisableInheritance(parts[1] == "stdout" ? NativeHandles.StandardErrorHandle : NativeHandles.StandardOutputHandle);
            var startInfo = new ProcessStartInfo { FileName = Environment.ProcessPath!, UseShellExecute = false, CreateNoWindow = true };
            startInfo.ArgumentList.Add("sleep-ms");
            startInfo.ArgumentList.Add(parts[3]);
            using (var descendant = Process.Start(startInfo)!)
            {
                File.WriteAllText(parts[2], descendant.Id.ToString());
            }

            return 0;

        default:
            return 64;
    }
}

static void WriteBytes(Stream stream, int count)
{
    var buffer = new byte[8192];
    Array.Fill(buffer, (byte)'A');

    var remaining = count;
    while (remaining > 0)
    {
        var chunk = Math.Min(buffer.Length, remaining);
        stream.Write(buffer, 0, chunk);
        remaining -= chunk;
    }

    stream.Flush();
}

static async Task EchoStandardInputAsync()
{
    await using var input = Console.OpenStandardInput();
    await using var output = Console.OpenStandardOutput();
    await input.CopyToAsync(output);
}

static async Task<int> RunAppServerScriptAsync(string scriptPath)
{
    var script = JsonSerializer.Deserialize<AppServerScript>(File.ReadAllText(scriptPath))
        ?? throw new InvalidOperationException($"Empty or unreadable app-server script at '{scriptPath}'.");

    // Written before anything else, so a test that expects this process to be killed (a
    // timeout or cancellation) can independently confirm the real OS process actually exited,
    // rather than only observing that the parent adapter's own call returned.
    if (script.PidFilePath is not null)
    {
        File.WriteAllText(script.PidFilePath, Environment.ProcessId.ToString());
    }

    if (script.ChildPidFilePath is not null)
    {
        if (script.ChildHoldsOutputOnly)
        {
            // Windows lets every inheritable handle of this process reach the descendant, so this process's own standard error
            // handle is made non-inheritable first; only standard output then stays open in the descendant.
            NativeHandles.DisableInheritance(NativeHandles.StandardErrorHandle);
        }

        var childStart = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath!,
            UseShellExecute = false,
            CreateNoWindow = true,
            // A descendant that keeps only this process's standard output open (its own standard error is a private pipe) lets a test
            // prove that a stdout stream which never reaches EOF is not treated as confirmed.
            RedirectStandardError = script.ChildHoldsOutputOnly,
        };
        childStart.ArgumentList.Add("sleep-ms");
        childStart.ArgumentList.Add("30000");
        using var child = Process.Start(childStart)!;
        File.WriteAllText(script.ChildPidFilePath, child.Id.ToString());
    }

    using var input = Console.OpenStandardInput();
    using var reader = new StreamReader(input);
    var output = Console.OpenStandardOutput();

    foreach (var step in script.Steps)
    {
        for (var line = 0; line < step.ReadLines; line++)
        {
            var requestLine = await reader.ReadLineAsync();
            if (script.RequestLogPath is not null && requestLine is not null)
            {
                File.AppendAllText(script.RequestLogPath, requestLine + "\n");
            }
        }

        if (step.SleepMs > 0)
        {
            await Task.Delay(step.SleepMs);
        }

        // Combined into a single buffer and written with exactly one WriteAsync call: this
        // fixture's redirected stdout stream performs its own underlying I/O per call rather
        // than buffering until Flush, so writing each scripted line separately could deliver
        // them to the parent's reader as more than one OS-level read — deterministically
        // scripting "more than one reply arrives together" (e.g. a conflicting duplicate)
        // requires they leave this process as one write.
        using var buffer = new MemoryStream();
        if (step.WriteLines is not null)
        {
            foreach (var line in step.WriteLines)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(line + "\n");
                buffer.Write(bytes);
            }
        }

        if (step.WriteRaw is not null)
        {
            buffer.Write(System.Text.Encoding.UTF8.GetBytes(step.WriteRaw));
        }

        if (step.WriteBase64 is not null)
        {
            buffer.Write(Convert.FromBase64String(step.WriteBase64));
        }

        if (buffer.Length > 0)
        {
            await output.WriteAsync(buffer.ToArray());
        }

        await output.FlushAsync();

        if (step.WriteStderr is not null)
        {
            var errorBytes = System.Text.Encoding.UTF8.GetBytes(step.WriteStderr);
            await using var error = Console.OpenStandardError();
            await error.WriteAsync(errorBytes);
            await error.FlushAsync();
        }
    }

    if (script.ThenHang)
    {
        await Task.Delay(Timeout.Infinite);
    }

    if (script.ThenWaitForEof)
    {
        // Mirrors a child such as `git update-ref --stdin`, which only exits once the parent closes the actual pipe.
        while (await reader.ReadLineAsync() is not null)
        {
        }
    }

    return script.ExitCode;
}

static async Task<int> RunGitHubAuthDoubleAsync(string[] arguments, string prefix)
{
    var behavior = JsonSerializer.Deserialize<GitHubAuthBehavior>(File.ReadAllText(prefix + "-behavior.json"))
        ?? throw new InvalidOperationException("Empty gh auth behavior.");

    var environment = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
    {
        environment[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
    }

    File.WriteAllText(prefix + "-invocation.json", JsonSerializer.Serialize(new { arguments, environment, Environment.ProcessId }));
    if (behavior.PidFile is not null)
    {
        File.WriteAllText(behavior.PidFile, Environment.ProcessId.ToString());
    }

    if (behavior.SleepMs > 0)
    {
        await Task.Delay(behavior.SleepMs);
    }

    await using var output = Console.OpenStandardOutput();
    if (behavior.Stdout is not null)
    {
        await output.WriteAsync(System.Text.Encoding.UTF8.GetBytes(behavior.Stdout));
    }

    if (behavior.StdoutBytes > 0)
    {
        WriteBytes(output, behavior.StdoutBytes);
    }

    await output.FlushAsync();
    if (behavior.Stderr is not null)
    {
        await using var error = Console.OpenStandardError();
        await error.WriteAsync(System.Text.Encoding.UTF8.GetBytes(behavior.Stderr));
    }

    return behavior.ExitCode;
}

static int SpawnTree(int sleepMilliseconds)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = Environment.ProcessPath!,
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add("sleep-ms");
    startInfo.ArgumentList.Add(sleepMilliseconds.ToString());

    using var child = Process.Start(startInfo)!;

    // Flushed immediately so the parent adapter's bounded capture already has this line even
    // if the whole tree is killed for a timeout before the child (or this parent) would
    // otherwise have exited on its own.
    Console.WriteLine(JsonSerializer.Serialize(new { parentPid = Environment.ProcessId, childPid = child.Id }));
    Console.Out.Flush();

    child.WaitForExit();
    return 0;
}

static int OrphanHoldsPipes(string heldStreams, string descendantPidFile, int sleepMilliseconds)
{
    Console.Out.Write("parent-marker\n");
    Console.Out.Flush();
    Console.Error.Write("parent-error-marker\n");
    Console.Error.Flush();

    if (heldStreams == "stdout")
    {
        NativeHandles.DisableInheritance(NativeHandles.StandardErrorHandle);
    }
    else if (heldStreams == "stderr")
    {
        NativeHandles.DisableInheritance(NativeHandles.StandardOutputHandle);
    }

    var startInfo = new ProcessStartInfo
    {
        FileName = Environment.ProcessPath!,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    startInfo.ArgumentList.Add("sleep-ms");
    startInfo.ArgumentList.Add(sleepMilliseconds.ToString());

    using var descendant = Process.Start(startInfo)!;
    File.WriteAllText(descendantPidFile, descendant.Id.ToString());
    return 0;
}

internal static class NativeHandles
{
    internal const int StandardOutputHandle = -11;

    internal const int StandardErrorHandle = -12;

    private const uint HandleFlagInherit = 1;

    internal static long StandardHandleValue(int standardHandle) => GetStdHandle(standardHandle).ToInt64();

    internal static void WriteTo(IntPtr handle, int count)
    {
        var bytes = new byte[count];
        Array.Fill(bytes, (byte)'E');
        _ = WriteFile(handle, bytes, count, out _, IntPtr.Zero);
    }

    internal static void DisableInheritance(int standardHandle)
    {
        var handle = GetStdHandle(standardHandle);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1) || !SetHandleInformation(handle, HandleFlagInherit, 0))
        {
            throw new InvalidOperationException("The standard handle could not be made non-inheritable.");
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(IntPtr handle, byte[] buffer, int count, out int written, IntPtr overlapped);
}

/// <summary>One scripted App Server interaction, deserialized from a per-test JSON fixture file.
/// <see cref="AppServerStep.ReadLines"/> lines are consumed from stdin (never validated — the
/// calling test already controls exactly what the adapter under test writes) before
/// <see cref="AppServerStep.WriteLines"/> (each with an appended newline) and then
/// <see cref="AppServerStep.WriteRaw"/> (written verbatim, no appended newline — used to produce
/// a deliberately unterminated or oversized "line") are written back.</summary>
internal sealed record AppServerScript(
    List<AppServerStep> Steps, bool ThenHang = false, string? PidFilePath = null,
    string? ChildPidFilePath = null, string? RequestLogPath = null, bool ThenWaitForEof = false, int ExitCode = 0,
    bool ChildHoldsOutputOnly = false);

/// <summary><see cref="WriteBase64"/> adds exact bytes (for invalid UTF-8 or control bytes a JSON string cannot carry) and
/// <see cref="WriteStderr"/> writes to the standard error stream after that step's standard output.</summary>
internal sealed record AppServerStep(
    int ReadLines, List<string>? WriteLines = null, string? WriteRaw = null, int SleepMs = 0,
    string? WriteBase64 = null, string? WriteStderr = null);

/// <summary>The scripted answer of the isolated GitHub CLI double, deserialized from <c>gh-auth-behavior.json</c>.</summary>
internal sealed record GitHubAuthBehavior(
    string? Stdout = null, string? Stderr = null, int ExitCode = 0, int SleepMs = 0, int StdoutBytes = 0, string? PidFile = null);
