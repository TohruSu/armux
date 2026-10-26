<#
.SYNOPSIS
  Measures cold start and memory for a GUI executable (armux, or a stack spike).

.DESCRIPTION
  Cold start: launches the exe -Runs times (after one discarded warm-up run) with
  ARMUX_TIMING_FILE set. The app writes "first_frame_ms=<n>" to that file once its
  first frame is presented, measured from its own process creation time. The
  median is reported.

  Memory: launches the exe once more, waits -SettleSeconds, then runs
  measure-memory.ps1 on its process tree (median of 5 samples).

  Every launched instance is closed with WM_CLOSE, and killed if it does not exit
  within 5 s.

.EXAMPLE
  ./scripts/bench.ps1 -Exe publish/armux.exe -ArgumentList '--scenario','idle' -Label idle
  (Run it from a pwsh prompt. `pwsh -File` passes an array argument as one string.)
.EXAMPLE
  ./scripts/bench.ps1 -Exe publish/armux.exe -ArgumentList '--scenario','browser','--close-after','5000' -SettleSeconds 70 -Runs 0 -Label browser-closed
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [string[]] $ArgumentList = @(),
    [string] $Label = 'run',
    [int] $Runs = 10,
    [int] $SettleSeconds = 10,
    [int] $StartTimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
$exePath = (Resolve-Path $Exe).Path
$measure = Join-Path $PSScriptRoot 'measure-memory.ps1'

function Start-Instance([string] $timingFile) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new($exePath)
    foreach ($a in $ArgumentList) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = Split-Path $exePath
    if ($timingFile) { $psi.Environment['ARMUX_TIMING_FILE'] = $timingFile }
    [System.Diagnostics.Process]::Start($psi)
}

function Stop-Instance([System.Diagnostics.Process] $p) {
    if ($p.HasExited) { return }
    $null = $p.CloseMainWindow()
    if (-not $p.WaitForExit(5000)) { $p.Kill($true); $p.WaitForExit() }
    # Give WebView2 helper processes time to exit so they don't leak into the next run.
    Start-Sleep -Milliseconds 1500
}

$startup = @()
for ($i = 0; $i -le $Runs -and $Runs -gt 0; $i++) {
    $tf = Join-Path ([IO.Path]::GetTempPath()) "armux-timing-$PID-$i.txt"
    Remove-Item $tf -ErrorAction SilentlyContinue
    $p = Start-Instance $tf
    $deadline = (Get-Date).AddSeconds($StartTimeoutSeconds)
    while (-not (Test-Path $tf) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 50 }
    $ms = if (Test-Path $tf) { [double]((Get-Content $tf -Raw) -replace '.*first_frame_ms=([\d.]+).*', '$1') } else { $null }
    Stop-Instance $p
    Remove-Item $tf -ErrorAction SilentlyContinue
    if ($i -gt 0 -and $null -ne $ms) { $startup += $ms }   # run 0 is the warm-up
}

$p = Start-Instance $null
Start-Sleep -Seconds $SettleSeconds
$mem = & $measure -Id $p.Id -Json | ConvertFrom-Json
Stop-Instance $p

$sorted = $startup | Sort-Object
$median = if ($sorted.Count) { if ($sorted.Count % 2) { $sorted[[int][math]::Floor($sorted.Count / 2)] } else { ($sorted[$sorted.Count / 2 - 1] + $sorted[$sorted.Count / 2]) / 2 } } else { $null }
$s = @{}; foreach ($row in $mem.Summary) { $s[$row.Scope] = $row }

[pscustomobject]@{
    Label             = $Label
    Exe               = Split-Path $exePath -Leaf
    Args              = $ArgumentList -join ' '
    SizeKB            = [math]::Round((Get-Item $exePath).Length / 1KB)
    StartupMedianMs   = $median
    StartupRuns       = $sorted.Count
    AppWorkingMB      = $s['app'].WorkingMB
    AppPrivateMB      = $s['app'].PrivateMB
    WebViewWorkingMB  = $s['webview'].WorkingMB
    WebViewPrivateMB  = $s['webview'].PrivateMB
    AttribWorkingMB   = $s['app-attributable'].WorkingMB
    AttribPrivateMB   = $s['app-attributable'].PrivateMB
    AttribPrivateWSMB = $s['app-attributable'].PrivateWSMB
    Processes         = ($mem.Processes | ForEach-Object { $_.Name }) -join ','
}
