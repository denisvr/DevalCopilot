using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Infrastructure.Persistence;

/// <summary>
/// Ensures the single deterministic walking-skeleton project is registered. Real
/// project registration through the environment-readiness feature replaces this in a
/// later increment.
/// </summary>
public static class ProjectFixture
{
    public const string Name = "DevalCopilot";
    public const string CanonicalPath = @"C:\repos\DevalCopilot";

    public static async Task EnsureSeededAsync(DevalCopilotDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var alreadySeeded = await dbContext.Projects
            .AnyAsync(project => project.CanonicalPath == CanonicalPath, cancellationToken);

        if (alreadySeeded)
        {
            return;
        }

        dbContext.Projects.Add(Project.Register(Guid.NewGuid(), Name, CanonicalPath));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
