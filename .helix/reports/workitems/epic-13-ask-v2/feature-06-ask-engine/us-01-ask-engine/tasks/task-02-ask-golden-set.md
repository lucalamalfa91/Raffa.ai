---
id: E13/F06/US01/T02
type: task
story: us-01-ask-engine
wave: 13
status: live
target_repo: contigo-backend
---

# task-02-ask-golden-set — AI golden set on the fixture gateway (`Contigo.AiEval`)

## Coding objective

Fill `backend/tests/Contigo.AiEval` (scaffolded by F01/T01) with the
evaluation set of `inputs/requirements.md` R-EVD-03 and spec §15.3: a
data-driven xunit suite over `backend/tests/Contigo.AiEval/golden/*.json`
— ≥ 40 questions (Italian and English) × 3 tenant fixtures (an empty
workspace; a seeded workspace with Salesforce / Microsoft / AWS / DocuSign
validated contracts mirroring `inputs/design/prototypes/contigo-v2/app.jsx`
`CONTRACTS`; a workspace with one document still in `needs_review`) —
each case with the expected reply `kind`, the expected citation corpora,
the expected numbers (dates, amounts, percentages that must appear
verbatim), forbidden substrings (`Document:`, guids, "Structured query",
"not wired") and, where relevant, the expected action hrefs. Cases cover
the prototype intents (`contigo-v2/ia-v2.md` "Ask intents": expire /
notice / liability / 120 days / askable / top / saving / benchmark /
capabilities / unknown supplier / legal fees) plus the V2 additions
(greeting, carbonara, legal, Allianz vs market, renewal strategy,
portfolio criticality, "cosa sai fare?", "come faccio a rivedere i campi
deboli?"). The suite runs the full pipeline (`AskCopilotService`) with the
**fixture gateway**, in-memory / sqlite stores as the module tests do,
and asserts: kinds match, numbers equal the calculator outputs, zero
`NumericGuard` / `GroundingGuard` interventions across the set, no
forbidden substring, every action href resolves to a catalog route. Add a
`--filter Category=AiEval` step to `.github/workflows/backend.yml`? — no:
`dotnet test Contigo.slnx` already runs it; instead mark the suite with
`[Trait("Category","AiEval")]` and document the manual Foundry run
(`AiEval__UseFoundry=true` env switch reading `AiGateway:Endpoint`) in
`backend/README.md`. Emit a markdown report
(`backend/tests/Contigo.AiEval/reports/last-run.md`, git-ignored) with
per-case verdicts to help HITL on `demo`.

## Parent story AC covered
- AC-9

## Files to create or modify
| Path | Change |
|------|--------|
| `backend/tests/Contigo.AiEval/golden/*.json`, `GoldenCase.cs`, `GoldenSetTests.cs`, `TenantFixtures/*`, `AiEvalOptions.cs` | new |
| `backend/tests/Contigo.AiEval/Contigo.AiEval.csproj` | references (Api, Chat, Insights, Market, Documents, AiGateway) as needed for the composition root |
| `backend/.gitignore` (or root) | ignore `tests/Contigo.AiEval/reports/` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 (guards; 0 interventions on the golden set), ADR-004 (fixture gateway proves the paths), spec §15.3.
- Gap G-GOLDEN-SET. The fixture `AnswerAsync` v2 behaviour (F06/T01) is deterministic — the set proves the pipeline, guards and calculators, not the model.
- **Do not touch**: any `src/` file (if the pipeline needs a change, HALT and name it), `web/`, workflows.

## Definition of done
- [ ] `dotnet test backend/tests/Contigo.AiEval` exit 0 — ≥ 40 cases pass; report written
- [ ] `dotnet test backend/Contigo.slnx` exit 0
- [ ] `backend/README.md` documents the manual Foundry run switch

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| AI eval | kinds, numbers, citations, no chrome, 0 guard interventions | `Contigo.AiEval/GoldenSetTests.cs` |

## Open questions blocking this task
- OQ-askv2-006 — Italian and English cases (assumed)
- OQ-askv2-009 — Foundry run is manual (assumed)

## Wave-spec entry
```yaml
- id: E13/F06/US01/T02
  prompt: reports/workitems/epic-13-ask-v2/feature-06-ask-engine/us-01-ask-engine/tasks/task-02-ask-golden-set.md
  produces: [ask-golden-set]
  depends_on: [ask-engine]
  effort: M
  layer: backend
  status: live
```
