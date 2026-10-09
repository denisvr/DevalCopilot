using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

/// <summary>
/// The mapping of the one column <c>runs.AbandonedAtUtc</c> (ADR-0031). It writes exactly the text the SQLite provider writes for a
/// <see cref="DateTimeOffset"/> (<c>yyyy-MM-dd HH:mm:ss.FFFFFFFzzz</c>, so stored values are unchanged) and reads that exact form back.
/// A stored value that is not that form (a damaged or hand-edited row: unparsable text, an out-of-range time) reads
/// as <see langword="null"/>; a value that SQLite does not store as TEXT never reaches this converter as text, because
/// <see cref="StoredAbandonmentTimeTypeMapping"/> (its provider-side half) preserves the storage class and reads it as null. A
/// damaged value is never a valid, default or approximate time, and never an exception that would make every read of
/// the run, such as its cockpit, fail. A <see langword="null"/> abandonment time is never coherent, so the abandonment facts stay
/// incoherent per run. The mapping applies only to this column; it is not a general persistence mechanism and no older timestamp
/// mapping uses it.
/// </summary>
internal sealed class StoredAbandonmentTimeConverter()
    : ValueConverter<DateTimeOffset?, string?>(model => Write(model), provider => Read(provider))
{
    private const string WrittenFormat = "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz";

    private const string WrittenFormatWithoutFraction = "yyyy-MM-dd HH:mm:sszzz";

    private static readonly string[] ReadFormats = [WrittenFormat, WrittenFormatWithoutFraction];

    internal static string? Write(DateTimeOffset? value) => value?.ToString(WrittenFormat, CultureInfo.InvariantCulture);

    internal static DateTimeOffset? Read(string? stored) =>
        stored is not null
        && DateTimeOffset.TryParseExact(stored, ReadFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;
}
