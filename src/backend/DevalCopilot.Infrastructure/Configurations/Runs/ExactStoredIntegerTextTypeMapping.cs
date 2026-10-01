using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

/// <summary>
/// The type mapping of the two Claude turn-limit columns (<c>runs.RequestedClaudeMaxTurns</c> and
/// <c>attempts.AgentRequestedMaxTurns</c>) and of the run execution-mode column (<c>runs.ExecutionMode</c>): an INTEGER-affinity column exposed to the Domain as one string whose form
/// identifies the SQLite storage class, so no non-integer value can be read as a request and no stored value is
/// confused with another. The default string mapping reads with <c>GetString</c>, which silently decodes a BLOB (for
/// example <c>X'37'</c>) into <c>"7"</c> before any validation could see the storage class. The representation is
/// disjoint by construction, because every non-integer class carries a one-letter tag and the tag is never the first
/// character of a canonical integer:
/// <list type="bullet">
/// <item><c>integer</c> is its canonical invariant digits (<c>7</c>, <c>-3</c>): the only form the Domain accepts as a
/// request, and only when it is a whole number from 1 through 100;</item>
/// <item><c>real</c> is <c>r:</c> plus the 16 hex digits of its exact IEEE-754 bits, so every finite value, both
/// infinities, and negative zero are preserved without any decimal rounding;</item>
/// <item><c>blob</c> is <c>b:</c> plus the hex of its exact bytes (empty for an empty BLOB);</item>
/// <item><c>text</c> is <c>t:</c> plus the text verbatim, so an actual TEXT value that merely looks like another form
/// (<c>blob:37</c>, <c>b:37</c>, <c>r:...</c>, digits, or an empty string) is still TEXT;</item>
/// <item>any other storage class is <c>?:</c> plus its type name and cannot be reconstructed.</item>
/// </list>
/// Binding the original value back (the concurrency-token comparison of an UPDATE, or the write of a new value) decodes
/// the tag and binds that exact type and content, never inferring a type from untagged text and never throwing on a
/// malformed suffix: a string that is not a well-formed tagged value or canonical integer is bound as plain text. The
/// SQL literal form uses the same decoding. The mapping applies only to these three columns; it is not a general
/// persistence mechanism.
/// </summary>
public sealed class ExactStoredIntegerTextTypeMapping : RelationalTypeMapping
{
    public const string RealTag = "r:";
    public const string BlobTag = "b:";
    public const string TextTag = "t:";
    public const string OtherTag = "?:";

    private static readonly MethodInfo ReadMethod = typeof(ExactStoredIntegerTextTypeMapping)
        .GetMethod(nameof(Read), BindingFlags.Public | BindingFlags.Static)!;

    public ExactStoredIntegerTextTypeMapping()
        : base(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(string)), "INTEGER", StoreTypePostfix.None, System.Data.DbType.String, unicode: false))
    {
    }

    private ExactStoredIntegerTextTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <summary>Reads the current, non-null value of <paramref name="ordinal"/> by its SQLite storage class.</summary>
    public static string Read(DbDataReader reader, int ordinal)
    {
        var storedType = reader.GetFieldType(ordinal);
        if (storedType == typeof(long))
        {
            return reader.GetInt64(ordinal).ToString(CultureInfo.InvariantCulture);
        }

        if (storedType == typeof(double))
        {
            return RealTag + BitConverter.DoubleToInt64Bits(reader.GetDouble(ordinal)).ToString("X16", CultureInfo.InvariantCulture);
        }

        if (storedType == typeof(string))
        {
            return TextTag + reader.GetString(ordinal);
        }

        if (storedType == typeof(byte[]))
        {
            return BlobTag + Convert.ToHexString(reader.GetFieldValue<byte[]>(ordinal));
        }

        return OtherTag + storedType.Name;
    }

    public override Expression CustomizeDataReaderExpression(Expression expression) =>
        expression is MethodCallExpression { Object: { } reader, Arguments: [var ordinal] }
            ? Expression.Call(ReadMethod, reader, ordinal)
            : throw new InvalidOperationException("Unexpected data reader expression for the turn-limit storage mapping.");

    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);
        if (parameter.Value is not string text)
        {
            return;
        }

        switch (Decode(text))
        {
            case long integer:
                parameter.DbType = System.Data.DbType.Int64;
                parameter.Value = integer;
                break;
            case double real:
                parameter.DbType = System.Data.DbType.Double;
                parameter.Value = real;
                break;
            case byte[] bytes:
                parameter.DbType = System.Data.DbType.Binary;
                parameter.Value = bytes;
                break;
            case string plain:
                parameter.DbType = System.Data.DbType.String;
                parameter.Value = plain;
                break;
        }
    }

    protected override string GenerateNonNullSqlLiteral(object value) => Decode(Convert.ToString(value, CultureInfo.InvariantCulture)!) switch
    {
        long integer => integer.ToString(CultureInfo.InvariantCulture),
        double real => RealLiteral(real),
        byte[] bytes => "X'" + Convert.ToHexString(bytes) + "'",
        string plain => Quote(plain),
        _ => "NULL",
    };

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new ExactStoredIntegerTextTypeMapping(parameters);

    /// <summary>Decodes a stored representation into the exact value to bind: a <see cref="long"/> for canonical
    /// integer text, a <see cref="double"/> for exact-bits real text, <see cref="byte"/>[] for well-formed BLOB hex, and
    /// otherwise a <see cref="string"/> (actual TEXT after its tag, or any untagged or malformed input as is).</summary>
    private static object Decode(string text)
    {
        if (text.StartsWith(TextTag, StringComparison.Ordinal))
        {
            return text[TextTag.Length..];
        }

        if (text.StartsWith(RealTag, StringComparison.Ordinal)
            && text.Length == RealTag.Length + 16
            && long.TryParse(text.AsSpan(RealTag.Length), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var bits))
        {
            return BitConverter.Int64BitsToDouble(bits);
        }

        if (text.StartsWith(BlobTag, StringComparison.Ordinal) && TryDecodeHex(text.AsSpan(BlobTag.Length), out var bytes))
        {
            return bytes;
        }

        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)
            && integer.ToString(CultureInfo.InvariantCulture) == text
                ? integer
                : text;
    }

    private static bool TryDecodeHex(ReadOnlySpan<char> hex, out byte[] bytes)
    {
        var wellFormed = hex.Length % 2 == 0;
        foreach (var character in hex)
        {
            wellFormed &= char.IsAsciiHexDigit(character);
        }

        bytes = wellFormed ? Convert.FromHexString(hex) : [];
        return wellFormed;
    }


    private static string RealLiteral(double real)
    {
        if (double.IsNaN(real))
        {
            return "NULL";
        }

        if (double.IsPositiveInfinity(real))
        {
            return "9e999";
        }

        if (double.IsNegativeInfinity(real))
        {
            return "-9e999";
        }

        var text = real.ToString("R", CultureInfo.InvariantCulture);
        return text.AsSpan().IndexOfAny('.', 'E') >= 0 ? text : text + ".0";
    }

    private static string Quote(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
}
