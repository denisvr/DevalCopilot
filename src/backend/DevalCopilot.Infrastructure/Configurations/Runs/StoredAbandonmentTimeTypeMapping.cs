using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

/// <summary>
/// The provider-side half of the mapping of <c>runs.AbandonedAtUtc</c> (ADR-0031), composed with
/// <see cref="StoredAbandonmentTimeConverter"/>. SQLite's reader decodes a BLOB to text before any converter sees it, so a BLOB that holds
/// the UTF-8 bytes of an exact, admitted timestamp would be indistinguishable from the TEXT that was written. This mapping preserves
/// the actual storage class at the read boundary: only a TEXT value reaches the converter; a value of any other storage class (BLOB,
/// INTEGER or REAL) reads as no value, so the abandonment is incoherent for that run. It reads, never repairs: the stored class and
/// bytes are left exactly as they were unless the row is deliberately rewritten. The mapping is used by this one column only.
/// </summary>
internal sealed class StoredAbandonmentTimeTypeMapping : RelationalTypeMapping
{
    private static readonly MethodInfo ReadMethod = typeof(StoredAbandonmentTimeTypeMapping)
        .GetMethod(nameof(Read), BindingFlags.Public | BindingFlags.Static)!;

    public StoredAbandonmentTimeTypeMapping()
        : base(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(string)), "TEXT", StoreTypePostfix.None, System.Data.DbType.String, unicode: true))
    {
    }

    private StoredAbandonmentTimeTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <summary>The text of the current, non-null value of <paramref name="ordinal"/> when SQLite stores it as TEXT; otherwise
    /// <see langword="null"/>, whatever the bytes of another storage class would decode to.</summary>
    public static string? Read(DbDataReader reader, int ordinal) =>
        reader.GetFieldType(ordinal) == typeof(string) ? reader.GetString(ordinal) : null;

    public override Expression CustomizeDataReaderExpression(Expression expression) =>
        expression is MethodCallExpression { Object: { } reader, Arguments: [var ordinal] }
            ? Expression.Call(ReadMethod, reader, ordinal)
            : throw new InvalidOperationException("Unexpected data reader expression for the abandonment-time storage mapping.");

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new StoredAbandonmentTimeTypeMapping(parameters);

    protected override string GenerateNonNullSqlLiteral(object value) =>
        "'" + Convert.ToString(value, CultureInfo.InvariantCulture)!.Replace("'", "''", StringComparison.Ordinal) + "'";
}
