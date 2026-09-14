# Orphan reaper and stall detector for the Helix launchers (run.ps1 / run-next.ps1).
#
# WHY THIS EXISTS
# ---------------
# The coding-agent path spawns one `claude` process per task, with cwd set to
# that task's git worktree. The SDK closes them on the ordered path
# (`query.aclose()` -> transport terminate -> kill, ~20s) and registers an
# atexit reaper. Both are skipped when the run dies from ABOVE: Helix's own
# `coding_agent/agent_sdk_client.py:388-401` documents that a foreign
# CancelledError "skips it instead", and `await query.aclose()` in that same
# `finally` is itself an await, so inside an already-cancelled coroutine it
# never reaches the escalation. On Windows a dying parent does not take its
# children with it and atexit does not run when the process is killed outright.
#
# Observed on wave w15 (2026-09-13/14): three launches each left 12-16 live
# `claude.exe` with every worktree already torn down -- unable to deliver
# anything, still burning tokens. The last one ran 2h30 without writing a
# single file.
#
# WHAT THIS DOES NOT DO
# ---------------------
# It never kills an agent while the run is healthy. The upstream reliability
# council rejected thread-level task deadlines for a good reason: "a fired
# thread-level timeout cannot kill the worker, so the barrier would proceed
# while a zombie coder still mutates its worktree; that cure is worse than the
# hang". So: reap only what OUTLIVES the run, and abort only a run that is
# provably making no progress at all.
#
# SAFETY: WHICH PROCESSES CAN BE KILLED
# -------------------------------------
# The tracked set is derived from the PROCESS TREE below -RootPid (the launcher
# itself), never from a name filter. The operator's own Claude Code session is
# an ANCESTOR of the launcher, not a descendant, so it can never enter the set.
# A `claude.exe` started by hand elsewhere is likewise untouched.
[CmdletBinding(DefaultParameterSetName = "Watch")]
param(
    [Parameter(ParameterSetName = "Watch", Mandatory = $true)]
    [switch]$Watch,
    [Parameter(ParameterSetName = "Watch", Mandatory = $true)]
    [int]$RootPid,
    [Parameter(ParameterSetName = "Watch")]
    [string]$RepoRoot,
    [Parameter(ParameterSetName = "Watch")]
    [int]$StallMinutes = 45,
    [Parameter(ParameterSetName = "Watch")]
    [int]$PollSeconds = 15,

    [Parameter(ParameterSetName = "Reap", Mandatory = $true)]
    [switch]$Reap,

    [Parameter(Mandatory = $true)]
    [string]$PidFile,
    [string]$LogFile
)

$ErrorActionPreference = "Continue"

function Write-ReapLog {
    param([string]$Message)
    $line = "[reap {0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $Message
    Write-Host $line
    if (-not [string]::IsNullOrWhiteSpace($LogFile)) {
        try { Add-Content -LiteralPath $LogFile -Value $line -Encoding utf8 } catch { }
    }
}

# Every live descendant pid of $rootPid, breadth-first over Win32_Process's
# ParentProcessId. Windows reuses pids, so the walk also refuses any child whose
# creation time precedes its parent's -- a recycled pid that merely looks like a
# descendant.
function Get-DescendantProcesses {
    param([int]$rootPid)
    $all = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Select-Object ProcessId, ParentProcessId, Name, CreationDate)
    if ($all.Count -eq 0) { return @() }
    $byParent = @{}
    foreach ($p in $all) {
        $key = [int]$p.ParentProcessId
        if (-not $byParent.ContainsKey($key)) { $byParent[$key] = New-Object System.Collections.ArrayList }
        $null = $byParent[$key].Add($p)
    }
    $byPid = @{}
    foreach ($p in $all) { $byPid[[int]$p.ProcessId] = $p }

    $out = New-Object System.Collections.ArrayList
    $seen = New-Object 'System.Collections.Generic.HashSet[int]'
    $queue = New-Object System.Collections.Queue
    $queue.Enqueue([int]$rootPid)
    $null = $seen.Add([int]$rootPid)
    while ($queue.Count -gt 0) {
        $cur = [int]$queue.Dequeue()
        if (-not $byParent.ContainsKey($cur)) { continue }
        $parentCreated = $null
        if ($byPid.ContainsKey($cur)) { $parentCreated = $byPid[$cur].CreationDate }
        foreach ($child in $byParent[$cur]) {
            $cpid = [int]$child.ProcessId
            if ($seen.Contains($cpid)) { continue }
            if ($null -ne $parentCreated -and $null -ne $child.CreationDate -and $child.CreationDate -lt $parentCreated) {
                continue  # recycled pid: "child" predates its parent
            }
            $null = $seen.Add($cpid)
            $null = $out.Add($child)
            $queue.Enqueue($cpid)
        }
    }
    return $out
}

function Read-TrackedPids {
    if (-not (Test-Path -LiteralPath $PidFile)) { return @() }
    try {
        return @(Get-Content -LiteralPath $PidFile -ErrorAction Stop |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ -match '^\d+$' } |
            ForEach-Object { [int]$_ } |
            Select-Object -Unique)
    } catch { return @() }
}

function Invoke-Reap {
    $tracked = Read-TrackedPids
    if ($tracked.Count -eq 0) { return 0 }
    $killed = 0
    foreach ($tpid in $tracked) {
        $proc = Get-Process -Id $tpid -ErrorAction SilentlyContinue
        if ($null -eq $proc) { continue }
        try {
            Stop-Process -Id $tpid -Force -ErrorAction Stop
            $killed++
            Write-ReapLog ("killed orphaned agent pid={0}" -f $tpid)
        } catch {
            Write-ReapLog ("could not kill pid={0}: {1}" -f $tpid, $_.Exception.Message)
        }
    }
    if ($killed -gt 0) {
        Write-ReapLog ("reaped {0} orphaned agent process(es) that outlived the run" -f $killed)
    }
    return $killed
}

if ($Reap) {
    $null = Invoke-Reap
    try { Remove-Item -LiteralPath $PidFile -Force -ErrorAction SilentlyContinue } catch { }
    exit 0
}

# ---------------------------------------------------------------- watch mode
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}
Set-Content -LiteralPath $PidFile -Value "" -Encoding utf8

# The progress signal: the newest of (a) any commit on a wave/* branch and
# (b) any file written under worktrees/. A task doing real work moves one of
# them long before the stall threshold; a run with every worktree torn down and
# nothing left to write moves neither.
function Get-ProgressStamp {
    $newest = [datetime]::MinValue
    try {
        $refs = & git -C $RepoRoot for-each-ref --sort=-committerdate --format='%(committerdate:iso8601)' 'refs/heads/wave/' 2>$null
        if ($LASTEXITCODE -eq 0 -and $refs) {
            $first = @($refs)[0]
            if ($first) {
                $parsed = [datetime]::MinValue
                if ([datetime]::TryParse($first, [ref]$parsed) -and $parsed -gt $newest) { $newest = $parsed }
            }
        }
    } catch { }
    $wt = Join-Path $RepoRoot "worktrees"
    if (Test-Path -LiteralPath $wt) {
        try {
            $f = Get-ChildItem -LiteralPath $wt -Recurse -File -Force -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '\\\.git\\' } |
                Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($null -ne $f -and $f.LastWriteTime -gt $newest) { $newest = $f.LastWriteTime }
        } catch { }
    }
    return $newest
}

$trackedSet = New-Object 'System.Collections.Generic.HashSet[int]'
$lastProgress = Get-ProgressStamp
$lastProgressSeen = Get-Date
$stallEnabled = $StallMinutes -gt 0
Write-ReapLog ("watching pid={0} (stall abort: {1})" -f $RootPid, $(if ($stallEnabled) { "$StallMinutes min" } else { "off" }))

while ($true) {
    Start-Sleep -Seconds $PollSeconds

    $engineAlive = $null -ne (Get-Process -Id $RootPid -ErrorAction SilentlyContinue)

    $added = 0
    foreach ($d in Get-DescendantProcesses -rootPid $RootPid) {
        if ($d.Name -notlike "claude*") { continue }
        $dpid = [int]$d.ProcessId
        if ($trackedSet.Add($dpid)) {
            Add-Content -LiteralPath $PidFile -Value $dpid -Encoding utf8
            $added++
        }
    }
    if ($added -gt 0) {
        Write-ReapLog ("tracking {0} agent process(es) (+{1})" -f $trackedSet.Count, $added)
    }

    if (-not $engineAlive) {
        Write-ReapLog "engine exited — collecting anything that outlived it"
        $null = Invoke-Reap
        break
    }

    if ($stallEnabled) {
        $now = Get-ProgressStamp
        if ($now -gt $lastProgress) {
            $lastProgress = $now
            $lastProgressSeen = Get-Date
        } elseif (((Get-Date) - $lastProgressSeen).TotalMinutes -ge $StallMinutes) {
            Write-ReapLog ("no commit on wave/* and no write under worktrees/ for {0} min — aborting the run" -f $StallMinutes)
            try {
                & taskkill /F /T /PID $RootPid 2>&1 | Out-Null
            } catch {
                Write-ReapLog ("taskkill failed for pid={0}: {1}" -f $RootPid, $_.Exception.Message)
            }
            Start-Sleep -Seconds 3
            $null = Invoke-Reap
            break
        }
    }
}
Write-ReapLog "done"
exit 0
