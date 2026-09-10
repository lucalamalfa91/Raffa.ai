# Launch the WEB DELTA process only. Never points at raffa-process.yaml.
# Does not accept --fresh (would wipe ADR-001…017 / epic-01…05).
# Does not copy slice.current.yaml (live fan-out stays on the other process).
param(
    [switch]$Check,
    [Alias("orchestration")][string]$o = "raffa-web-design",
    [Alias("input")][string]$i = "Raffa web delta: wave 6+ from existing R0-R4 plan",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Rest
)

$ErrorActionPreference = "Stop"
$Here = $PSScriptRoot
$Artifact = Join-Path $Here "raffa-web-process.yaml"
$envFile = Join-Path $Here ".env"

if (-not (Test-Path $envFile)) {
    throw "missing .env -- copy .env.example to .env and fill values"
}
if (-not (Test-Path $Artifact)) {
    throw "missing raffa-web-process.yaml"
}

foreach ($a in @($Rest)) {
    if ($a -eq "--fresh" -or $a -eq "-Fresh") {
        throw "run-web.ps1 refuses --fresh (would wipe the backend plan this delta sits on)"
    }
    if ($a -eq "--slice" -or $a -eq "-Slice") {
        throw "run-web.ps1 has no fan-out. After e06 exists and the live wave is idle: ./run.ps1 -Max -Slice e06 -o execution-fanout"
    }
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

$webOrchs = @(
    "raffa-web-design", "docs-intake-web", "architecture-council-web",
    "architecture-lanes-web", "council-close-web", "decomposition-web",
    "decomposition-check-web", "decomposition-remediation-web"
)
if ($webOrchs -notcontains $o) {
    throw "run-web.ps1 only launches web orchs (got '$o'). Default is raffa-web-design. Never execution-fanout / raffa-design."
}

if ($Check) {
    & python (Join-Path $Here "scripts\validate-artifact.py") $Artifact --helix-backend $backend
    exit $LASTEXITCODE
}

$assert = Join-Path $Here "scripts\assert_plan_untouched.py"
Write-Host "artifact: raffa-web-process.yaml  orch: $o"
Write-Host "protect: e01-e05, wave-spec.execution.yaml, ADR-001..017, epic-01..05 (not slice.current.yaml)"
& python $assert snapshot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$passArgs = @("-o", $o)
if ($i) { $passArgs += @("-i", $i) }

Set-Location $backend
$helixExe = Join-Path $backend ".venv\Scripts\helix.exe"
$runRc = 1
if (Get-Command uv -ErrorAction SilentlyContinue) {
    & uv run helix run $Artifact @passArgs
    $runRc = $LASTEXITCODE
} elseif (Test-Path $helixExe) {
    & $helixExe run $Artifact @passArgs
    $runRc = $LASTEXITCODE
} else {
    throw "neither uv nor helix.exe is available"
}

Set-Location $Here
& python $assert verify
$verifyRc = $LASTEXITCODE
if ($verifyRc -ne 0) { exit $verifyRc }
exit $runRc
