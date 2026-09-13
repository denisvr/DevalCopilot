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
