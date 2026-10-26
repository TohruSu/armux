using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Armux.Core;

namespace Armux.App;

/// <summary>
/// The status of the current workspace on armux's taskbar button, through ITaskbarList3 progress states:
/// working = a full normal (green) bar, needs input = yellow, error = red, idle = nothing. Shown while armux is in the
/// background as well, which is the point: you can see Claude Code's state while tabbed out.
/// </summary>
public static partial class Taskbar
{
    const int TBPF_NOPROGRESS = 0, TBPF_NORMAL = 2, TBPF_ERROR = 4, TBPF_PAUSED = 8;

    static ITaskbarList3? list;
    static bool failed;
    static AgentState? shown;

    public static void Show(IntPtr hwnd, AgentState state)
    {
        if (failed || state == shown) return;
        try
        {
            list ??= Create();
            switch (state)
            {
                case AgentState.Working:
                case AgentState.NeedsInput:
                case AgentState.Error:
                    // A full bar in the state's color. Windows 11 draws the indeterminate marquee as a faint dot in the
                    // accent color, too subtle to notice from another window, so "working" is a solid normal bar too.
                    list.SetProgressState(hwnd, TBPF_NORMAL);
                    list.SetProgressValue(hwnd, 100, 100);
                    if (state != AgentState.Working) list.SetProgressState(hwnd, state == AgentState.Error ? TBPF_ERROR : TBPF_PAUSED);
                    break;
                default:
                    list.SetProgressState(hwnd, TBPF_NOPROGRESS);
                    break;
            }
            shown = state;
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            failed = true;
            App.Log(nameof(Taskbar), e);
        }
    }

    static ITaskbarList3 Create()
    {
        Guid clsid = new("56FDF344-FD6D-11d0-958A-006097C9A090"), iid = typeof(ITaskbarList3).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(clsid, IntPtr.Zero, 1 /* CLSCTX_INPROC_SERVER */, iid, out var ptr));
        var obj = (ITaskbarList3)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);
        Marshal.Release(ptr);
        obj.HrInit();
        return obj;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, IntPtr outer, int context, in Guid iid, out IntPtr ppv);

    /// <summary>ITaskbarList, ITaskbarList2 and the first ITaskbarList3 methods, in vtable order.</summary>
    [GeneratedComInterface]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    internal partial interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, int fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, int flags);
    }
}
