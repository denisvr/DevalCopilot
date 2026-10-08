using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Commands.ExecuteLocalCommit;
using DevalCopilot.Application.Features.Runs.Queries.GetEligibleLocalCommitOperations;

namespace DevalCopilot.Api.HostedServices;

/// <summary>
/// Executes only durably admitted local-commit operations (ADR-0029), one at a time. The query only proposes candidates; the
/// execution command re-reads the complete authority, commits the single-use execution marker and then performs the one host
/// mutation, so a polling cycle can never run an operation twice. Startup recovery has already decided every operation a previous
/// host left open before this service starts.
/// </summary>
public sealed class LocalCommitSupervisor(IServiceScopeFactory scopeFactory, ILogger<LocalCommitSupervisor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                IReadOnlyList<Guid> operations;
                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
                    var eligible = await mediator.SendAsync(new GetEligibleLocalCommitOperationsQuery(), stoppingToken);
                    operations = eligible.IsSuccess ? eligible.Value : [];
                }

                foreach (var operationId in operations)
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
                    await mediator.SendAsync(new ExecuteLocalCommitCommand(operationId), stoppingToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError("local_commit_supervisor_iteration_failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
