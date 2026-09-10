#!/usr/bin/env python3
"""Generate missing task files from the master wave-spec."""
from pathlib import Path
import re

ROOT = Path("reports/workitems")
WS = Path("reports/plan/wave-spec.execution.yaml")

# task_id -> (story, wave, repo, adrs, objective)
META = {
"E01/F01/US01/T02": ("us-01-github-org-repo-protection","R0","raffa-infra",["ADR-014"],"Scan for committed secrets and verify five-folder layout + no-secret state."),
"E01/F01/US02/T02": ("us-02-hcp-terraform-workspaces","R0","raffa-infra",["ADR-007","ADR-014"],"Wire the two HCP workspaces to the raffa repo VCS and assert remote state only."),
"E01/F02/US01/T02": ("us-01-terraform-module-library","R0","raffa-infra",["ADR-007","ADR-005","ADR-006"],"Create the two env roots dev/demo with backend.tf pointing at HCP workspaces."),
"E01/F02/US02/T02": ("us-02-dev-environment","R0","raffa-infra",["ADR-005","ADR-006"],"Verify dev Terraform outputs expose resource ids/endpoints and tags applied."),
"E01/F02/US03/T02": ("us-03-demo-environment","R0","raffa-infra",["ADR-005","ADR-016"],"Assert demo uses distinct RG/store and no shared state with dev."),
"E01/F02/US04/T02": ("us-04-entra-keyvault","R0","raffa-infra",["ADR-010","ADR-011"],"Grant each env API/worker managed identity only its own Key Vault."),
"E01/F02/US05/T02": ("us-05-foundry-account","R0","raffa-infra",["ADR-008","ADR-006"],"Verify Foundry hub/projects availability in westeurope; record connection ids."),
"E01/F03/US01/T02": ("us-01-ci-azure-oidc","R0","raffa-infra",["ADR-015"],"Author a reusable azure/login OIDC step (no secret, only client/tenant/sub)."),
"E01/F03/US02/T02": ("us-02-per-folder-workflows","R0","raffa-infra",["ADR-014"],"Verify four workflows have correct path filters and mobile non-blocking."),
"E01/F04/US01/T02": ("us-01-dotnet-solution-shape","R0","raffa-backend",["ADR-002"],"Add an architecture test blocking domain->provider/domain-internals references."),
"E01/F03/US03/T02": ("us-03-promotion-dev-demo","R0","raffa-infra",["ADR-016"],"Document and lock the demo GitHub Environment required reviewers."),
"E01/F04/US02/T02": ("us-02-relational-store","R0","raffa-backend",["ADR-003"],"Apply initial EF Core migrations; prove pgvector vector column usable."),
"E01/F04/US03/T02": ("us-03-tenant-rls","R0","raffa-backend",["ADR-009"],"Add CI migration check rejecting tenant tables lacking an RLS policy."),
"E01/F04/US04/T01": ("us-04-deployable-api","R0","raffa-backend",["ADR-002"],"Create the thin API host composing modules with /health + Dockerfile."),
"E01/F04/US04/T02": ("us-04-deployable-api","R0","raffa-backend",["ADR-002"],"Create the thin worker host consuming the queue and shared app services."),
"E01/F05/US01/T01": ("us-01-workspace-roles","R0","raffa-backend",["ADR-009","ADR-003"],"Implement Workspace/User/Role/Membership with tenant_id and RLS."),
"E01/F05/US01/T02": ("us-01-workspace-roles","R0","raffa-backend",["ADR-010","ADR-009"],"Implement workspace invite + role assignment with OIDC claims."),
"E01/F06/US01/T01": ("us-01-document-upload","R0","raffa-backend",["ADR-009","ADR-011"],"Implement POST /api/documents to tenant-scoped blob + processing job."),
"E01/F06/US01/T02": ("us-01-document-upload","R0","raffa-backend",["ADR-003"],"Persist document metadata/status; GET /api/documents/{id}."),
"E01/F06/US02/T01": ("us-02-audit-baseline","R0","raffa-backend",["ADR-009","ADR-003"],"Implement append-only audit abstraction every module writes to."),
"E01/F06/US02/T02": ("us-02-audit-baseline","R0","raffa-backend",["ADR-009"],"Implement authorized GET /api/audit with tenant scoping."),
"E01/F07/US01/T01": ("us-01-web-oidc-shell","R0","raffa-web",["ADR-012","ADR-010"],"Scaffold React+TS+Vite SPA with OIDC PKCE + config injection."),
"E01/F07/US01/T02": ("us-01-web-oidc-shell","R0","raffa-web",["ADR-012"],"Generate TS API client from OpenAPI; wire /health."),
"E01/F08/US01/T01": ("us-01-mobile-scaffold","R0","raffa-mobile",["ADR-013"],"Scaffold React Native (Expo) + TypeScript app (non-blocking)."),
"E01/F08/US01/T02": ("us-01-mobile-scaffold","R0","raffa-mobile",["ADR-013","ADR-010"],"Configure OIDC PKCE vs Entra with native redirect scheme."),
"E01/F09/US01/T01": ("us-01-final-integration","R0","raffa-backend",["ADR-001","ADR-002","ADR-009","ADR-011","ADR-016"],"Prove R0 end-to-end: workspace->upload->storage->audit on dev/demo."),
"E02/F01/US01/T01": ("us-01-ai-gateway-classification","R1","raffa-backend",["ADR-004","ADR-011"],"Implement IAiGateway classify/extract/embed/answer + config-selected IDs."),
"E02/F01/US01/T02": ("us-01-ai-gateway-classification","R1","raffa-backend",["ADR-011","ADR-004"],"Log model/version/prompt/timestamp/input-hash; no-training config."),
"E02/F02/US01/T01": ("us-01-contract-clause-obligation","R1","raffa-backend",["ADR-003","ADR-009"],"Add Contract/LineItem/Clause/Obligation/Risk/CorrectionHistory + migrations."),
"E02/F01/US02/T01": ("us-02-staged-extraction","R1","raffa-backend",["ADR-004","ADR-002"],"Implement staged schema-constrained extraction with source+confidence."),
"E02/F02/US01/T02": ("us-01-contract-clause-obligation","R1","raffa-backend",["ADR-003"],"Add evidence/source-span/confidence/version columns + schema test."),
"E02/F01/US02/T02": ("us-02-staged-extraction","R1","raffa-backend",["ADR-004","ADR-011"],"Add hybrid OCR pre-pass behind gateway (full doc, no 2-page cap)."),
"E02/F02/US02/T01": ("us-02-embedding-search-index","R1","raffa-backend",["ADR-003","ADR-004"],"Add Embedding entity with pgvector vector column + fixed dimension."),
"E02/F02/US02/T02": ("us-02-embedding-search-index","R1","raffa-backend",["ADR-009","ADR-004"],"Tenant-scoped similarity search + embed via IAiGateway."),
"E02/F03/US01/T01": ("us-01-portfolio-list-filters","R1","raffa-backend",["ADR-002","ADR-009"],"GET /api/contracts spec 8.1 columns + server-side tenant filter."),
"E02/F03/US01/T02": ("us-01-portfolio-list-filters","R1","raffa-backend",["ADR-002"],"Add filters + pagination."),
"E02/F03/US02/T01": ("us-02-contract-360-aggregate","R1","raffa-backend",["ADR-002"],"GET /api/contracts/{id} 360 aggregate (header + tabs)."),
"E02/F04/US01/T01": ("us-01-query-router","R1","raffa-backend",["ADR-002"],"Structured-vs-semantic query intent router (spec 8.3)."),
"E02/F04/US01/T02": ("us-01-query-router","R1","raffa-backend",["ADR-002"],"Deterministic query handlers for dates/spend (no LLM)."),
"E02/F04/US02/T01": ("us-02-rag-citations","R1","raffa-backend",["ADR-004","ADR-011","ADR-003"],"RAG retrieval + grounded answer with citations or cannot-determine."),
"E02/F04/US02/T02": ("us-02-rag-citations","R1","raffa-backend",["ADR-004"],"No-fabrication guard returning cannot-determine; auth-before-retrieval."),
"E02/F05/US01/T01": ("us-01-correction-history","R1","raffa-backend",["ADR-003","ADR-009"],"PATCH /api/contracts/{id} versioned correction + history."),
"E02/F05/US01/T02": ("us-01-correction-history","R1","raffa-backend",["ADR-011"],"Emit audit event on correction; correction history query."),
"E02/F06/US01/T01": ("us-01-final-integration","R1","raffa-backend",["ADR-002","ADR-003","ADR-004","ADR-009","ADR-011","ADR-016"],"Prove R1 end-to-end: upload->extract->portfolio->360->Ask Raffa->correction."),
"E03/F01/US01/T01": ("us-01-deterministic-dates","R2","raffa-backend",["ADR-002"],"Compute renewal date + cancellation deadline deterministically."),
"E03/F01/US01/T02": ("us-01-deterministic-dates","R2","raffa-backend",["ADR-002"],"Generate renewal opportunities; abstain cannot-determine when missing."),
"E03/F01/US02/T01": ("us-02-priority-score","R2","raffa-backend",["ADR-002","ADR-003"],"Priority score + component breakdown."),
"E03/F01/US02/T02": ("us-02-priority-score","R2","raffa-backend",["ADR-002"],"Explainability query + tunable weights."),
"E03/F02/US01/T01": ("us-01-threshold-scheduler","R2","raffa-backend",["ADR-002","ADR-003"],"Daily scheduler + threshold windows + renewal.approaching event."),
"E03/F02/US01/T02": ("us-01-threshold-scheduler","R2","raffa-backend",["ADR-002"],"Create alerts and recompute on contract correction."),
"E03/F03/US01/T01": ("us-01-renewal-dashboard-api","R2","raffa-backend",["ADR-002","ADR-009"],"GET /api/renewals pipeline + insight card."),
"E03/F03/US01/T02": ("us-01-renewal-dashboard-api","R2","raffa-backend",["ADR-002","ADR-009"],"POST /api/renewals/{id}/action + tenant scoping."),
"E03/F04/US01/T01": ("us-01-final-integration","R2","raffa-backend",["ADR-002","ADR-003","ADR-009","ADR-016"],"Prove R2 end-to-end: dates + alerts + prioritized pipeline."),
"E04/F01/US01/T01": ("us-01-benchmark-interface","R3","raffa-backend",["ADR-001","ADR-002"],"Benchmark Service interface getBenchmark + normalized DTO."),
"E04/F01/US01/T02": ("us-01-benchmark-interface","R3","raffa-backend",["ADR-001"],"Adapter registry; no provider SDK in domain code."),
"E04/F01/US02/T01": ("us-02-fixture-adapter","R3","raffa-backend",["ADR-001"],"Fixture adapter returning P25/P50/P75 + confidence + provenance."),
"E04/F01/US02/T02": ("us-02-fixture-adapter","R3","raffa-backend",["ADR-001"],"Weak-comparable abstain; no paid API."),
"E04/F02/US01/T01": ("us-01-price-normalization","R3","raffa-backend",["ADR-002","ADR-003"],"Normalize unit price; compute percentile/target/range."),
"E04/F02/US01/T02": ("us-01-price-normalization","R3","raffa-backend",["ADR-002"],"Propagate confidence + provenance."),
"E04/F02/US02/T01": ("us-02-savings-opportunity","R3","raffa-backend",["ADR-002","ADR-003"],"SavingsOpportunity entity + GET/PATCH /api/savings."),
"E04/F02/US02/T02": ("us-02-savings-opportunity","R3","raffa-backend",["ADR-009"],"Record realized value + audit event."),
"E04/F03/US01/T01": ("us-01-savings-kpis","R3","raffa-backend",["ADR-002","ADR-009"],"Procurement homepage KPI aggregation."),
"E04/F03/US01/T02": ("us-01-savings-kpis","R3","raffa-backend",["ADR-002","ADR-009"],"Savings opportunity list + tenant scoping + provenance."),
"E04/F04/US01/T01": ("us-01-final-integration","R3","raffa-backend",["ADR-001","ADR-002","ADR-003","ADR-009","ADR-016"],"Prove R3 end-to-end with fixture benchmark."),
"E05/F01/US01/T01": ("us-01-quote-line-extraction","R4","raffa-backend",["ADR-002","ADR-004","ADR-003"],"POST /api/quotes upload + line-item extraction."),
"E05/F01/US01/T02": ("us-01-quote-line-extraction","R4","raffa-backend",["ADR-002"],"Normalize line-item unit economics."),
"E05/F01/US02/T01": ("us-02-sku-normalization","R4","raffa-backend",["ADR-002"],"Normalize SKU/edition; flag unmatched."),
"E05/F01/US02/T02": ("us-02-sku-normalization","R4","raffa-backend",["ADR-002"],"Manual product mapping + recalculate trigger."),
"E05/F02/US01/T01": ("us-01-market-assessment","R4","raffa-backend",["ADR-002","ADR-001"],"Match line items to benchmark; above/in-line/below."),
"E05/F02/US01/T02": ("us-01-market-assessment","R4","raffa-backend",["ADR-002"],"Target range + potential saving (deterministic)."),
"E05/F03/US01/T01": ("us-01-negotiation-strategy","R4","raffa-backend",["ADR-002","ADR-004"],"Opening target/range/walk-away/levers with rationale."),
"E05/F03/US01/T02": ("us-01-negotiation-strategy","R4","raffa-backend",["ADR-002"],"Cite evidence per lever."),
"E05/F03/US02/T01": ("us-02-outcome-capture","R4","raffa-backend",["ADR-002","ADR-003"],"NegotiationOutcome entity + POST /api/negotiations/outcomes."),
"E05/F03/US02/T02": ("us-02-outcome-capture","R4","raffa-backend",["ADR-003","ADR-009"],"Realized-savings propagation + audit."),
"E05/F04/US01/T01": ("us-01-final-integration","R4","raffa-backend",["ADR-001","ADR-002","ADR-003","ADR-009","ADR-016"],"Prove R4 Day-1 path: quote->assess->strategy->outcome."),
}

# read wave-spec id -> prompt
ws = WS.read_text(encoding="utf-8")
id_prompt = {}
for m in re.finditer(r"\{id: (E\d+[^,\s]+), prompt: ([^,]+), produces: \[([^\]]*)\], depends_on: \[([^\]]*)\], effort: (\w), layer: (\w+), status: (\w+)\}", ws):
    id_prompt[m.group(1).strip()] = dict(prompt=m.group(2).strip(), produces=[p.strip() for p in m.group(3).split(',') if p.strip()], deps=[d.strip() for d in m.group(4).split(',') if d.strip()], effort=m.group(5), layer=m.group(6))

written = 0
for tid, (story, wave, repo, adrs, objective) in META.items():
    info = id_prompt.get(tid)
    if not info:
        print("MISSING WAVE-SPEC ENTRY:", tid); continue
    path = Path(info["prompt"])
    if path.exists():
        continue
    path.parent.mkdir(parents=True, exist_ok=True)
    fn = path.stem
    slug = fn.split("-", 1)[1] if "-" in fn else fn
    title = slug.replace("-", " ").title()
    produces = info["produces"]; deps = info["deps"]; effort = info["effort"]; layer = info["layer"]
    prod_list = ", ".join(produces); dep_list = ", ".join(deps)
    content = f"""---
id: {tid}
type: task
story: {story}
wave: {wave}
status: live
target_repo: {repo}
---

# {fn} — {title}

## Coding objective
{objective}

## Parent story AC covered
- See parent story `{story}` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/{repo}/src/ | implementation for `{produces[0]}` |

## Context the implementer needs
- **Architecture decisions in force**: {', '.join(adrs)}.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `{produces[0]}`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | {produces[0]} behaviour | workspace/{repo}/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: {tid}
  prompt: {info["prompt"]}
  produces: [{prod_list}]
  depends_on: [{dep_list}]
  effort: {effort}
  layer: {layer}
  status: live
```
"""
    path.write_text(content, encoding="utf-8")
    written += 1

print(f"wrote {written} task files (had {len(id_prompt)} wave-spec entries)")
