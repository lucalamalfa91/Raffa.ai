---
id: us-01
type: user-story
parent: feature-01
wave: 13
status: active
---

# us-01-v2-foundation — Live Foundry behind `IAiGateway`, on a solution ready for V2

## Story

As **procurement**, I want extraction, classification and Ask to run on a
real Foundry model when the environment is wired (fixture otherwise), so
that answers and admission decisions are produced by a model — never a
chunk echo or a keyword match — while the solution already has the V2
modules every later task fills in.

## Acceptance criteria

- [ ] AC-1 With `AiGateway:Endpoint` set, `IAiGateway` resolves to a
      Foundry-backed implementation wrapped by `LoggingAiGateway`; with the
      endpoint unset, the fixture resolves, still wrapped.
- [ ] AC-2 The `answer` request sent to Foundry carries no `tools`,
      grounding or browsing payload (asserted on a fake HTTP handler), and
      returns the structured JSON of `inputs/requirements.md` R-ASK-05;
      `classify` returns one label of a fixed set plus confidence.
- [ ] AC-3 No Azure AI SDK reference exists outside `Raffa.AiGateway`.
- [ ] AC-4 `backend/Raffa.slnx` contains `Raffa.Market`, `Raffa.Insights`,
      their test projects, `Raffa.Suppliers.Products.Tests` and
      `Raffa.AiEval`; `dotnet build Raffa.slnx` is green; the
      architecture allow-list names Market → `[SharedKernel, AiGateway,
      Benchmark]` and Insights → `[SharedKernel, Benchmark]`.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| — | First e13 backend tasks (phase 1) |

## Architecture decisions in force

- ADR-024 — module map delta; no tools on the answer role
- ADR-002 — one project per bounded context; dependency direction test
- ADR-004 (amended) — five roles, structured output, always log-wrapped
- ADR-011 — no-training endpoint, hash-only logging

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| T01 | V2 solution scaffold (slnx, projects, allow-list) | M | phase-1 |
| T02 | `FoundryAiGateway` + DI swap + no-tools compliance | L | phase-1 |

## Council decisions carried into this story

Model ids per role from `AiGatewayModelOptions` (`gpt-4o-mini` classify /
extract / answer, `text-embedding-3-small` embed, `prebuilt-read` OCR —
confirmed in region by cloud-architect). Endpoint / project / Document
Intelligence connection injected by Container Apps as `AiGateway__Endpoint`,
`AiGateway__ProjectName`, `AiGateway__DocumentIntelligenceConnection`
(`infra/modules/containerapps/main.tf`). Managed identity, never a key in
config.

## Open questions

- OQ-askv2-009 — live Foundry on `dev` / `demo` for acceptance; fixture in CI (assumed)
