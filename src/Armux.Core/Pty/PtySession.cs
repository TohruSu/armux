using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Armux.Core.Pty;

/// <summary>
/// A shell running in a Windows pseudoconsole (ConPTY). Output arrives on a reader thread through the
/// <c>output</c> callback; input goes through <see cref="Write(string)"/>. Each session costs one conhost.exe
/// (the ConPTY host), the shell, one reader thread and a thread-pool wait for exit.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class PtySession : IDisposable
{
    readonly IntPtr hpc;
    readonly SafeFileHandle input;
    readonly FileStream inputStream;
    readonly SafeWaitHandle process;
    readonly RegisteredWaitHandle exitWait;
    readonly Thread reader;
    readonly Action<ReadOnlyMemory<byte>> output;
    readonly Action<int> exited;
    int disposed;

    public int ProcessId { get; }

    PtySession(IntPtr hpc, SafeFileHandle input, SafeFileHandle outputPipe, SafeWaitHandle process, int pid,
        Action<ReadOnlyMemory<byte>> output, Action<int> exited)
    {
        this.hpc = hpc;
        this.output = output;
        this.exited = exited;
        this.input = input;
        this.process = process;
        ProcessId = pid;
        inputStream = new FileStream(input, FileAccess.Write, 1);
        reader = new Thread(() => ReadLoop(outputPipe)) { IsBackground = true, Name = "armux pty reader" };
        var wait = new ManualResetEvent(false) { SafeWaitHandle = process };
        exitWait = ThreadPool.RegisterWaitForSingleObject(wait, (_, _) => OnExit(), null, Timeout.Infinite, executeOnlyOnce: true);
    }

    /// <summary>
    /// Starts <paramref name="commandLine"/> in a new pseudoconsole of the given size. <paramref name="output"/> is called on
    /// the reader thread with each chunk; <paramref name="exited"/> on a thread-pool thread with the exit code.
    /// </summary>
    public static PtySession Start(string commandLine, string? workingDirectory, IReadOnlyDictionary<string, string> extraEnvironment,
        int cols, int rows, Action<ReadOnlyMemory<byte>> output, Action<int> exited)
    {
        if (!CreatePipe(out var inRead, out var inWrite, IntPtr.Zero, 0)) throw new Win32Exception();
        if (!CreatePipe(out var outRead, out var outWrite, IntPtr.Zero, 0)) throw new Win32Exception();
        int hr = CreatePseudoConsole(new Coord((short)cols, (short)rows), inRead, outWrite, 0, out var hpc);
        inRead.Dispose();
        outWrite.Dispose();
        if (hr != 0) { inWrite.Dispose(); outRead.Dispose(); throw new Win32Exception(hr); }

        IntPtr attrs = IntPtr.Zero;
        try
        {
            IntPtr size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attrs = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attrs, 1, 0, ref size)) throw new Win32Exception();
            if (!UpdateProcThreadAttribute(attrs, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, hpc, IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception();

            var si = new StartupInfoEx
            {
                cb = Marshal.SizeOf<StartupInfoEx>(),
                // Without this, a child of a parent with redirected standard handles writes to those handles
                // instead of the pseudoconsole (seen in the Phase 3 spike).
                dwFlags = STARTF_USESTDHANDLES,
                lpAttributeList = attrs,
            };
            // CreateProcessW may write to the command line, so it gets its own buffer.
            var cmd = (commandLine + "\0").ToCharArray();
            var env = BuildEnvironment(extraEnvironment).ToCharArray();
            string? dir = workingDirectory is not null && Directory.Exists(workingDirectory) ? workingDirectory : null;
            ProcessInformation pi;
            unsafe
            {
                fixed (char* c = cmd)
                fixed (char* e = env)
                    if (!CreateProcessW(null, c, IntPtr.Zero, IntPtr.Zero, false, EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT,
                            e, dir, ref si, out pi))
                        throw new Win32Exception();
            }
            CloseHandle(pi.hThread);
            var session = new PtySession(hpc, inWrite, outRead, new SafeWaitHandle(pi.hProcess, true), pi.dwProcessId, output, exited);
            session.reader.Start();
            return session;
        }
        catch
        {
            ClosePseudoConsole(hpc);
            inWrite.Dispose();
            outRead.Dispose();
            throw;
        }
        finally
        {
            if (attrs != IntPtr.Zero) { DeleteProcThreadAttributeList(attrs); Marshal.FreeHGlobal(attrs); }
        }
    }

    /// <summary>The current environment plus <paramref name="extra"/>, as a sorted, double-NUL-terminated UTF-16 block.</summary>
    static string BuildEnvironment(IReadOnlyDictionary<string, string> extra)
    {
        var vars = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            vars[(string)e.Key] = (string?)e.Value ?? "";
        foreach (var (k, v) in extra) vars[k] = v;
        var sb = new StringBuilder();
        foreach (var (k, v) in vars) sb.Append(k).Append('=').Append(v).Append('\0');
        return sb.Append('\0').ToString();
    }

    void ReadLoop(SafeFileHandle pipe)
    {
        var buf = new byte[16 * 1024];
        using var stream = new FileStream(pipe, FileAccess.Read, 1);
        try
        {
            int n;
            while ((n = stream.Read(buf, 0, buf.Length)) > 0) output(buf.AsMemory(0, n));
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    void OnExit()
    {
        GetExitCodeProcess(process, out int code);
        exited(code);
    }

    public void Write(string text) => Write(Encoding.UTF8.GetBytes(text));

    public void Write(ReadOnlySpan<byte> data)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        try { inputStream.Write(data); inputStream.Flush(); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    public void Resize(int cols, int rows)
    {
        if (Volatile.Read(ref disposed) == 0) ResizePseudoConsole(hpc, new Coord((short)Math.Max(1, cols), (short)Math.Max(1, rows)));
    }

    /// <summary>Closes the pseudoconsole, which ends the shell, and releases the pipes.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        exitWait.Unregister(null);
        inputStream.Dispose();
        input.Dispose();
        // ClosePseudoConsole can wait for the reader to drain output, so it runs off the caller's thread.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            ClosePseudoConsole(hpc);
            process.Dispose();
        });
    }

    // ---- interop -------------------------------------------------------------------------------

    const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000, CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    const int STARTF_USESTDHANDLES = 0x00000100;
    static readonly IntPtr PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    [StructLayout(LayoutKind.Sequential)]
    readonly struct Coord(short x, short y) { readonly short X = x, Y = y; }

    [StructLayout(LayoutKind.Sequential)]
    struct StartupInfoEx
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, int size);

    [LibraryImport("kernel32.dll")]
    private static partial int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr hpc);

    [LibraryImport("kernel32.dll")]
    private static partial int ResizePseudoConsole(IntPtr hpc, Coord size);

    [LibraryImport("kernel32.dll")]
    private static partial void ClosePseudoConsole(IntPtr hpc);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);

    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(IntPtr list);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CreateProcessW(string? application, char* commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, char* environment, string? currentDirectory,
        ref StartupInfoEx startupInfo, out ProcessInformation processInformation);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(SafeWaitHandle process, out int exitCode);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
