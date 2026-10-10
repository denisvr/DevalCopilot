using DevalCopilot.Api.IntegrationTests.ManualQualification;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>A read-only process table that is scripted, not native: the first read (the watch's baseline) is one table and every later
/// read, from the sampler or the final proof, another. It is an offline observation input and never evidence of a native shutdown.</summary>
public sealed class ScriptedProcessTable(IReadOnlyList<ProcessEntry> baseline, IReadOnlyList<ProcessEntry> afterwards) : IProcessTable
{
    private int _reads;

    public int Reads => Volatile.Read(ref _reads);

    public IReadOnlyList<ProcessEntry> Read() => Interlocked.Increment(ref _reads) == 1 ? baseline : afterwards;
}
