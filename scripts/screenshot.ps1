<#
.SYNOPSIS
  Launches an app with arguments, waits, and saves a PNG of its main window.

.DESCRIPTION
  Uses PrintWindow with PW_RENDERFULLCONTENT so composition-rendered content (WinUI, WebView2)
  is captured even when another window covers it. The app is closed afterwards.

.EXAMPLE
  ./scripts/screenshot.ps1 -Exe publish/ArmuxApp.exe -Out shots/demo.png -ArgumentList '--scenario','demo'
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [string] $Out,
    [string[]] $ArgumentList = @(),
    [double] $WaitSeconds = 4,
    # Optional SendKeys sequences sent after start-up, one per element (^ = Ctrl, + = Shift, % = Alt).
    [string[]] $Keys = @()
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('Shot.Native' -as [type])) {
    Add-Type -Namespace Shot -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
[DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
public struct RECT { public int Left, Top, Right, Bottom; }
'@
}

# Physical pixels: without this, GetWindowRect returns scaled coordinates and the capture is cropped.
[Shot.Native]::SetProcessDpiAwarenessContext([IntPtr]-4) | Out-Null

$psi = [System.Diagnostics.ProcessStartInfo]::new((Resolve-Path $Exe).Path)
foreach ($a in $ArgumentList) { $psi.ArgumentList.Add($a) }
$psi.UseShellExecute = $false
$p = [System.Diagnostics.Process]::Start($psi)
try {
    $deadline = (Get-Date).AddSeconds(20)
    while ($p.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200; $p.Refresh() }
    if ($p.MainWindowHandle -eq 0) { throw "No main window after 20 s" }
    [Shot.Native]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
    Start-Sleep -Seconds $WaitSeconds
    if ($Keys) {
        Add-Type -AssemblyName System.Windows.Forms
        foreach ($k in $Keys) {
            [Shot.Native]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
            [System.Windows.Forms.SendKeys]::SendWait($k)
            Start-Sleep -Milliseconds 600
        }
        Start-Sleep -Seconds 1
    }
    # GetWindowRect includes the invisible resize border; DWM's extended frame bounds are the visible window.
    $r = [Shot.Native+RECT]::new()
    $v = [Shot.Native+RECT]::new()
    [Shot.Native]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
    [Shot.Native]::DwmGetWindowAttribute($p.MainWindowHandle, 9, [ref]$v, 16) | Out-Null
    $full = [System.Drawing.Bitmap]::new($r.Right - $r.Left, $r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($full)
    $hdc = $g.GetHdc()
    [Shot.Native]::PrintWindow($p.MainWindowHandle, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    $crop = [System.Drawing.Rectangle]::new($v.Left - $r.Left, $v.Top - $r.Top, $v.Right - $v.Left, $v.Bottom - $v.Top)
    $bmp = $full.Clone($crop, $full.PixelFormat)
    $full.Dispose()
    New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "saved $Out ($($v.Right - $v.Left)x$($v.Bottom - $v.Top))"
}
finally {
    if (-not $p.HasExited) { $null = $p.CloseMainWindow(); if (-not $p.WaitForExit(5000)) { $p.Kill($true) } }
}
