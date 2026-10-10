namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The launcher's check that the host left nothing it started running, whatever the executable is called. Identity is a process
/// id together with its creation time. Before the host starts it records every process already running. While the host runs it
/// samples the table and classifies each new process by walking its ancestry: <c>Owned</c> when the chain reaches this launcher
/// through new, older processes; <c>Unrelated</c> only when the chain positively reaches a pre-existing process whose creation
/// time is known and unchanged and is known to be older than its child (a missing creation time or order is never positive);
/// otherwise <c>Unknown</c> (an orphan whose parent is gone, a parent that is younger than its child or without a known
/// creation time or order, a pre-existing parent id whose identity is unreadable, changed or cannot be verified). An unavailable
/// identity is never a positively different one, nor a match: a pre-existing id with an unreadable creation time is presumed the
/// same process only while its parent id and name are unchanged, and only as an exemption for that entry itself. At the end the
/// proof needs: no remembered or new descendant still alive, no remembered id whose identity can no longer be read, no new
/// process of unknown origin still alive (only a positively unrelated classification is remembered), and no failed or partial
/// table read. Anything else is unproven with a closed reason, and the owned root is then preserved. Nothing is ever killed or
/// adopted here, and an unknown process is not declared owned.
/// </summary>
public sealed class ChildProcessWatch
{
    public static readonly IReadOnlyList<string> ProviderProcessNames = ["node.exe", "codex.exe", "claude.exe"];

    private readonly IProcessTable _table;
    private readonly int _self;
    private readonly IReadOnlyList<string> _orphanNames;
    private readonly List<ProcessEntry> _baseline = [];
    private readonly Dictionary<ProcessKey, ProcessEntry> _owned = [];
    private readonly HashSet<ProcessKey> _unrelated = [];
    private readonly object _gate = new();
    private volatile bool _readFailed;
    private CancellationTokenSource? _sampling;
    private Task? _sampler;

    private ChildProcessWatch(IProcessTable table, int self, IReadOnlyList<string> orphanNames)
    {
        _table = table;
        _self = self;
        _orphanNames = orphanNames;
    }

    private enum Origin
    {
        Owned,
        Unrelated,
        Unknown,
    }

    /// <summary>How an entry relates to the processes recorded at the start. <c>Same</c> needs one known creation time on both
    /// sides. <c>Unverified</c> is the same id with an unreadable creation time on at least one side but an unchanged parent id
    /// and name: a pre-existing process is presumed, never positively known. <c>Ambiguous</c> is the same id with an unreadable
    /// creation time and a changed parent or name, which may be a reused id.</summary>
    private enum Baseline
    {
        Absent,
        Same,
        Unverified,
        Ambiguous,
    }

    public static ChildProcessWatch Begin(
        IProcessTable? table = null,
        int? self = null,
        IReadOnlyList<string>? orphanNames = null)
    {
        var watch = new ChildProcessWatch(
            table ?? new ProcessTable(),
            self ?? Environment.ProcessId,
            orphanNames ?? ProviderProcessNames);
        try
        {
            watch._baseline.AddRange(watch._table.Read());
        }
        catch (ProcessTableException)
        {
            watch._readFailed = true;
        }

        return watch;
    }

    /// <summary>Starts sampling in the background until <see cref="ProveAsync"/> finishes.</summary>
    public void StartSampling(TimeSpan interval)
    {
        _sampling = new CancellationTokenSource();
        var token = _sampling.Token;
        _sampler = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                Sample();
                try
                {
                    await Task.Delay(interval, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });
    }

    /// <summary>Remembers the new descendants alive right now. A failed read is remembered as a gap in the proof.</summary>
    public void Sample()
    {
        try
        {
            Remember(_table.Read());
        }
        catch (ProcessTableException)
        {
            _readFailed = true;
        }
    }

    public async Task<ChildProof> ProveAsync(TimeSpan grace)
    {
        try
        {
            var deadline = DateTime.UtcNow + grace;
            while (true)
            {
                Assessment assessment;
                try
                {
                    assessment = Assess();
                }
                catch (ProcessTableException)
                {
                    return ChildProof.Unproven("EnumerationFailed");
                }

                if (_readFailed)
                {
                    return ChildProof.Unproven("EnumerationFailed");
                }

                if (assessment.IsClear)
                {
                    return ChildProof.Stopped;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    return ChildProof.Unproven(assessment.Reason);
                }

                await Task.Delay(250);
            }
        }
        finally
        {
            await StopSamplingAsync();
        }
    }

    /// <summary>The remembered and current descendants still alive, plus new orphans with a provider-shaped name. Processes whose
    /// origin or identity cannot be resolved are not leftovers here; <see cref="ProveAsync"/> treats them as unproven. Throws
    /// <see cref="ProcessTableException"/> when the table cannot be read completely.</summary>
    public IReadOnlyList<ProcessEntry> Leftovers() => Assess().Leftovers;

    private Assessment Assess()
    {
        var table = _table.Read();
        Remember(table);
        var present = table.Select(entry => entry.Id).ToHashSet();
        var byId = IndexById(table);
        var leftovers = new Dictionary<ProcessKey, ProcessEntry>();
        var unresolved = false;
        var identityUnavailable = false;
        lock (_gate)
        {
            foreach (var owned in _owned.Values)
            {
                foreach (var entry in table.Where(entry => entry.Id == owned.Id))
                {
                    if (Same(entry, owned))
                    {
                        leftovers[new ProcessKey(entry)] = entry;
                    }
                    else if (entry.StartedUtc is null || owned.StartedUtc is null)
                    {
                        identityUnavailable = true;
                    }
                }
            }

            foreach (var entry in table)
            {
                var key = new ProcessKey(entry);
                if (leftovers.ContainsKey(key) || _unrelated.Contains(key) || _owned.ContainsKey(key))
                {
                    continue;
                }

                var baseline = BaselineOf(entry);
                if (baseline == Baseline.Same)
                {
                    continue;
                }

                var origin = Classify(entry, byId);
                if (origin == Origin.Owned
                    || (!present.Contains(entry.ParentId)
                        && _orphanNames.Contains(entry.ExecutableName, StringComparer.OrdinalIgnoreCase)
                        && baseline != Baseline.Unverified))
                {
                    leftovers[key] = entry;
                }
                else if (origin == Origin.Unknown && baseline != Baseline.Unverified)
                {
                    unresolved = true;
                }
            }
        }

        return new Assessment([.. leftovers.Values], unresolved, identityUnavailable);
    }

    private async Task StopSamplingAsync()
    {
        if (_sampling is null)
        {
            return;
        }

        await _sampling.CancelAsync();
        if (_sampler is not null)
        {
            await _sampler;
        }

        _sampling.Dispose();
        _sampling = null;
    }

    private static Dictionary<int, ProcessEntry> IndexById(IReadOnlyList<ProcessEntry> table) =>
        table.GroupBy(entry => entry.Id).ToDictionary(group => group.Key, group => group.First());

    private static bool Same(ProcessEntry left, ProcessEntry right) =>
        left.Id == right.Id && left.StartedUtc == right.StartedUtc;

    private void Remember(IReadOnlyList<ProcessEntry> table)
    {
        var byId = IndexById(table);
        lock (_gate)
        {
            foreach (var entry in table.Where(entry => BaselineOf(entry) != Baseline.Same))
            {
                switch (Classify(entry, byId))
                {
                    case Origin.Owned:
                        _owned[new ProcessKey(entry)] = entry;
                        break;
                    case Origin.Unrelated:
                        _unrelated.Add(new ProcessKey(entry));
                        break;
                }
            }
        }
    }

    /// <summary>Whether the process was already running when the watch began. Only a known, equal creation time is a positive
    /// match. An unreadable creation time is never a positively different one, but it is not a match either: the id is matched
    /// as unverified when its parent and name are unchanged and as ambiguous otherwise.</summary>
    private Baseline BaselineOf(ProcessEntry entry)
    {
        var verdict = Baseline.Absent;
        foreach (var earlier in _baseline.Where(earlier => earlier.Id == entry.Id))
        {
            if (earlier.StartedUtc is not null && earlier.StartedUtc == entry.StartedUtc)
            {
                return Baseline.Same;
            }

            if (earlier.StartedUtc is null || entry.StartedUtc is null)
            {
                var unchanged = earlier.ParentId == entry.ParentId
                    && string.Equals(earlier.ExecutableName, entry.ExecutableName, StringComparison.OrdinalIgnoreCase);
                verdict = unchanged && verdict != Baseline.Ambiguous ? Baseline.Unverified : Baseline.Ambiguous;
            }
        }

        return verdict;
    }

    private Origin Classify(ProcessEntry entry, Dictionary<int, ProcessEntry> byId)
    {
        var child = entry;
        var visited = new HashSet<int> { entry.Id };
        var linkUncertain = false;
        while (true)
        {
            if (!byId.TryGetValue(child.ParentId, out var parent) || !visited.Add(parent.Id))
            {
                return Origin.Unknown;
            }

            var order = Compare(parent, child);
            if (parent.Id == _self)
            {
                return order == Order.Younger ? Origin.Unknown : Origin.Owned;
            }

            switch (BaselineOf(parent))
            {
                case Baseline.Same:
                    // Unrelated only from a known pre-existing parent, a known child time and a known, older order.
                    return order == Order.Older && !linkUncertain ? Origin.Unrelated : Origin.Unknown;
                case Baseline.Unverified:
                case Baseline.Ambiguous:
                    return Origin.Unknown;
            }

            switch (order)
            {
                case Order.Younger:
                    return Origin.Unknown;
                case Order.Unknown:
                    linkUncertain = true;
                    break;
            }

            child = parent;
        }
    }

    private static Order Compare(ProcessEntry parent, ProcessEntry child)
    {
        if (parent.StartedUtc is not { } parentStart || child.StartedUtc is not { } childStart)
        {
            return Order.Unknown;
        }

        return parentStart > childStart ? Order.Younger : Order.Older;
    }

    private enum Order
    {
        Older,
        Younger,
        Unknown,
    }

    private readonly record struct Assessment(
        IReadOnlyList<ProcessEntry> Leftovers,
        bool Unresolved,
        bool IdentityUnavailable)
    {
        public bool IsClear => Leftovers.Count == 0 && !Unresolved && !IdentityUnavailable;

        public string Reason =>
            Leftovers.Count > 0 ? "LeftoverAlive" : IdentityUnavailable ? "IdentityUnavailable" : "OriginUnresolved";
    }

    private readonly record struct ProcessKey(int Id, DateTime? Started)
    {
        public ProcessKey(ProcessEntry entry)
            : this(entry.Id, entry.StartedUtc)
        {
        }
    }
}
