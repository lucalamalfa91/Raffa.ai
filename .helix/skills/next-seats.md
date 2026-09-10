# Seats of the next-wave council — who is needed for what

The council of the next-wave process has **seven seats and one gate**. Every
seat is declared in the YAML and takes its round-robin turn, but only the
seats the intake listed for an item **work** on it. The others answer in
one line (`PASS`). The intake decides the roster **per item**, in
`reports/context/waves/<w>-requirements.md` (column `Seats` and §3 roster),
using this table. When in doubt, add the seat: a PASS costs one short turn;
a missing seat costs a wave.

| Seat key | Label at the table | Owns (delta on the existing product) | ADRs it guards | Involve it when the item… |
|---|---|---|---|---|
| `product-owner` | `PRODUCT_OWNER:` | scope vs non-goals, priority, acceptance wording, pilot path, personas/roles | ADR-001, spec §1/§16/§20, `inputs/percorso-pilota-v1.md` | changes what the product promises, adds a capability, re-prioritises, has ambiguous acceptance, or cancels an existing backlog item |
| `software-architect` | `SOFTWARE_ARCHITECT:` | .NET modules, API contract (OpenAPI), data model / migrations, extraction pipeline, AI gateway roles, worker/queue, deterministic calculators | ADR-002, 003, 004, 017, 021, 023/024 | adds or changes an endpoint, a table, a module, a pipeline stage, an AI role, a background job, or fixes a backend bug that crosses a module boundary |
| `cloud-architect` | `CLOUD_ARCHITECT:` | Azure resources, SKUs, Terraform modules, container apps, storage, Foundry account, env vars per environment, cost | ADR-005, 006, 007, 008, 016 | needs a new or changed Azure resource, a Terraform change, a runtime config key per environment, a deploy-path change, or touches `infra/` |
| `security-architect` | `SECURITY_ARCHITECT:` | tenancy/RLS, Entra/OIDC, JWT, roles and authorization, Key Vault, RAG isolation, audit, privacy | ADR-009, 010, 011, 022 | touches auth, tokens, headers, roles, tenant scoping, membership, invitations, audit, or any data another tenant could see |
| `client-architect` | `CLIENT_ARCHITECT:` | web SPA structure, routing, state/persistence rules, API client generation, e2e, mobile scaffold | ADR-012, 013, 018 (routes) | changes a route, a client store / read-back contract, the generated client, session/local storage use, or fixes a web bug beyond a single component |
| `ux-ui-designer` | `UX_UI_DESIGNER:` | information architecture, design system tokens, screen inventory, copy, states, Claude Design handoff | ADR-018, 019, 020 | is a design change, a new screen or state, a visual alignment to a prototype under `inputs/design/`, a copy change, an empty/error state |
| `delivery-manager` | `DELIVERY_MANAGER:` | git flow, CI workflows, promotion `dev` → `demo`, seeds and operator jobs, wave order, acceptance on `dev` | ADR-014, 015, 016 | adds or changes a workflow, a seed/job, the promotion path, or the wave has ordering constraints (a fix must land before a feature) |
| `council-gate` | `COUNCIL_GATE:` | nothing — verifies votes and files, closes the table | all | always at the table |

## Rules for the roster

- A **bug in one component** (wrong count, missing read-back, broken CTA)
  usually needs **no seat**: kind `bug`, seats `none`. The decomposer still
  writes the task. The table then closes in one round with every seat on
  `PASS` and the gate approving an empty decision record.
- A **design item** always involves `ux-ui-designer`; add `client-architect`
  when routes or state change.
- A **new backend resource on Azure** involves `software-architect` +
  `cloud-architect`; add `security-architect` when identities or secrets
  change, `delivery-manager` when Terraform/CI must run first.
- **Auth / roles / tenant** items always involve `security-architect`.
- `product-owner` is not automatic. Involve it for scope, priority conflicts,
  or acceptance the raw file leaves vague.
- An item may need **no ADR change** even with a seat involved: the seat then
  records `ADR action: none — <why>` in the decision record.
