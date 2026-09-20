namespace DevalCopilot.Application.Features.Runs.Ports;

public interface IClaudeReviewCorrectionAdapter
{
    Task<ReviewCorrectionInvocationResult> InvokeAsync(
        ReviewCorrectionInvocationRequest request, CancellationToken cancellationToken);
}
