# Launch the NEXT-WAVE process (contigo-next-process.yaml): raw requirements
# file -> normalized requirements -> dynamic council (ADRs) -> new epics ->
# ONE wave (reports/plan/slices/<wave>.yaml). Passata 1 only; the wave itself
# runs on contigo-process.yaml (execution-fanout), chained here by -Launch.
#
#   ./run-next.ps1 -Check
#   ./run-next.ps1 -Max -Todo inputs/next/next-waves-todo.md
#   ./run-next.ps1 -Max -Wave w14 -MaxTasks 12 -Focus "only the Ask items"
#   ./run-next.ps1 -Max -Wave w14 -o next-from-council        # re-run after editing the normalized file
#   ./run-next.ps1 -Max -Wave w14 -o next-from-decomposition  # council closed on disk: decomposer + checker <-> remediator
#   ./run-next.ps1 -Max -Wave w14 -o next-plan-close          # checker <-> remediator only
#   ./run-next.ps1 -LaunchOnly -Wave w14                    # HITL done: prereqs + execution-fanout
#
# Refuses --fresh and -Slice. Never writes slice.current.yaml itself.
param(
    [switch]$Check,
    [switch]$Max,
    [switch]$Launch,
    [switch]$LaunchOnly,
    [string]$Todo,
    [string]$Wave,
    [int]$MaxTasks = 20,
    [int]$MaxPhases = 5,
    [string]$Focus = "",
    [Alias("orchestration")][string]$o = "next-design",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Rest
)

$ErrorActionPreference = "Stop"
$Here = $PSScriptRoot
$Artifact = Join-Path $Here "contigo-next-process.yaml"
$envFile = Join-Path $Here ".env"

if (-not (Test-Path $envFile)) { throw "missing .env -- copy .env.example to .env and fill values" }
if (-not (Test-Path $Artifact)) { throw "missing contigo-next-process.yaml" }

foreach ($a in @($Rest)) {
    if ($a -eq "--fresh" -or $a -eq "-Fresh") { throw "run-next.ps1 refuses --fresh (the next-wave process is append-only on the live plan)" }
    if ($a -eq "--slice" -or $a -eq "-Slice") { throw "run-next.ps1 has no fan-out of its own: use -Launch / -LaunchOnly, or ./run.ps1 -Max -Slice <wave> -o execution-fanout" }
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
        if (($q -eq [char]34 -or $q -eq [char]39) -and $value[-1] -eq $q) { $value = $value.Substring(1, $value.Length - 2) }
    }
    if ($name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { return }
    Set-Item -Path ("Env:" + $name) -Value $value
}

$env:PYTHONUTF8 = "1"
$backend = $env:HELIX_BACKEND
if ([string]::IsNullOrWhiteSpace($backend)) {
    $backend = (Resolve-Path (Join-Path $Here "..\..\..\helix\src\backend")).Path
}

if ($Check) {
    & python (Join-Path $Here "scripts\validate-artifact.py") $Artifact --helix-backend $backend
    exit $LASTEXITCODE
}

# Passata 1 runs on Claude Code Opus (operator decision 2026-09-08), billed to
# the Max login (PROCESS.md D11): blank the Hub/Console variables with -Max.
$opus = $env:ANTHROPIC_DEFAULT_OPUS_MODEL
if (-not $LaunchOnly) {
    if ([string]::IsNullOrWhiteSpace($opus)) { throw "unset ANTHROPIC_DEFAULT_OPUS_MODEL in .env (Claude Code Opus id, e.g. claude-opus-5)" }
    if ($opus -match '^claude-opus-4') { Write-Warning "ANTHROPIC_DEFAULT_OPUS_MODEL=$opus looks stale; the current Opus id is claude-opus-5" }
}
if ($Max) {
    Write-Host "[run-next.ps1] -Max: blanking Hub URL/token so Claude Code uses Max login"
    foreach ($name in @("ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_BASE_URL")) { Set-Item -Path ("Env:" + $name) -Value "" }
}
elseif (-not [string]::IsNullOrWhiteSpace($env:ANTHROPIC_API_KEY)) {
    throw "ANTHROPIC_API_KEY is set. Passata 1 on Claude Code Opus bills the Max login, not Console API. Unset it or pass -Max."
}

$allowed = @("next-design", "next-from-council", "next-from-decomposition", "next-plan-close", "next-intake-phase", "next-council", "next-decomposition", "next-check")
if ($allowed -notcontains $o) { throw "run-next.ps1 only launches next-wave orchestrations ($($allowed -join ', ')); got '$o'" }

Set-Location $Here

# --- wave id ------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($Wave)) {
    $Wave = (& python (Join-Path $Here "scripts\register_wave.py") --next-id).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($Wave)) { throw "could not compute the next wave id (scripts/register_wave.py --next-id)" }
}
$Wave = $Wave.ToLowerInvariant()
if ($Wave -notmatch '^[ew]\d{1,3}$') { throw "wave id must look like w14 (got '$Wave')" }
$Previous = (& python (Join-Path $Here "scripts\register_wave.py") --last-id).Trim()
if ($Previous -eq $Wave) { $Previous = "" }

function Invoke-Helix([string[]]$passArgs) {
    $helixExe = Join-Path $backend ".venv\Scripts\helix.exe"
    $uvWorks = $false
    if (Get-Command uv -ErrorAction SilentlyContinue) {
        try { $null = & uv --version 2>&1; $uvWorks = ($LASTEXITCODE -eq 0) } catch { $uvWorks = $false }
    }
    Push-Location $backend
    try {
        if ($uvWorks) { & uv run helix run $Artifact @passArgs; return $LASTEXITCODE }
        if (Test-Path $helixExe) { & $helixExe run $Artifact @passArgs; return $LASTEXITCODE }
        throw "neither a working uv nor helix.exe is available under $backend"
    }
    finally { Pop-Location }
}

# --- Passata 1 ------------------------------------------------------------------
$runRc = 0
if (-not $LaunchOnly) {
    if ([string]::IsNullOrWhiteSpace($Todo)) {
        $candidates = Get-ChildItem (Join-Path $Here "inputs\next") -Filter *.md -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ne "README.md" } | Sort-Object Name -Descending
        if (-not $candidates) { throw "no raw requirements file under inputs/next/ (drop one, e.g. inputs/next/2026-09-12-demo.md, or pass -Todo)" }
        $Todo = "inputs/next/" + $candidates[0].Name
    }
    $todoPath = Join-Path $Here $Todo
    if (-not (Test-Path $todoPath)) { throw "raw requirements file not found: $Todo" }
    $Todo = $Todo -replace '\\', '/'

    $params = [ordered]@{
        wave        = $Wave
        todo        = $Todo
        max_tasks   = $MaxTasks
        max_phases  = $MaxPhases
        focus       = $Focus
        previous    = $Previous
        orchestration = $o
        started_at  = (Get-Date).ToUniversalTime().ToString("s") + "Z"
    }
    $runJson = Join-Path $Here "reports\plan\next-run.json"
    ($params | ConvertTo-Json -Compress) | Set-Content -Encoding ascii -LiteralPath $runJson
    Write-Host "artifact: contigo-next-process.yaml  orch: $o  wave: $Wave  previous: $Previous"
    Write-Host "todo: $Todo  caps: $MaxTasks tasks / $MaxPhases phases  focus: '$Focus'"

    $protect = Join-Path $Here "scripts\assert_next_plan_untouched.py"
    & python $protect snapshot --wave $Wave
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # Council re-entry: the table's close gate needs reports/architecture/waves/<w>.md
    # with mtime >= run start (NEXT-PROCESS.md D-N2). Touch it so an empty wave,
    # where no seat rewrites the record, still closes.
    if ($o -eq "next-from-council" -or $o -eq "next-council") {
        $record = Join-Path $Here ("reports\architecture\waves\" + $Wave + ".md")
        if (-not (Test-Path $record)) { throw "cannot re-enter the council: $record does not exist (run next-design first)" }
        (Get-Item -LiteralPath $record).LastWriteTime = Get-Date
        Write-Host "[run-next.ps1] touched $record for the council close gate"
    }

    $helixInput = "wave=$Wave todo=$Todo max_tasks=$MaxTasks max_phases=$MaxPhases previous=$Previous focus=$Focus"
    $runRc = Invoke-Helix @("-o", $o, "-i", $helixInput)

    Set-Location $Here
    & python $protect verify --wave $Wave
    $verifyRc = $LASTEXITCODE
    if ($verifyRc -ne 0) {
        Write-Host "[run-next.ps1] protected plan mutated -- review `git status` before doing anything else"
        exit $verifyRc
    }

    $waveFile = Join-Path $Here ("reports\plan\slices\" + $Wave + ".yaml")
    if (Test-Path $waveFile) {
        $tasks = (Select-String -LiteralPath $waveFile -Pattern '\{id:' | Measure-Object).Count
        $phases = (Select-String -LiteralPath $waveFile -Pattern '^\s+- id: \d+\s*$' | Measure-Object).Count
        Write-Host ""
        Write-Host "[run-next.ps1] wave $Wave on disk: $tasks live tasks in $phases phases"
        Write-Host "  review : reports/context/waves/$Wave-requirements.md"
        Write-Host "           reports/architecture/waves/$Wave.md  (+ ADR footers, INDEX.md)"
        Write-Host "           reports/audit/$Wave-hitl.md  (single-writer table, queued tasks)"
        Write-Host "  launch : python scripts/check_slice_prereqs.py --slice $Wave"
        Write-Host "           ./run.ps1 -Max -Slice $Wave -o execution-fanout    (or ./run-next.ps1 -LaunchOnly -Wave $Wave)"
    }
    else {
        Write-Host "[run-next.ps1] no wave file reports/plan/slices/$Wave.yaml -- the run stopped before the decomposition closed (rc=$runRc)"
    }
    if ($runRc -ne 0 -and -not $Launch) { exit $runRc }
}

# --- Passata 2 hand-off ----------------------------------------------------------
if ($Launch -or $LaunchOnly) {
    $waveFile = Join-Path $Here ("reports\plan\slices\" + $Wave + ".yaml")
    if (-not (Test-Path $waveFile)) { throw "cannot launch: reports/plan/slices/$Wave.yaml does not exist" }
    $prereqs = Join-Path $Here "scripts\check_slice_prereqs.py"
    if (-not [string]::IsNullOrWhiteSpace($Previous)) {
        $stamp = Join-Path $Here ("reports\plan\gates\" + $Previous + ".hitl-ok")
        if (-not (Test-Path $stamp)) {
            Write-Host "[run-next.ps1] recording HITL of the previous wave $Previous (you are launching $Wave after reviewing it)"
            & python $prereqs --record-hitl $Previous
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
    }
    & python $prereqs --slice $Wave
    if ($LASTEXITCODE -ne 0) { Write-Host "[run-next.ps1] prerequisites not met -- fix them, then ./run-next.ps1 -LaunchOnly -Wave $Wave"; exit $LASTEXITCODE }
    Write-Host "[run-next.ps1] launching execution-fanout on slice $Wave (contigo-process.yaml)"
    & (Join-Path $Here "run.ps1") -Max -Slice $Wave -o execution-fanout
    exit $LASTEXITCODE
}

exit $runRc
