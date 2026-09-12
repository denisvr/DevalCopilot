namespace DevalCopilot.Domain.Features.Projects;

public sealed class Project
{
    private Project()
    {
    }

    public static Project Register(Guid id, string name, string canonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);

        return new Project
        {
            Id = id,
            Name = name,
            CanonicalPath = canonicalPath,
            NextExecutionNumber = 1,
        };
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string CanonicalPath { get; private set; } = string.Empty;

    public int NextExecutionNumber { get; private set; }

    public int ReserveExecutionNumber()
    {
        var executionNumber = NextExecutionNumber;
        NextExecutionNumber++;

        return executionNumber;
    }
}
