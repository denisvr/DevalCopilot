using System.Diagnostics;
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

    case "spawn-tree":
        return SpawnTree(int.Parse(args[1]));

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
