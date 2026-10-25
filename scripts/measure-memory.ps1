<#
.SYNOPSIS
  Sums working set and private bytes across a process and every descendant it started.

.DESCRIPTION
  This is the armux memory measurement method (see ARCHITECTURE.md, "Memory budget").
  It walks the process tree by ParentProcessId from Win32_Process, so WebView2
  (msedgewebview2.exe) children, ConPTY hosts (OpenConsole.exe/conhost.exe) and
  shells are all counted.

  Each process is tagged with a category so shell cost can be reported separately
  from app-attributable cost:
    app      - the root process and anything not matched below
    webview  - msedgewebview2.exe
    conpty   - OpenConsole.exe, conhost.exe
    shell    - pwsh.exe, powershell.exe, cmd.exe, wsl.exe, wslhost.exe, bash.exe
    workload - anything started by a shell (the user's own programs)

  "app-attributable" = app + webview + conpty. Shell and workload are reported
  but not counted against the budget, since they cost the same in any terminal.

  Samples are taken -Samples times, -IntervalSeconds apart, and the median of each
  total is reported, so a single GC or page-trim spike does not skew the result.

.EXAMPLE
  pwsh scripts/measure-memory.ps1 -Name armux
.EXAMPLE
  pwsh scripts/measure-memory.ps1 -Id 1234 -Samples 10 -Json
#>
[CmdletBinding(DefaultParameterSetName = 'ByName')]
param(
    [Parameter(ParameterSetName = 'ByName', Mandatory)] [string] $Name,
    [Parameter(ParameterSetName = 'ById', Mandatory)] [int] $Id,
    [int] $Samples = 5,
    [double] $IntervalSeconds = 1,
    [switch] $Json
)

$ErrorActionPreference = 'Stop'

$categories = @{
    'msedgewebview2.exe' = 'webview'
    'openconsole.exe'    = 'conpty'
    'conhost.exe'        = 'conpty'
    'pwsh.exe'           = 'shell'
    'powershell.exe'     = 'shell'
    'cmd.exe'            = 'shell'
    'wsl.exe'            = 'shell'
    'wslhost.exe'        = 'shell'
    'bash.exe'           = 'shell'
}

function Get-Tree([int[]] $RootIds) {
    $all = Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId, Name, CreationDate
    $byParent = $all | Group-Object ParentProcessId -AsHashTable -AsString
    $seen = [System.Collections.Generic.HashSet[int]]::new()
    $queue = [System.Collections.Generic.Queue[object]]::new()
    foreach ($r in $all | Where-Object { $RootIds -contains $_.ProcessId }) { $queue.Enqueue(@($r, 'app')) }
    while ($queue.Count) {
        $p, $parentCat = $queue.Dequeue()
        if (-not $seen.Add([int]$p.ProcessId)) { continue }
        # Anything a shell started is the user's workload, not armux's cost.
        $cat = if ($parentCat -in 'shell', 'workload') { 'workload' }
               elseif ($categories[$p.Name.ToLowerInvariant()]) { $categories[$p.Name.ToLowerInvariant()] }
               else { 'app' }
        [pscustomobject]@{ ProcessId = $p.ProcessId; Name = $p.Name; Category = $cat }
        foreach ($c in $byParent["$($p.ProcessId)"]) {
            # PIDs are reused; a child must have been created after its parent.
            if ($c.CreationDate -ge $p.CreationDate) { $queue.Enqueue(@($c, $cat)) }
        }
    }
}

$rootIds = if ($PSCmdlet.ParameterSetName -eq 'ById') { @($Id) } else {
    @(Get-Process -Name $Name -ErrorAction Stop | ForEach-Object Id)
}

$runs = for ($i = 0; $i -lt $Samples; $i++) {
    if ($i) { Start-Sleep -Seconds $IntervalSeconds }
    $rows = foreach ($p in Get-Tree $rootIds) {
        $gp = Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
        if (-not $gp) { continue }
        [pscustomobject]@{
            Pid       = $p.ProcessId
            Name      = $p.Name
            Category  = $p.Category
            WorkingMB = [math]::Round($gp.WorkingSet64 / 1MB, 1)
            PrivateMB = [math]::Round($gp.PrivateMemorySize64 / 1MB, 1)
        }
    }
    , @($rows)
}

function Median([double[]] $v) {
    $s = $v | Sort-Object
    if ($s.Count % 2) { $s[[int][math]::Floor($s.Count / 2)] } else { ($s[$s.Count / 2 - 1] + $s[$s.Count / 2]) / 2 }
}

$summary = foreach ($cat in 'app', 'webview', 'conpty', 'shell', 'workload', 'total', 'app-attributable') {
    $pick = switch ($cat) {
        'total'            { { $true } }
        'app-attributable' { { $_.Category -in 'app', 'webview', 'conpty' } }
        default            { [scriptblock]::Create("`$_.Category -eq '$cat'") }
    }
    $ws = foreach ($r in $runs) { ($r | Where-Object $pick | Measure-Object WorkingMB -Sum).Sum + 0 }
    $pv = foreach ($r in $runs) { ($r | Where-Object $pick | Measure-Object PrivateMB -Sum).Sum + 0 }
    [pscustomobject]@{ Scope = $cat; WorkingMB = [math]::Round((Median $ws), 1); PrivateMB = [math]::Round((Median $pv), 1) }
}

if ($Json) {
    [pscustomobject]@{ Samples = $Samples; Processes = $runs[-1]; Summary = $summary } | ConvertTo-Json -Depth 4
} else {
    "Processes (last sample):"
    $runs[-1] | Sort-Object Category, Name | Format-Table -AutoSize | Out-String
    "Median of $Samples samples:"
    $summary | Format-Table -AutoSize | Out-String
}
