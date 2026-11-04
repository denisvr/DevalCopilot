namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// What one role's stage has asked of the message-evidence route. A request is counted before it is sent and its result, including a
/// failure or a cancellation, is kept by message identity together with the facts it was compared against, so a later read of the same
/// stage (a poll, or a retry after any failure) can neither repeat the request nor start the list again. Every read first observes its
/// comparison facts (<see cref="Observe"/>), even when it lists no reply or may request none. The first read whose facts differ from
/// the first ones observed, or from any remembered result, makes the drift irreversible for the stage: from then on no remembered
/// agreement is ever reused, not even when the original facts return, and nothing more is requested, because agreement with other
/// facts proves nothing about these. A remembered failure, disagreement or unavailable result keeps its own result. After the first
/// failed request the stage asks for nothing more. A cancellation is remembered as a failure and still propagated, so the caller's
/// deadline stays in charge.
/// </summary>
public sealed class MessageEvidenceMemory
{
    private readonly Dictionary<Guid, (MessageEvidenceFacts Facts, EvidenceRouteComparisonStatus Result)> _results = [];
    private MessageEvidenceFacts? _baseline;
    private int _issued;
    private bool _failed;
    private bool _drifted;

    /// <summary>The requests issued so far, whatever their results.</summary>
    public int Issued => _issued;

    /// <summary>True once any read observed facts other than the first ones; it never becomes false again.</summary>
    public bool Drifted => _drifted;

    /// <summary>Records the comparison facts of one read. The first read sets the baseline; any later read, or any remembered result,
    /// that differs from it latches the drift and closes the stage to further requests. It needs no reply to exist.</summary>
    public void Observe(MessageEvidenceFacts facts)
    {
        _baseline ??= facts;
        if (_baseline != facts || _results.Values.Any(entry => entry.Facts != facts))
        {
            _drifted = true;
            _failed = true;
        }
    }

    public async Task<EvidenceRouteComparisonStatus> ResolveAsync(
        Guid messageId,
        MessageEvidenceFacts facts,
        int ceiling,
        Func<Task<EvidenceRouteComparisonStatus>> request)
    {
        Observe(facts);
        if (_results.TryGetValue(messageId, out var remembered))
        {
            // Only agreement can be stale; a failure, a disagreement or an unavailable result already cannot qualify.
            return _drifted && remembered.Result == EvidenceRouteComparisonStatus.Agrees
                ? EvidenceRouteComparisonStatus.Disagrees
                : remembered.Result;
        }

        if (_failed || _issued >= ceiling)
        {
            return EvidenceRouteComparisonStatus.NotAttempted;
        }

        _issued++;
        EvidenceRouteComparisonStatus comparison;
        try
        {
            comparison = await request();
        }
        catch (OperationCanceledException)
        {
            _failed = true;
            _results[messageId] = (facts, EvidenceRouteComparisonStatus.Unavailable);
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            comparison = EvidenceRouteComparisonStatus.Unavailable;
            _failed = true;
        }

        _results[messageId] = (facts, comparison);
        return comparison;
    }
}
