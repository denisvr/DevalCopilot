namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The qualification-only admission boundary every host-issued and launcher-issued process request passes before its process is
/// created: the request must match exactly one listed class, every approved file it launches must still be exactly its pinned
/// identity, the class and the authorization total must still have a durable slot, and only then is the slot consumed. An unlisted,
/// ambiguous, drifted or exhausted request is refused with a closed code and creates no process; the refusal is counted and never
/// retried or relabelled.
/// The gate is terminal: the first refusal, an explicit <see cref="Close"/>, or the whole-entry deadline closes it atomically with the
/// decision that caused it. The first reason is preserved, no later request of any producer, including a background refresh or a
/// detached continuation, is ever admitted, and consumed slots are never refunded. The deadline is rechecked after every step that can
/// block (identity verification, durable marker I/O) and again at the dispatch handoff (<see cref="ConfirmOpen"/>): a request whose
/// slot was already consumed when the gate closed keeps its marker, is counted as <see cref="ExpiredBeforeStartCode"/> and never
/// starts. A closed gate also leaves the final source inspection unavailable; that is reported as unproven, never as unchanged.
/// Progress is read from an immutable snapshot published after every state change, so a reader (the deadline waiter above all) never
/// waits for the lock that is held during hashing and marker I/O; a request in flight is visible as pending before it blocks.
/// The boundary changes no production behavior: it only decides whether the unchanged production request may proceed.
/// </summary>
public sealed class ProcessAdmission(
    IReadOnlyList<InventoryEntry> entries,
    int totalLimit,
    AdmissionLedger ledger,
    Func<ProcessShape, string?>? revalidate = null,
    Func<string?>? externalClosure = null)
{
    public const string ClosedCode = "AdmissionClosed";
    public const string ExpiredBeforeStartCode = "ExpiredBeforeStart";

    private readonly object _gate = new();
    private readonly SortedDictionary<string, int> _admitted = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _refused = new(StringComparer.Ordinal);
    private readonly List<string> _pending = [];
    private string? _closedBy;
    private ProcessAdmissionFacts _snapshot = ProcessAdmissionFacts.None;

    /// <summary>Supplies the owner-approved absolute Git file the launcher itself runs (the host runs the same one). It is read at
    /// each launcher Git command, so an approval made after construction is seen; nothing approved means nothing is runnable.</summary>
    public Func<string?>? LauncherGit { get; init; }

    public string? LauncherGitPath => LauncherGit?.Invoke();

    public IReadOnlyList<InventoryEntry> Entries => entries;

    public int TotalLimit => totalLimit;

    public AdmissionLedger Ledger => ledger;

    public bool IsClosed => Facts.ClosedBy is not null;

    /// <summary>The coherent progress of the boundary: starts admitted, refusals, the first closing reason and the requests still in
    /// flight. It never takes the admission lock, so it returns at once even while a request is being verified or its marker written;
    /// the whole-entry deadline is applied to it from the clock.</summary>
    public ProcessAdmissionFacts Facts
    {
        get
        {
            var snapshot = Volatile.Read(ref _snapshot);
            return snapshot.ClosedBy is null && externalClosure?.Invoke() is { } reason ? snapshot with { ClosedBy = reason } : snapshot;
        }
    }

    /// <summary>Closes the gate for <paramref name="reason"/> unless it is already closed (the first reason is kept). Nothing is
    /// admitted afterwards; slots already consumed stay consumed.</summary>
    public void Close(string reason)
    {
        lock (_gate)
        {
            _closedBy ??= reason;
            Publish();
        }
    }

    /// <summary>Consumes the slot of the one class <paramref name="shape"/> matches, or throws the closed refusal. Returns the
    /// admitted class name. The decision, the slot and the closure are one atomic step.</summary>
    public string Admit(ProcessShape shape)
    {
        lock (_gate)
        {
            ApplyExternalClosure();
            if (_closedBy is not null)
            {
                throw Refuse(ClosedCode);
            }

            var matching = entries.Where(entry => entry.Matches(shape)).ToArray();
            if (matching.Length != 1)
            {
                throw Refuse(matching.Length == 0 ? "Unlisted" : "InventoryAmbiguous");
            }

            var entry = matching[0];
            _pending.Add(entry.Name);
            Publish();
            try
            {
                if (revalidate?.Invoke(shape) is { } drift)
                {
                    throw Refuse(drift);
                }

                // The identity check can take long: the gate may have closed meanwhile, in which case nothing is consumed.
                ApplyExternalClosure();
                if (_closedBy is not null)
                {
                    throw Refuse(ClosedCode);
                }

                var refusal = ledger.TryAdmit(entry.Name, entry.Limit, totalLimit);
                if (refusal is not null)
                {
                    throw Refuse(refusal == "ClassLimit" ? $"ClassLimit.{entry.Name}" : refusal);
                }

                // The marker is durable: from here the slot is consumed whatever happens. If the gate closed while it was being
                // written the request must not start, but the marker is never taken back.
                ApplyExternalClosure();
                if (_closedBy is not null)
                {
                    throw Refuse(ExpiredBeforeStartCode);
                }

                _admitted[entry.Name] = _admitted.GetValueOrDefault(entry.Name) + 1;
                return entry.Name;
            }
            finally
            {
                _pending.Remove(entry.Name);
                Publish();
            }
        }
    }

    /// <summary>The dispatch handoff: called after <see cref="Admit"/> and immediately before the process would be created. If the
    /// gate closed since, the request does not start: its start is moved from admitted to <see cref="ExpiredBeforeStartCode"/>, its
    /// marker stays consumed, and the closed refusal is thrown.</summary>
    public void ConfirmOpen(string admittedClass)
    {
        lock (_gate)
        {
            ApplyExternalClosure();
            if (_closedBy is null)
            {
                return;
            }

            if (_admitted.GetValueOrDefault(admittedClass) > 0)
            {
                _admitted[admittedClass]--;
                if (_admitted[admittedClass] == 0)
                {
                    _admitted.Remove(admittedClass);
                }
            }

            throw Refuse(ExpiredBeforeStartCode);
        }
    }

    /// <summary>The whole-entry deadline closes the gate the moment it elapses, read from the monotonic clock at every decision, so
    /// no start can follow the expiry whichever thread asks. The first reason, if any, is kept.</summary>
    private void ApplyExternalClosure()
    {
        if (_closedBy is null && externalClosure?.Invoke() is { } reason)
        {
            _closedBy = reason;
        }
    }

    /// <summary>Counts the refusal and closes the gate with this code unless an earlier reason already closed it.</summary>
    private ProcessAdmissionRefusedException Refuse(string code)
    {
        _refused[code] = _refused.GetValueOrDefault(code) + 1;
        _closedBy ??= code;
        Publish();
        return new ProcessAdmissionRefusedException(code);
    }

    private void Publish() =>
        Volatile.Write(
            ref _snapshot,
            new ProcessAdmissionFacts(
                new Dictionary<string, int>(_admitted),
                new Dictionary<string, int>(_refused),
                _closedBy,
                [.. _pending]));
}
