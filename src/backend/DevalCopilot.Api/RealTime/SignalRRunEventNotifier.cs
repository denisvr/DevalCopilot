using DevalCopilot.Application.Features.Runs.Ports;
using Microsoft.AspNetCore.SignalR;

namespace DevalCopilot.Api.RealTime;

public sealed class SignalRRunEventNotifier(IHubContext<RunNotificationHub> hubContext) : IRunEventNotifier
{
    public Task NotifyRunAdvancedAsync(Guid runId, long latestSequence, CancellationToken cancellationToken)
    {
        return hubContext.Clients.All.SendAsync(
            RunAdvancedNotification.ClientMethodName,
            new RunAdvancedNotification(runId, latestSequence),
            cancellationToken);
    }
}
