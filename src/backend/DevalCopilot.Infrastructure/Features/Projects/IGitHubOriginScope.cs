namespace DevalCopilot.Infrastructure.Features.Projects;

/// <summary>A held, read-only binding to one root's own administrative scope for the duration of an origin observation.</summary>
internal interface IGitHubOriginScope : IDisposable
{
    /// <summary>True while the held scope is still the plain, non-redirected scope it was acquired as.</summary>
    bool IsIntact();
}
