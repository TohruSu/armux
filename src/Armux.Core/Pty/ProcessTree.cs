using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Armux.Core.Pty;

/// <summary>Finds the programs a shell has started, so closing armux can warn before ending them.</summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class ProcessTree
{
    /// <summary>Snapshot of every process as (pid, parent pid, exe name).</summary>
    public static List<(int Pid, int Parent, string Name)> Snapshot()
    {
        var list = new List<(int, int, string)>();
        IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == INVALID_HANDLE_VALUE) return list;
        try
        {
            var e = new ProcessEntry32 { dwSize = sizeof(ProcessEntry32) };
            for (bool ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e))
                list.Add((e.th32ProcessID, e.th32ParentProcessID, new string(e.szExeFile)));
        }
        finally { CloseHandle(snap); }
        return list;
    }

    /// <summary>Names of the direct children of <paramref name="pid"/> in <paramref name="snapshot"/>, without the extension.</summary>
    public static IEnumerable<string> ChildNames(IReadOnlyList<(int Pid, int Parent, string Name)> snapshot, int pid) =>
        snapshot.Where(p => p.Parent == pid && p.Pid != pid).Select(p => Path.GetFileNameWithoutExtension(p.Name).ToLowerInvariant());

    const uint TH32CS_SNAPPROCESS = 0x00000002;
    static readonly IntPtr INVALID_HANDLE_VALUE = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry32
    {
        public int dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public int th32ModuleID, cntThreads, th32ParentProcessID, pcPriClassBase, dwFlags;
        public fixed char szExeFile[260];
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr CreateToolhelp32Snapshot(uint flags, int pid);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32 entry);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32NextW(IntPtr snapshot, ref ProcessEntry32 entry);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
