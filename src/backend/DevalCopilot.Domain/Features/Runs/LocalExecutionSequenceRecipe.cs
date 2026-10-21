using System.Text.Json;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// One immutable pinned member of the whole enabled verification set of a <see cref="LocalExecutionSequence"/> (ADR-0060): the full
/// execution metadata of a project-owned recipe by value, including its ordered arguments, timeout, enabled flag and configuration
/// timestamps. The set is ordered by CommandNumber then Id; <see cref="Ordinal"/> is that position.
/// </summary>
public sealed class LocalExecutionSequenceRecipe
{
    private LocalExecutionSequenceRecipe()
    {
        CommandName = string.Empty;
        ExecutablePath = string.Empty;
        ArgumentsJson = string.Empty;
    }

    public static LocalExecutionSequenceRecipe Pin(Guid id, Guid sequenceId, int ordinal, VerificationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (id == Guid.Empty || sequenceId == Guid.Empty)
        {
            throw new ArgumentException("A pinned recipe requires durable identifiers.");
        }

        if (ordinal is < 0 or >= LocalExecutionSequencePolicy.MaximumRecipes)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        return new LocalExecutionSequenceRecipe
        {
            Id = id,
            SequenceId = sequenceId,
            Ordinal = ordinal,
            VerificationCommandId = command.Id,
            CommandNumber = command.CommandNumber,
            CommandName = command.Name,
            ExecutablePath = command.ExecutablePath,
            ArgumentsJson = JsonSerializer.Serialize(command.Arguments),
            TimeoutSeconds = command.TimeoutSeconds,
            IsEnabled = command.IsEnabled,
            ConfiguredAtUtc = command.ConfiguredAtUtc,
            UpdatedAtUtc = command.UpdatedAtUtc,
        };
    }

    public Guid Id { get; private set; }

    public Guid SequenceId { get; private set; }

    public int Ordinal { get; private set; }

    public Guid VerificationCommandId { get; private set; }

    public int CommandNumber { get; private set; }

    public string CommandName { get; private set; }

    public string ExecutablePath { get; private set; }

    public string ArgumentsJson { get; private set; }

    public int TimeoutSeconds { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset ConfiguredAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Whether a stored recipe still equals this pinned value in every execution-relevant member.</summary>
    public bool Matches(VerificationCommand command) =>
        command is not null
        && command.Id == VerificationCommandId
        && command.CommandNumber == CommandNumber
        && string.Equals(command.Name, CommandName, StringComparison.Ordinal)
        && string.Equals(command.ExecutablePath, ExecutablePath, StringComparison.Ordinal)
        && string.Equals(JsonSerializer.Serialize(command.Arguments), ArgumentsJson, StringComparison.Ordinal)
        && command.TimeoutSeconds == TimeoutSeconds
        && command.IsEnabled == IsEnabled
        && command.ConfiguredAtUtc == ConfiguredAtUtc
        && command.UpdatedAtUtc == UpdatedAtUtc;
}
