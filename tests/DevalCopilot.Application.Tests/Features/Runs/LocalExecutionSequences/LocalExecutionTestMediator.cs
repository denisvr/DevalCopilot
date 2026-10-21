using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Infrastructure.Persistence;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// The small test mediator the coordinator claim composes the six existing claim commands through. Every request is routed to the REAL
/// production handler of its type and every dispatch builds that handler with its OWN fresh <see cref="DevalCopilotDbContext"/> from
/// <see cref="ContextFactory"/>, so a claim here is exactly as independent of the caller's connection as one in the hosted process, and
/// SQLite races between claims, stops and manual writers are real. Any other request type is a test defect and throws.
/// </summary>
internal sealed class LocalExecutionTestMediator(
    Func<IDevalCopilotDbContext> contextFactory,
    IGitWorkspaceEvidenceReader evidence,
    IArtifactStore artifacts,
    TimeProvider clock,
    IAttemptDurabilityProbe durability,
    ILocalExecutionStepProbe stepProbe) : IApplicationMediator
{
    /// <summary>The context every composed handler is built over; replace it to inject a fault-injecting decorator.</summary>
    public Func<IDevalCopilotDbContext> ContextFactory { get; set; } = contextFactory;

    /// <summary>How many composed requests have been dispatched, per request type name.</summary>
    public List<string> Dispatched { get; } = [];

    public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException($"The test mediator routes no query ({query.GetType().Name}).");

    public async Task<TResult> SendAsync<TResult>(ICommand<TResult> request, CancellationToken cancellationToken = default)
    {
        Dispatched.Add(request.GetType().Name);
        var context = ContextFactory();
        try
        {
            object result = request switch
            {
                CreateImplementationAttemptCommand command => await new CreateImplementationAttemptCommandHandler(
                    context, evidence, artifacts, clock, durability, stepProbe).HandleAsync(command, cancellationToken),
                ClaimVerificationExecutionCommand command => await new ClaimVerificationExecutionCommandHandler(
                    context, evidence, clock, stepProbe).HandleAsync(command, cancellationToken),
                CreateCodeReviewAttemptCommand command => await new CreateCodeReviewAttemptCommandHandler(
                    context, evidence, artifacts, clock, durability, null, stepProbe).HandleAsync(command, cancellationToken),
                CreateVerificationDiagnosisAttemptCommand command => await new CreateVerificationDiagnosisAttemptCommandHandler(
                    context, evidence, artifacts, clock, durability, null, stepProbe).HandleAsync(command, cancellationToken),
                CreateReviewCorrectionAttemptCommand command => await new CreateReviewCorrectionAttemptCommandHandler(
                    context, evidence, artifacts, clock, null, stepProbe).HandleAsync(command, cancellationToken),
                CreateDiagnosisCorrectionAttemptCommand command => await new CreateDiagnosisCorrectionAttemptCommandHandler(
                    context, evidence, artifacts, clock, durability, null, stepProbe).HandleAsync(command, cancellationToken),
                _ => throw new InvalidOperationException($"The test mediator does not route {request.GetType().Name}."),
            };
            return (TResult)result;
        }
        finally
        {
            if (context is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        }
    }
}
