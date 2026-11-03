using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexAccountAllowance;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;
using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The three production observers that start a Codex <c>app-server</c> process through their own duplex process boundary, which the
/// shared process adapter never sees. In a real session none is listed: each is answered here as an unavailable observation after
/// the admission boundary counted and refused the launch, so the process is never created. The production account-usage dispatch
/// guard calls its observer only when the run configured a usage stop, which the qualification run never does; any call is
/// therefore a visible refusal, never a hidden launch and never an invented readiness.
/// </summary>
public sealed class AdmissionRefusingCodexObservers(ProcessAdmission admission)
    : ICodexAccountAllowanceAdapter, ICodexModelCatalogAdapter, IAccountUsageObserver
{
    private static readonly string[] AppServerArguments = ["app-server", "--stdio"];

    Task<CodexAccountAllowanceObservation> ICodexAccountAllowanceAdapter.ObserveAsync(
        string executablePath,
        string? scriptPath,
        CancellationToken cancellationToken) =>
        Refuse(executablePath, CodexAccountAllowanceObservation.Unknown);

    Task<CodexModelCatalogObservation> ICodexModelCatalogAdapter.ObserveAsync(
        string executablePath,
        string? scriptPath,
        CancellationToken cancellationToken) =>
        Refuse(executablePath, CodexModelCatalogObservation.Unknown);

    Task<AccountUsageObservation> IAccountUsageObserver.ObserveAsync(
        string executablePath,
        string? scriptPath,
        CancellationToken cancellationToken) =>
        Refuse(executablePath, AccountUsageObservation.Unavailable);

    private Task<T> Refuse<T>(string executablePath, T unavailable)
    {
        try
        {
            admission.Admit(new ProcessShape(executablePath, AppServerArguments));
        }
        catch (ProcessAdmissionRefusedException)
        {
            return Task.FromResult(unavailable);
        }

        throw new InvalidOperationException("The app-server launch must never be a listed class.");
    }
}
