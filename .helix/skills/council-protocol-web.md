# Council protocol — Raffa **web delta** table

This is **not** the platform council. ADR-001…017 and epic-01…05 are **done**.
You decide only the **web experience** that consumes that backend, starting at
**wave / epic 6**.

Seven producers. One critic closes. Locked decisions are **cited, never
re-litigated**.

## Seats

| Seat | Draft folder | Owns on this pass |
|---|---|---|
| ux-ui-designer | `reports/architecture/draft/ux-ui-designer/` | IA, design system, screen inventory, Claude Design handoff, a11y baseline |
| product-owner | `reports/architecture/draft/product-owner-web/` | Day-1 path vs spec §16/§20; which screens are in e06+ |
| client-architect | `reports/architecture/draft/client-architect-web/` | ADR-012 consumption: routing, MSAL, OpenAPI regen — **not** pixels |
| software-architect | `reports/architecture/draft/software-architect-web/` | Thin API gaps only; no module redesign |
| delivery-manager | `reports/architecture/draft/delivery-manager-web/` | e06+ slice calendar; do not edit `slices/e01`–`e05` |
| cloud-architect | `reports/architecture/draft/cloud-architect-web/` | Confirm no infra delta (or name the one SWA/config gap) |
| security-architect | `reports/architecture/draft/security-architect-web/` | Confirm OIDC/PKCE/RLS unchanged for the SPA |
| council-gate-web | (writes nothing) | votes + files. Only seat that may emit `COUNCIL_APPROVED:` |

## Independent lanes, then the table

Each specialist runs **alone** first. Do not `glob` sibling draft folders.
Write only under **your** draft folder (the `-web` / `ux-ui-designer` path).

`council-close-web` is the table. **First producer turn:** promote drafts to
`ADR-018` / `ADR-019` / `ADR-020` if those files are missing. If they already
exist on disk, vote `APPROVE` immediately — do not spend turns re-arguing IA.
Never replace ADR-001…017.

## Required ADR coverage (this pass)

Use `templates/adr-template.md`. Number **018+**.

1. Information architecture (Day-1 sitemap, nav, roles) — ux-ui-designer
2. Design system (tokens + Claude Design system pointer) — ux-ui-designer
3. Screen inventory mapped to spec §16 R0–R4 and §20 — ux-ui-designer + product-owner
4. Optional: thin API gaps — software-architect (omit the ADR if zero gaps)

Claude Design exports must exist under `inputs/design/` before the table
closes. Point ADRs at those paths.

## Votes

Every producer turn at the table ends with:

```
VOTE: APPROVE
VOTE: OBJECT — <missing file or decision>
VOTE: PROPOSE — <change needed>
VOTE: ABSTAIN — <who must rule>
```

`APPROVE` means the **files on disk**, not the chat.

## What you must not do

- Do not re-open git flow, SKUs, region, Terraform, .NET, Foundry IDs, promotion.
- Do not plan mobile feature waves or a store release (ADR-013 stands).
- Do not splice UI tasks into `reports/plan/slices/e01.yaml`–`e05.yaml`.
- Do not overwrite `reports/plan/slice.current.yaml` or `wave-spec.execution.yaml`.
- Do not emit `COUNCIL_APPROVED:` or `COUNCIL_FILES_WRITTEN:` — only the gate.
- Do not write application code.
