using System.Text;
using System.Text.Json;

namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// Finds a duplicated property name anywhere in the parts of an <c>account/rateLimits/read</c> reply the account-usage stop relies on:
/// the reply itself, its <c>result</c>, the legacy <c>rateLimits</c> snapshot, the <c>rateLimitsByLimitId</c> map (a duplicated bucket
/// key), every bucket snapshot, and every <c>primary</c>/<c>secondary</c> window. <see cref="JsonDocument"/> keeps the last of two equal
/// names without saying so, so a duplicate would otherwise let one value silently shadow another in a reply the host then trusts.
/// Property names are compared after unescaping, so <c>"a"</c> and <c>"a"</c> are the same name. Unrelated provider sub-objects
/// (credits, upsell banners, plan data) are not inspected and never influence the policy.
/// </summary>
internal static class CodexRateLimitsDuplicateScan
{
    private enum Kind
    {
        Irrelevant,
        Root,
        Result,
        ByLimit,
        Snapshot,
        Window,
    }

    /// <summary>True when a relevant object repeats a property name, or the text is not the single JSON object it must be.</summary>
    internal static bool HasDuplicate(string rawJson)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(rawJson), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });
        var kinds = new Stack<Kind>();
        var names = new Stack<HashSet<string>?>();
        var pending = Kind.Root;
        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        kinds.Push(pending);
                        names.Push(pending == Kind.Irrelevant ? null : new HashSet<string>(StringComparer.Ordinal));
                        pending = Kind.Irrelevant;
                        break;
                    case JsonTokenType.StartArray:
                        kinds.Push(Kind.Irrelevant);
                        names.Push(null);
                        pending = Kind.Irrelevant;
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        kinds.Pop();
                        names.Pop();
                        pending = Kind.Irrelevant;
                        break;
                    case JsonTokenType.PropertyName:
                        var name = reader.GetString()!;
                        var seen = names.Peek();
                        if (seen is not null && !seen.Add(name))
                        {
                            return true;
                        }

                        pending = Child(kinds.Peek(), name);
                        break;
                    default:
                        pending = Kind.Irrelevant;
                        break;
                }
            }
        }
        catch (JsonException)
        {
            return true;
        }

        return false;
    }

    private static Kind Child(Kind parent, string name) => parent switch
    {
        Kind.Root => name == "result" ? Kind.Result : Kind.Irrelevant,
        Kind.Result => name switch
        {
            "rateLimits" => Kind.Snapshot,
            "rateLimitsByLimitId" => Kind.ByLimit,
            _ => Kind.Irrelevant,
        },
        Kind.ByLimit => Kind.Snapshot,
        Kind.Snapshot => name is "primary" or "secondary" ? Kind.Window : Kind.Irrelevant,
        _ => Kind.Irrelevant,
    };
}
