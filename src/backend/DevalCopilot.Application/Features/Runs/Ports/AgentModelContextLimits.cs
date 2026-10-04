using System.Collections.Immutable;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Provider-neutral, provider-reported model context limits for one Agent invocation, shared by every Agent adapter port
/// regardless of provider: the model identifiers the provider listed in its own result, each with the context-window and
/// maximum-output token limits it reported. An adapter constructs it only from a report it parsed through a proven,
/// versioned provider contract (<see cref="Source"/> names that contract) and admits the whole report or none of it;
/// it is never derived from a requested model, a default, a catalog, token totals, or another provider's fields, and it
/// never keeps a valid subset of a malformed report. It is historical observation, never remaining context,
/// next-invocation capacity, a live capability, or proof that a listed model was used, and it never carries a path,
/// argument, environment value, output, session identifier, or credential.
///
/// The value owns an immutable snapshot of what it was given. Construction copies the caller's entries into storage nobody else
/// can reach, so a collection the caller keeps cannot change the report afterwards, and <see cref="Models"/> cannot be written
/// through. Counted collections exceeding <see cref="AgentModelContextLimitsEvidence.MaxModels"/> are refused without reading
/// entries. An uncounted sequence is read only to its first excess entry, enough for recording to refuse it whole. A null or
/// excessive counted collection becomes an empty snapshot; null entries remain null so recording can return its fixed error.
/// </summary>
public sealed class AgentModelContextLimits
{
    /// <summary>The most entries a snapshot ever holds: the evidence bound plus one, so an excess is visible but never copied whole.</summary>
    public const int MaxCopiedEntries = AgentModelContextLimitsEvidence.MaxModels + 1;

    public AgentModelContextLimits(string source, IEnumerable<AgentModelContextLimitEntry?>? models)
    {
        Source = source;
        Models = Snapshot(models);
    }

    /// <summary>The parsing-contract tag of the provider contract the report came through.</summary>
    public string Source { get; }

    /// <summary>The listed models in the order reported, as an immutable snapshot of at most <see cref="MaxCopiedEntries"/> entries;
    /// an entry is null only when the supplied collection held a null.</summary>
    public ImmutableArray<AgentModelContextLimitEntry?> Models { get; }

    private static ImmutableArray<AgentModelContextLimitEntry?> Snapshot(IEnumerable<AgentModelContextLimitEntry?>? models)
    {
        if (models is null)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<AgentModelContextLimitEntry?>();
        if (models is IReadOnlyList<AgentModelContextLimitEntry?> list)
        {
            var count = list.Count;
            if (count <= 0 || count > AgentModelContextLimitsEvidence.MaxModels)
            {
                return [];
            }

            for (var index = 0; index < count; index++)
            {
                builder.Add(list[index]);
            }
        }
        else
        {
            foreach (var model in models)
            {
                builder.Add(model);
                if (builder.Count == MaxCopiedEntries)
                {
                    break;
                }
            }
        }

        return builder.ToImmutable();
    }
}
