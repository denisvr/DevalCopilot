namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// Resources a test instance uses but does not create: a Git scene owned by a class fixture, the one SQLite file this instance may
/// write and the writable artifact root of its hosts. <paramref name="Owner"/> is disposed by the instance before it removes its own
/// files, so the owner of a borrowed set releases its hosts first. The scene is never disposed by the borrower.
/// </summary>
internal sealed record LocalCommitBorrowedResources(
    LocalCommitScene Scene, string DatabasePath, string WritableRoot, IDisposable Owner);
