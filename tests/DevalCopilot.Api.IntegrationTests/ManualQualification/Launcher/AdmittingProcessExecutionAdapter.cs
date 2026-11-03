using DevalCopilot.Application.Features.Processes.Ports;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>Wraps the one production process adapter of the host so that every request it receives is admitted first. The request
/// is forwarded unchanged and exactly once; a refused request never reaches the inner adapter, so no process is created. The gate is
/// confirmed open once more at the handoff, so a request that was admitted but whose gate closed (the entry's deadline above all)
/// before its dispatch never starts.</summary>
public sealed class AdmittingProcessExecutionAdapter(IProcessExecutionAdapter inner, ProcessAdmission admission)
    : IProcessExecutionAdapter
{
    public async Task<ProcessExecutionResult> ExecuteAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var admitted = admission.Admit(ProcessShape.From(request));
        admission.ConfirmOpen(admitted);
        return await inner.ExecuteAsync(request, cancellationToken);
    }
}
