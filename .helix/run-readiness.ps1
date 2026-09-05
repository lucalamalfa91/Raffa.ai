# Launch the DEMO-READINESS process only. Never points at contigo-process.yaml.
# Refuses --fresh and -Slice (live e05 fan-out stays on the other process).
param(
    [switch]$Check,
    [Alias("orchestration")][string]$o = "contigo-readiness-design",
    [Alias("input")][string]$i = "Contigo demo-readiness: epic-10 / e10 residuals after wave 9",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Rest
)

$ErrorActionPreference = "Stop"
$Here = $PSScriptRoot
$Artifact = Join-Path $Here "contigo-readiness-process.yaml"
$envFile = Join-Path $Here ".env"

if (-not (Test-Path $envFile)) {
    throw "missing .env -- copy .env.example to .env and fill values"
}
if (-not (Test-Path $Artifact)) {
    throw "missing contigo-readiness-process.yaml"
}

foreach ($a in @($Rest)) {
    if ($a -eq "--fresh" -or $a -eq "-Fresh") {
        throw "run-readiness.ps1 refuses --fresh (would wipe the live plan this delta sits on)"
    }
    if ($a -eq "--slice" -or $a -eq "-Slice") {
        throw "run-readiness.ps1 has no fan-out. After gap-report HITL + e09 + e06-e08: ./run.ps1 -Max -Slice e10 -o execution-fanout"
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

$readinessOrchs = @(
    "contigo-readiness-design", "docs-intake-readiness", "architecture-council-readiness",
    "architecture-lanes-readiness", "council-close-readiness", "decomposition-readiness",
    "decomposition-check-readiness", "decomposition-remediation-readiness"
)
if ($readinessOrchs -notcontains $o) {
    throw "run-readiness.ps1 only launches readiness orchs (got '$o'). Default is contigo-readiness-design."
}

if ($Check) {
    & python (Join-Path $Here "scripts\validate-artifact.py") $Artifact --helix-backend $backend
    exit $LASTEXITCODE
}

$assert = Join-Path $Here "scripts\assert_readiness_plan_untouched.py"
Write-Host "artifact: contigo-readiness-process.yaml  orch: $o"
Write-Host "protect: e01-e05, wave-spec.execution.yaml, ADR-001..021, epic-01..09 (not slice.current.yaml)"
& python $assert snapshot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$passArgs = @("-o", $o)
if ($i) { $passArgs += @("-i", $i) }

Set-Location $backend
$helixExe = Join-Path $backend ".venv\Scripts\helix.exe"
$runRc = 1
if (Get-Command uv -ErrorAction SilentlyContinue) {
    $null = & uv --version 2>&1
    if ($LASTEXITCODE -eq 0) {
        & uv run helix run $Artifact @passArgs
        $runRc = $LASTEXITCODE
    } elseif (Test-Path $helixExe) {
        Write-Host "[run-readiness.ps1] uv shim broken; using helix.exe"
        & $helixExe run $Artifact @passArgs
        $runRc = $LASTEXITCODE
    } else {
        throw "neither a working uv nor helix.exe is available"
    }
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
