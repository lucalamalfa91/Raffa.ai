# Raffa web-delta process (wave 6+)

Separate Helix artifact from the live R0–R4 process.

| File | Role |
|---|---|
| `raffa-process.yaml` + `./run.ps1` | **Do not touch while a wave is running.** Backend slices e01–e05 / `execution-fanout`. |
| `raffa-web-process.yaml` + `./run-web.ps1` | Web delta Passata 1 only. Starts at **epic-06 / e06**. |

## Launch (after the live fan-out is idle *or* in parallel — this YAML never writes `slice.current.yaml`)

```powershell
cd .helix
# HITL first: export Claude Design prototypes into inputs/design/prototypes/
./run-web.ps1 -Check
./run-web.ps1
# default -o raffa-web-design
```

`--fresh` is refused. So are `-Slice` / fan-out.

## What it reads

`inputs/product-spec.md`, `engineering-brief.md`, `engineering-constraints.md`,
`web-integration-brief.md`, plus on-disk ADR-001…017 and epic-01…05 (**not rewritten**).

## What it writes

- `reports/context/web-integration-mandate.md`
- `reports/architecture/draft/ux-ui-designer/` and `draft/*-web/`
- `ADR-018+` (INDEX **appended**)
- `reports/workitems/epic-06-*`
- `reports/plan/wave-spec.web.yaml`
- `reports/plan/slices/e06.yaml` (+ e07…), `INDEX-web.md`, `MANIFEST-web.yaml`

## What it never writes

`wave-spec.execution.yaml`, `slices/e01.yaml`–`e05.yaml`, `slice.current.yaml`.

## After `DECOMPOSITION_OK:`

When the backend wave is idle:

```powershell
./run.ps1 -Max -Slice e06 -o execution-fanout
```

## Claude Design

Council cannot open claude.ai/design. Gate OBJECTs if `inputs/design/prototypes/`
is empty. Do that HITL **before** or **during** the UX lane.
