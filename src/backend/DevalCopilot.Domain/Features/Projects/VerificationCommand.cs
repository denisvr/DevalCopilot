using System.Text;

namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// A user-configured, project-owned local verification recipe. It is deliberately only a typed
/// executable plus literal arguments: no shell command text, script interpolation, environment
/// values, or authority to execute belongs in configuration. A checkpoint-bound execution
/// snapshots this configuration before starting a process.
/// </summary>
public sealed class VerificationCommand
{
    public const int MaximumArgumentCount = 64;
    public const int MaximumArgumentUtf8Bytes = 4 * 1024;
    public const int MaximumTimeoutSeconds = 15 * 60;

    private VerificationCommand()
    {
    }

    public static VerificationCommand Configure(
        Guid id,
        Guid projectId,
        int commandNumber,
        string name,
        string executablePath,
        IReadOnlyCollection<string> arguments,
        int timeoutSeconds,
        bool isEnabled,
        DateTimeOffset configuredAtUtc)
    {
        var command = new VerificationCommand
        {
            Id = id,
            ProjectId = projectId,
            CommandNumber = commandNumber,
            ConfiguredAtUtc = configuredAtUtc,
        };

        command.Update(name, executablePath, arguments, timeoutSeconds, isEnabled, configuredAtUtc);
        return command;
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public int CommandNumber { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string ExecutablePath { get; private set; } = string.Empty;

    public IReadOnlyList<string> Arguments { get; private set; } = [];

    public int TimeoutSeconds { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset ConfiguredAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(
        string name,
        string executablePath,
        IReadOnlyCollection<string> updatedArguments,
        int timeoutSeconds,
        bool isEnabled,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(updatedArguments);

        if (CommandNumber < 1)
        {
            throw new InvalidOperationException("A verification command must have a positive command number.");
        }

        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException("The executable path must be fully qualified.", nameof(executablePath));
        }

        if (updatedArguments.Count > MaximumArgumentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedArguments));
        }

        foreach (var argument in updatedArguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            if (Encoding.UTF8.GetByteCount(argument) > MaximumArgumentUtf8Bytes)
            {
                throw new ArgumentOutOfRangeException(nameof(updatedArguments));
            }
        }

        if (timeoutSeconds is < 1 or > MaximumTimeoutSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        Name = name;
        ExecutablePath = executablePath;
        Arguments = updatedArguments.ToArray();
        TimeoutSeconds = timeoutSeconds;
        IsEnabled = isEnabled;
        UpdatedAtUtc = updatedAtUtc;
    }
}
