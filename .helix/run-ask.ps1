# Launch the ASK V2 process only. Never points at contigo-process.yaml.
# Refuses --fresh and -Slice (live fan-out stays on the other process).
# Oracle: inputs/requirements.md + inputs/design/prototypes/Contigo V2 Prototype.html.
param(
    [switch]$Check,
    [switch]$Max,
    [Alias("orchestration")][string]$o = "contigo-ask-design",
    [Alias("input")][string]$i = "Contigo Ask V2 (inputs/requirements.md): epic-13 / e13 replaces epic-12 / e12; ADR-024 supersedes ADR-023; verify-or-write the authored outputs",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Rest
)

$ErrorActionPreference = "Stop"
$Here = $PSScriptRoot
$Artifact = Join-Path $Here "contigo-ask-process.yaml"
$envFile = Join-Path $Here ".env"

if (-not (Test-Path $envFile)) {
    throw "missing .env -- copy .env.example to .env and fill values"
}
if (-not (Test-Path $Artifact)) {
    throw "missing contigo-ask-process.yaml"
}

foreach ($a in @($Rest)) {
    if ($a -eq "--fresh" -or $a -eq "-Fresh") {
        throw "run-ask.ps1 refuses --fresh (would wipe the live plan this delta sits on)"
    }
    if ($a -eq "--slice" -or $a -eq "-Slice") {
        throw "run-ask.ps1 has no fan-out. After ADR-024 HITL: Studio -> contigo-process.yaml -> execution-fanout (slice.current.yaml = e13), or ./run.ps1 -Max -Slice e13 -o execution-fanout"
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

# Passata 1 (Ask V2) runs on Claude Code Opus (operator decision 2026-09-08):
# the artifact binds model ${ANTHROPIC_DEFAULT_OPUS_MODEL}. Same billing
# posture as passata 2 (PROCESS.md D11): Max login, never Console API.
foreach ($v in @("ANTHROPIC_DEFAULT_OPUS_MODEL")) {
    $item = Get-Item ("Env:" + $v) -ErrorAction SilentlyContinue
    if ($null -eq $item -or [string]::IsNullOrWhiteSpace($item.Value)) {
        throw "unset $v in .env (Claude Code Opus id, e.g. claude-opus-5)"
    }
}
if ($Max) {
    Write-Host "[run-ask.ps1] -Max: blanking Hub URL/token so Claude Code uses Max login"
    foreach ($name in @("ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_BASE_URL")) {
        Set-Item -Path ("Env:" + $name) -Value ""
    }
}
elseif (-not $Check -and -not [string]::IsNullOrWhiteSpace($env:ANTHROPIC_API_KEY)) {
    throw "ANTHROPIC_API_KEY is set. Passata 1 on Claude Code Opus bills the Max login, not Console API. Unset it or pass -Max."
}

$askOrchs = @(
    "contigo-ask-design", "docs-intake-ask", "ask-adr-gate",
    "decomposition-ask", "decomposition-check-ask", "decomposition-remediation-ask"
)
if ($askOrchs -notcontains $o) {
    throw "run-ask.ps1 only launches ask orchs (got '$o'). Default is contigo-ask-design."
}

if ($Check) {
    & python (Join-Path $Here "scripts\validate-artifact.py") $Artifact --helix-backend $backend
    exit $LASTEXITCODE
}

$assert = Join-Path $Here "scripts\assert_ask_plan_untouched.py"
Write-Host "artifact: contigo-ask-process.yaml  orch: $o"
Write-Host "protect: e01-e11 e1011 e12, prior wave-specs, locked ADRs incl. ADR-023 (superseded), epic-01..12 (ADR-001/004/011/018/020 footers + ADR-024 + epic-13 writable)"
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
        Write-Host "[run-ask.ps1] uv shim broken; using helix.exe"
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
