namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

internal static class RowExtensions
{
    /// <summary>The row with one member replaced (the raw-insert builders return a fresh dictionary each time).</summary>
    public static Dictionary<string, string> With(this Dictionary<string, string> row, string key, string value)
    {
        row[key] = value;
        return row;
    }
}
