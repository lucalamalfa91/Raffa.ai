# raffa-flow Windows launcher. Prefer this in PowerShell; run.sh needs Git Bash.
param(
    [switch]$Check,
    [switch]$Fresh,
    [switch]$Max,
    [Alias("orchestration")][string]$o,
    [Alias("input")][string]$i,
    # Do not Alias("slice"): PowerShell is case-insensitive, so the alias
    # collides with -Slice and the script cannot be invoked at all.
    [string]$Slice,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Rest
)

$ErrorActionPreference = "Stop"
$Here = $PSScriptRoot
$Artifact = Join-Path $Here "raffa-process.yaml"
$envFile = Join-Path $Here ".env"

if (-not (Test-Path $envFile)) {
    throw "missing .env -- copy .env.example to .env and fill values"
}

Get-Content -LiteralPath $envFile -Encoding utf8 | ForEach-Object {
    $line = $_.Trim()
    if ($line -eq "" -or $line.StartsWith("#")) { return }
    $eq = $line.IndexOf("=")
    if ($eq -lt 1) { return }
    $name = $line.Substring(0, $eq).Trim()
    $value = $line.Substring($eq + 1).Trim()
    if ($value.Length -ge 2) {
        $q = $value[0]
        if (($q -eq [char]34 -or $q -eq [char]39) -and $value[-1] -eq $q) {
            $value = $value.Substring(1, $value.Length - 2)
        }
    }
    if ($name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { return }
    Set-Item -Path ("Env:" + $name) -Value $value
}

$env:PYTHONUTF8 = "1"

$backend = $env:HELIX_BACKEND
if ([string]::IsNullOrWhiteSpace($backend)) {
    $backend = (Resolve-Path (Join-Path $Here "..\..\..\helix\src\backend")).Path
}

$required = @(
    "DEEPSEEK_BASE_URL", "DEEPSEEK_API_KEY", "DEEPSEEK_REASONING_MODEL",
    "DEEPSEEK_FAST_MODEL",
    "ANTHROPIC_DEFAULT_SONNET_MODEL"
)
$missing = @()
foreach ($v in $required) {
    $item = Get-Item ("Env:" + $v) -ErrorAction SilentlyContinue
    if ($null -eq $item -or [string]::IsNullOrWhiteSpace($item.Value)) {
        $missing += $v
    }
}
if ($missing.Count -gt 0) {
    Write-Host "ERROR: unset variables in .env"
    $missing | ForEach-Object { Write-Host ("  - " + $_) }
    exit 1
}

$extraArgs = @()
$expectSlice = $false
foreach ($a in @($Rest)) {
    if ($a -eq "--check") { $Check = $true }
    elseif ($a -eq "--fresh") { $Fresh = $true }
    elseif ($a -eq "--max") { $Max = $true }
    elseif ($a -like "*.yaml") { $Artifact = Join-Path $Here $a }
    elseif ($a -like "--slice=*") { $Slice = $a.Substring(8) }
    elseif ($a -eq "--slice" -or $a -eq "-Slice") { $expectSlice = $true }
    elseif ($expectSlice) { $Slice = $a; $expectSlice = $false }
    else { $extraArgs += $a }
}

if ($Max) {
    # Present-but-empty: Helix load_dotenv(override=False) will not refill Hub
    # URL/token from this .env. Remove-Item lets the file win again.
    Write-Host "[run.ps1] -Max: blanking Hub URL/token so Claude Code uses Max login"
    foreach ($name in @("ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_BASE_URL")) {
        Set-Item -Path ("Env:" + $name) -Value ""
    }
}
elseif (-not [string]::IsNullOrWhiteSpace($env:ANTHROPIC_API_KEY)) {
    throw "ANTHROPIC_API_KEY is set. Passata 2 bills Claude Code Max (claude login), not Console API. Unset it or pass -Max."
}

if (-not [string]::IsNullOrWhiteSpace($Slice)) {
    $Slice = $Slice.ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($o)) { $o = "execution-fanout" }
}

if ($o -eq "execution-fanout") {
    if ([string]::IsNullOrWhiteSpace($Slice)) {
        Write-Host "ERROR: execution-fanout needs -Slice <id> (one slice wave-spec)."
        Write-Host "  See reports/plan/slices/INDEX.md  e.g.  ./run.ps1 -Max -Slice r0-a -o execution-fanout"
        exit 1
    }
    $src = Join-Path $Here ("reports\plan\slices\" + $Slice + ".yaml")
    $dst = Join-Path $Here "reports\plan\slice.current.yaml"
    if (-not (Test-Path $src)) {
        throw ("unknown slice '" + $Slice + "' -- missing " + $src)
    }
    Copy-Item -LiteralPath $src -Destination $dst -Force
    Write-Host ("[run.ps1] slice {0} -> slice.current.yaml" -f $Slice)
    if (-not $Check) {
        Write-Host "[run.ps1] ensure local clone is a git toplevel (fan-out worktrees)"
        & python (Join-Path $Here "scripts\ensure_artifact_git.py")
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}

$passArgs = @()
if ($o) { $passArgs += @("-o", $o) }
if ($i) { $passArgs += @("-i", $i) }
$passArgs += $extraArgs

if ($Fresh) {
    Write-Host "[run.ps1] --fresh: clearing design outputs"
    foreach ($name in @("context", "architecture", "workitems", "costs", "briefing", "audit")) {
        $p = Join-Path $Here ("reports\" + $name)
        if (Test-Path $p) { Remove-Item $p -Recurse -Force }
    }
    $oq = Join-Path $Here "reports\open-questions.md"
    if (Test-Path $oq) { Remove-Item $oq -Force }
    foreach ($d in @("context", "architecture\draft", "workitems", "plan", "costs", "briefing", "audit")) {
        New-Item -ItemType Directory -Force -Path (Join-Path $Here ("reports\" + $d)) | Out-Null
    }
    $ws = Join-Path $Here "reports\plan\wave-spec.execution.yaml"
    Set-Content -Encoding ascii $ws "waveId: placeholder`nstatus: planned`nphases: []`nforks: []`n"
    $cur = Join-Path $Here "reports\plan\slice.current.yaml"
    Set-Content -Encoding ascii $cur "waveId: slice-unset`nstatus: planned`nphases: []`nforks: []`n"
}

function Test-UvWorks {
    if (-not (Get-Command uv -ErrorAction SilentlyContinue)) { return $false }
    try {
        $null = & uv --version 2>&1
        return ($LASTEXITCODE -eq 0)
    }
    catch {
        return $false
    }
}

$helixExe = Join-Path $backend ".venv\Scripts\helix.exe"
$uvWorks = Test-UvWorks
if (-not $uvWorks -and (Get-Command uv -ErrorAction SilentlyContinue)) {
    Write-Host "[run.ps1] uv shim is present but broken; using helix.exe"
}

if ($Check) {
    & python (Join-Path $Here "scripts\validate-artifact.py") $Artifact --helix-backend $backend
    exit $LASTEXITCODE
}

if (-not (Test-Path $backend)) {
    throw ("Helix backend not found: " + $backend)
}
if (-not $uvWorks -and -not (Test-Path $helixExe)) {
    throw "neither a working uv nor helix.exe is available"
}

Set-Location $backend

function Invoke-Helix {
    if ($uvWorks) {
        & uv run helix run $Artifact @passArgs
        return $LASTEXITCODE
    }
    if (Test-Path $helixExe) {
        & $helixExe run $Artifact @passArgs
        return $LASTEXITCODE
    }
    throw "neither a working uv nor helix.exe is available"
}

$rc = Invoke-Helix
exit $rc
