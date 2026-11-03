using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The durable process counters of one authorization. Admitting a process creates one marker file exclusively (create-new,
/// flushed) BEFORE the process is created, first in its class and then in the authorization's total, so an interruption, a crash
/// or a second launcher can never find a spent slot unspent. Nothing here deletes, rewrites, refunds or re-arms a marker, and a
/// refused request never consumes more than the slot it already took. The directory belongs to exactly one authorization and is
/// never an old ledger.
/// </summary>
public sealed partial class AdmissionLedger(string ledgerDirectory, string authorization, TimeProvider clock)
{
    public const string TotalName = "total";

    public string LedgerDirectory { get; } = ledgerDirectory;

    public string Authorization { get; } = authorization;

    /// <summary>Takes the next slot of <paramref name="className"/> and of the total. Returns null when both were consumed, or the
    /// closed refusal code. A taken class slot is never returned when the total refuses.</summary>
    public string? TryAdmit(string className, int classLimit, int totalLimit)
    {
        if (!ClassName().IsMatch(className) || className == TotalName || classLimit < 1 || totalLimit < 1)
        {
            return "InventoryInvalid";
        }

        try
        {
            if (IsAlias(LedgerDirectory))
            {
                return "LedgerUnwritable";
            }

            Directory.CreateDirectory(LedgerDirectory);
            if (!TakeSlot(className, classLimit))
            {
                return "ClassLimit";
            }

            return TakeSlot(TotalName, totalLimit) ? null : "TotalLimit";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "LedgerUnwritable";
        }
    }

    /// <summary>The number of consumed slots per class (the total is under <c>total</c>), from the markers of this authorization.</summary>
    public IReadOnlyDictionary<string, int> Consumed()
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (!Directory.Exists(LedgerDirectory))
        {
            return counts;
        }

        foreach (var file in Directory.EnumerateFiles(LedgerDirectory, "admission-*.start"))
        {
            var match = MarkerFile().Match(Path.GetFileName(file));
            if (match.Success)
            {
                var name = match.Groups["class"].Value;
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        }

        return counts;
    }

    public bool AnyConsumed() => Consumed().Count > 0;

    private bool TakeSlot(string className, int limit)
    {
        for (var number = 1; number <= limit; number++)
        {
            if (TryCreate(className, number))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryCreate(string className, int number)
    {
        var entry = JsonSerializer.Serialize(new
        {
            authorization = Authorization,
            slot = className,
            number,
            consumedAtUtc = clock.GetUtcNow().ToString("O"),
        });
        try
        {
            var path = Path.Combine(LedgerDirectory, $"admission-{className}-{number}.start");
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(Encoding.UTF8.GetBytes(entry));
            stream.Flush(flushToDisk: true);
            return true;
        }
        catch (IOException) when (File.Exists(Path.Combine(LedgerDirectory, $"admission-{className}-{number}.start")))
        {
            return false;
        }
    }

    private static bool IsAlias(string directory) =>
        Directory.Exists(directory) && File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint);

    [GeneratedRegex("^[a-z0-9.]{1,48}$")]
    private static partial Regex ClassName();

    [GeneratedRegex("^admission-(?<class>[a-z0-9.]{1,48})-[0-9]{1,4}\\.start$")]
    private static partial Regex MarkerFile();
}
