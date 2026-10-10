using System.Reflection;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunEventHistory;

/// <summary>The only stored event types this operation discloses: the exact, case-sensitive string constants of
/// <see cref="RunEventType"/>. Nothing else a row stores is ever admitted, so an unknown value keeps its row but is never exposed.</summary>
public static class RunEventHistoryEventTypes
{
    public static readonly IReadOnlyList<string> Known = typeof(RunEventType)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToArray();
}
