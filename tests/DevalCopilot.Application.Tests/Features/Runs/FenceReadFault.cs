namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>How <see cref="FenceReadFaultInterceptor"/> fails the workspace-execution read of a claim's commit seam.</summary>
public enum FenceReadFault
{
    /// <summary>The caller cancels while the read is starting.</summary>
    Cancel,

    /// <summary>The database raises an error for the read.</summary>
    DatabaseFailure,
}
