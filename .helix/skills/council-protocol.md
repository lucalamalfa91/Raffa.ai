# Council protocol — Raffa architecture table

Six producers decide everything the engineering brief listed under
**Council decides**. One critic closes. Locked decisions are **cited, never
re-litigated**.

## Seats

| Seat | Draft folder | Owns |
|---|---|---|
| product-owner | `reports/architecture/draft/product-owner/` | scope vs non-goals, R0-R4 slice, what V1 will not do |
| software-architect | `reports/architecture/draft/software-architect/` | modular-monolith modules, API-first, worker, data store (within the brief) |
| cloud-architect | `reports/architecture/draft/cloud-architect/` | Azure `dev`/`demo`, cheapest SKUs, region, Terraform layout, Foundry account shape |
| security-architect | `reports/architecture/draft/security-architect/` | tenancy, Entra/OIDC, Key Vault, RAG isolation |
| client-architect | `reports/architecture/draft/client-architect/` | web stack, mobile stack, how clients consume the API |
| delivery-manager | `reports/architecture/draft/delivery-manager/` | git flow, GitHub org + 4 repos, CI/CD to `dev` and `demo`, wave order, calendar |
| council-gate | (writes nothing) | votes + files. Only seat that may emit `COUNCIL_APPROVED:` |

## Independent lanes, then the table

Each specialist runs **alone** first (concurrent sequentials). You have **not**
read the other seats' drafts. Do not `glob` `reports/architecture/draft/` for
sibling folders. Write only under your own draft folder.

The table (`council-close`) is where you read everyone, reconcile, and promote
accepted ADRs to `reports/architecture/ADR-NNN-<slug>.md`.

## Required ADR coverage (at least these topics)

Use `templates/adr-template.md`. One ADR may cover one topic; do not bury two
unrelated decisions in one file.

1. Git flow (branches, promotion `dev` -> `demo`, protections) — delivery-manager
2. Azure services and SKUs for `dev` **and** `demo` — cloud-architect
3. Region (same for both envs) — cloud-architect
4. Terraform module layout (HCP Terraform, remote state per env) — cloud-architect
5. .NET solution shape (modular monolith + worker) — software-architect
6. Web stack — client-architect
7. Mobile stack — client-architect
8. Foundry model IDs (cheapest that meet extract / embed / Ask Raffa) — software-architect + cloud-architect
9. CI -> Azure auth — delivery-manager + cloud-architect + security-architect
10. Promotion `dev` -> `demo` (explicit, not accidental copy) — delivery-manager

11. Relational store (PostgreSQL + pgvector) — software-architect
12. Tenancy / RLS — security-architect
13. Key Vault + RAG isolation — security-architect
14. V1 scope R0–R4 (user-visible ladder + out-of-scope) — product-owner

The decomposer consumes the **entire INDEX**, not an R0 subset. Closing the
table on platform ADRs only, while product-context still lists R1–R4, is OBJECT.

## Votes at the table

Every producer turn at `council-close` ends with a `VOTE:` line:

```
VOTE: APPROVE
VOTE: OBJECT — <one concrete file or decision that is missing>
VOTE: PROPOSE — <the change you need before you approve>
VOTE: ABSTAIN — <who must rule, and why it is not your seat>
```

`APPROVE` means you accept the **files on disk**, not the conversation. If an
ADR is still only in chat, OBJECT.

## What you must not do

- Do not add locked rules the brief did not lock.
- Do not pick production HA, AKS, multi-region, or a fifth GitHub repo without
  justifying it against the product spec (and expect OBJECT).
- Do not emit `COUNCIL_APPROVED:` or `COUNCIL_FILES_WRITTEN:` — only the gate.
- Do not write application code.
