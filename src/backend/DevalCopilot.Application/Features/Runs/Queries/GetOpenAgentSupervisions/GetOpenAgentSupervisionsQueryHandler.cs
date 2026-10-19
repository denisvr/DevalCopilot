using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetOpenAgentSupervisions;

public sealed class GetOpenAgentSupervisionsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetOpenAgentSupervisionsQuery, OpenAgentSupervisionWindow>
{
    public async Task<OpenAgentSupervisionWindow> HandleAsync(GetOpenAgentSupervisionsQuery query, CancellationToken cancellationToken)
    {
        // Open observations are few (one per live supervisor, plus any a host loss left for startup): the identifiers alone are read and
        // totally ordered here, so the cursor is a plain position in one fixed order.
        var open = (await dbContext.AgentSupervisions.AsNoTracking()
                .Where(candidate => candidate.ClosedAtUtc == null)
                .Select(candidate => candidate.Id)
                .ToListAsync(cancellationToken))
            .Order()
            .ToList();
        if (open.Count == 0)
        {
            return new OpenAgentSupervisionWindow([], null);
        }

        var start = query.After is { } after ? open.FindIndex(candidate => candidate.CompareTo(after) > 0) : 0;
        if (start < 0)
        {
            start = 0;
        }

        var window = new List<Guid>();
        for (var offset = 0; offset < open.Count && window.Count < AgentSupervisionPolicy.SweepBatchSize; offset++)
        {
            window.Add(open[(start + offset) % open.Count]);
        }

        return new OpenAgentSupervisionWindow(window, window[^1]);
    }
}
