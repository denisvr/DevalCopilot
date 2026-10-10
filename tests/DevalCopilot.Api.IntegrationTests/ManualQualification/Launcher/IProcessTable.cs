namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

public interface IProcessTable
{
    /// <summary>The complete table, or a <see cref="ProcessTableException"/>: a failed or partial read is never returned as a
    /// shorter table.</summary>
    IReadOnlyList<ProcessEntry> Read();
}
