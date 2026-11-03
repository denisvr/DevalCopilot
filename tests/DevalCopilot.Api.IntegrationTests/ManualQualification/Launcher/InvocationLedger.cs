using System.Text;
using System.Text.Json;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The small durable record of one authorization's two provider allowances. An allowance is one file created exclusively (create-new,
/// flushed to disk) BEFORE its single POST, so a timeout, a crash, a restart or a second launcher can never find it unspent
/// again; nothing here ever deletes or rewrites an entry. The reviewer allowance can be taken only by the session that holds the
/// planner allowance. The ledger lives outside every disposable root and outside the repository, and a new authorization is the
/// only way to reset it.
/// </summary>
public sealed class InvocationLedger(string ledgerDirectory, TimeProvider clock)
{
    public const string DirectoryName = "devalcopilot-qualification-profile-01-lot-b-ledger";
    public const string Authorization = "owner-qualification-profile-01-lot-b-one-planner-one-critical-reviewer";

    /// <summary>The directory of every earlier authorization's spent allowances. Named only so that it can be refused: this launcher
    /// never reads, enumerates, resets or archives it, and no path or authorization switch exists that could point back to it.</summary>
    private const string SpentLegacyDirectoryName = "devalcopilot-manual-qualification-ledger";

    private static readonly AllowanceRole[] Roles = [AllowanceRole.Planner, AllowanceRole.Reviewer];

    public string LedgerDirectory { get; } = GuardDirectory(ledgerDirectory);

    public static InvocationLedger CreateDefault() =>
        new(Path.Combine(Path.GetTempPath(), DirectoryName), TimeProvider.System);

    public bool IsConsumed(AllowanceRole role) => File.Exists(PathOf(role));

    /// <summary>True once either allowance or any process slot of this authorization was consumed. A restart after any consumption is
    /// never supported: there is no refund, archive or re-arm.</summary>
    public bool IsSpent() => Roles.Any(IsConsumed) || HasProcessSlots();

    private static string GuardDirectory(string directory)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        return string.Equals(name, SpentLegacyDirectoryName, StringComparison.OrdinalIgnoreCase)
            ? throw new ArgumentException("The spent allowance directory of an earlier authorization is never used.", nameof(directory))
            : directory;
    }

    private bool HasProcessSlots() =>
        Directory.Exists(LedgerDirectory) && Directory.EnumerateFiles(LedgerDirectory, "admission-*.start").Any();

    public ConsumeOutcome Consume(AllowanceRole role, string sessionId)
    {
        try
        {
            if (IsAlias(LedgerDirectory))
            {
                return ConsumeOutcome.Unwritable;
            }

            Directory.CreateDirectory(LedgerDirectory);
            if (role == AllowanceRole.Reviewer && !IsHeldBy(AllowanceRole.Planner, sessionId))
            {
                return ConsumeOutcome.FirstStageNotHeld;
            }

            var entry = JsonSerializer.Serialize(new
            {
                authorization = Authorization,
                session = sessionId,
                role = role.ToString(),
                consumedAtUtc = clock.GetUtcNow().ToString("O"),
            });
            using var stream = new FileStream(PathOf(role), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(Encoding.UTF8.GetBytes(entry));
            stream.Flush(flushToDisk: true);
            return ConsumeOutcome.Consumed;
        }
        catch (IOException) when (File.Exists(PathOf(role)))
        {
            return ConsumeOutcome.AlreadyConsumed;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ConsumeOutcome.Unwritable;
        }
    }

    public bool IsHeldBy(AllowanceRole role, string sessionId)
    {
        try
        {
            using var entry = JsonDocument.Parse(File.ReadAllText(PathOf(role)));
            return entry.RootElement.TryGetProperty("session", out var session)
                && string.Equals(session.GetString(), sessionId, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsAlias(string directory) =>
        Directory.Exists(directory) && File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint);

    private string PathOf(AllowanceRole role) =>
        Path.Combine(LedgerDirectory, role.ToString().ToLowerInvariant() + ".allowance");
}
