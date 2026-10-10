using System.Runtime.InteropServices;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>One read-only snapshot of the machine's processes with parent identities and creation times (the Windows tool-help
/// snapshot). Only the documented end-of-table answer ends a read; any other failure is an exception, never a shorter table.</summary>
public sealed class ProcessTable : IProcessTable
{
    internal const int NoMoreFiles = 18;

    private const uint SnapshotProcesses = 0x2;
    private const uint QueryLimitedInformation = 0x1000;
    private static readonly IntPtr InvalidHandle = new(-1);

    public IReadOnlyList<ProcessEntry> Read()
    {
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == InvalidHandle)
        {
            throw new ProcessTableException("The process snapshot could not be taken.");
        }

        try
        {
            var entry = new NativeEntry { Size = (uint)Marshal.SizeOf<NativeEntry>() };
            return Collect(
                () => Process32First(snapshot, ref entry),
                () => Process32Next(snapshot, ref entry),
                Marshal.GetLastWin32Error,
                () => new ProcessEntry(
                    (int)entry.ProcessId,
                    (int)entry.ParentProcessId,
                    entry.ExecutableName ?? string.Empty,
                    StartOf(entry.ProcessId)));
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    /// <summary>The enumeration contract, separated from the native calls so each outcome can be proven deterministically: a
    /// successful first step, then steps until the documented end-of-table error. Any other error, or an empty table, throws.</summary>
    internal static IReadOnlyList<ProcessEntry> Collect(
        Func<bool> first,
        Func<bool> next,
        Func<int> lastError,
        Func<ProcessEntry> current)
    {
        var entries = new List<ProcessEntry>();
        if (!first())
        {
            throw new ProcessTableException("The process table could not be enumerated.");
        }

        do
        {
            entries.Add(current());
        }
        while (next());

        return lastError() == NoMoreFiles
            ? entries
            : throw new ProcessTableException("The process table enumeration ended with an error.");
    }

    private static DateTime? StartOf(uint processId)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return GetProcessTimes(handle, out var created, out _, out _, out _) && created > 0
                ? DateTime.FromFileTimeUtc(created)
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public UIntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableName;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref NativeEntry entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref NativeEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        IntPtr process,
        out long created,
        out long exited,
        out long kernel,
        out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
