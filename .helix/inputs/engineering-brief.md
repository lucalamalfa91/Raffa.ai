# Raffa V1 — Technical Engineering Brief

**Audience:** Helix council  
**Status:** Engineering mandate — Azure `dev` + `demo`  
**Date:** 1 September 2026 (v1.2 — supersedes 27 August source-control lock)  
**Product source of truth:** [`product-spec.md`](./product-spec.md)

**v1.2:** source control is **one public** GitHub repository (monorepo) with domain folders, called "raffa" with description "Raffa platform" in the lucalamalfa91 account ([https://github.com/lucalamalfa91/raffa](https://github.com/lucalamalfa91/raffa)). Helix `fan_out` isolates a single git toplevel. 

---

## How to use this document

| Document | Role |
|---|---|
| Product specification | **WHAT** to build. Do not re-negotiate product scope here. |
| **This brief** | **HOW** — locked platform guidelines only. Everything not locked is decided by the Helix process. |

Do not treat examples, SKUs, model names, branch names, or library choices elsewhere as mandates. If this brief does not lock it, the council defines it.

**Helix execution notes**

- **All code is written by Claude Code through the Helix process.** The council designs, gates, and reviews. There is no parallel hand-written codebase.
- Artifacts (ADRs, YAML, prompts, code comments) in English. Council deliberation may be Italian.
- Do not re-litigate locked decisions. Do not fill gaps with preferences that this brief does not state — fill them in council.

---

## 1. Locked vs council-owned

### Locked

| Decision | Guideline |
|---|---|
| Cloud | Microsoft Azure. |
| Environments | Two from day one: **`dev`** and **`demo`**. No production yet. Isolated from each other (data, identities, resource groups). |
| Cost | Use free tiers and the cheapest SKUs that still satisfy the product spec. Nothing idle-expensive. No production HA / multi-region for now. |
| IaC | HCP Terraform. Infra code lives in the `infra/` folder of the monorepo. |
| Backend | C# / ASP.NET Core (current LTS at implementation time). Modular monolith + background worker. No microservices split in V1. |
| Frontend / mobile | Council decides the stacks. |
| Source control | GitHub account **lucalamalfa91**. **One public** repository [`raffa`](https://github.com/lucalamalfa91/raffa) (see §2). Description "Raffa platform". Not four remotes. |
| Delivery | GitHub CI/CD releases to Azure `dev` and Azure `demo`. |
| AI | Microsoft Foundry only, via a Raffa **AI Gateway**. Domain modules never call a provider directly. Use the cheapest Foundry models that still meet the product tasks. |
| Auth / secrets | OIDC, SSO-ready (Entra ID). Secrets in Key Vault. No secrets in code, client bundles, or Terraform source. |
| API | API-first. Web and mobile consume the backend API. |
| Code authoring | Claude Code via Helix, for infra, backend, web, and mobile. |

### Council decides (non-exhaustive)

Git flow on the single repo; path filters / per-folder CI jobs; exact Azure services and SKUs within the cost guideline; region; Terraform module layout; .NET solution shape; frontend stack; mobile stack; Foundry model IDs; how CI authenticates to Azure; how promotion `dev` → `demo` works.

Record council decisions as ADRs. Do not invent extra locked rules.

---

## 2. GitHub

The product remote already exists and is **public**: [`https://github.com/lucalamalfa91/raffa`](https://github.com/lucalamalfa91/raffa) (owner `lucalamalfa91`, name `raffa`, description **Raffa platform**). That repository is the Helix run repo and the only product remote. Do not create a GitHub organization. Do not make the repository private.

Locked folder layout (do not invent extra top-level product trees):

| Folder | Responsibility |
|---|---|
| `infra/` | Terraform for Azure `dev` and `demo` |
| `backend/` | ASP.NET Core API + worker |
| `web/` | Web client (stack: council) |
| `mobile/` | Mobile client (stack: council) |
| `.helix/` | Helix process artifact (this brief, ADRs, work items, wave-spec). Not a nested git repo. |

No second product remote. No `raffa-infra` / `raffa-backend` / `raffa-web` / `raffa-mobile` as separate GitHub repositories. Extra remotes only if the council justifies them against the product spec **and** against Helix’s one-repo isolation.

Passata 2 `fan_out` worktrees are checkouts of **this** repository (branch per task, same tree: `.helix` + the four domain folders). Claude Code cwd is that worktree root. Product files go under `infra/`, `backend/`, `web/`, `mobile/` — not under a synthetic `workspace/<repo>/` and not into four remotes.

CI/CD on GitHub must be able to deploy each **deployable folder** to **both** Azure environments (path filters / per-folder jobs: council). How branches, tags, environments, approvals, and rollbacks work is **git flow — council**.

### 2.1 Git flow — guidelines only

The council defines the git flow and writes an ADR. Claude Code implements that ADR.

Guidelines (not a flow):

- Two Azure targets only: `dev` and `demo`. Do not add `prod` now.
- `dev` is for integration. `demo` is for stakeholders. They must not share databases or document storage.
- Promotion to `demo` is explicit, not an accidental copy of every `dev` deploy.
- Work lands through the process the council names (PRs, protections, etc.). Claude Code follows that process.
- One flow on the one repo. Per-folder deploy jobs must not require four remotes.

Do not assume a default branch, GitHub Flow, Git Flow, tags, or Environment approvals unless the council ADR says so.

---

## 3. What to construct

Build V1 as in the product spec (jobs, entities, APIs, events, non-goals, Appendix C). Topology intent from the spec: modular monolith, worker, relational store, object storage, queue, AI gateway.

**In scope for `dev`/`demo`:** workspace and roles; document upload and async extraction with evidence + confidence; human correction history; portfolio and Contract 360; Ask Raffa with citations and auth-before-retrieval; deterministic renewals; benchmark **interface** (fixture adapter is enough); savings and quote-check paths as in the product delivery plan.

**Out of scope:** product §1.2 non-goals; production-only platform (AKS, multi-region, dedicated-per-tenant DB).

---

## 4. Azure `dev` and `demo`

Two isolated landing zones, same architecture, different names/data/identities.

**Guidelines**

- Cheapest (or free) SKU that still supports the product: HTTPS app, worker, object storage, queue, relational DB with tenant isolation and vectors/search as the spec requires, secrets, Entra, Foundry.
- Tag resources with `project=raffa` and `env=dev|demo`.
- `dev` and `demo` must not share PostgreSQL (or equivalent) or document storage.
- Foundry billing vs one-vs-two accounts: council, under the cost guideline.
- Region: council (keep `dev` and `demo` in the same region).
- Stop/start or scale-to-zero where the platform allows, to keep idle cost down.

The council selects the concrete Azure services. This brief does not prescribe Container Apps vs App Service, queue product, log caps, or registry.

---

## 5. Database

The product spec’s deployable topology is relational (PostgreSQL + vectors). SQLite was considered for cost.

**Guideline:** SQLite is acceptable on a developer laptop only. It does not meet product constraints on Azure (`tenant` isolation at DB level, shared API + worker, embeddings/search, durable shared storage).

On Azure `dev` and `demo`, use the cheapest managed store that satisfies those product constraints. Exact engine, SKU, and access library: council.

Do not use a non-relational store as the system of record.

---

## 6. Infrastructure as code

HCP Terraform in `infra/`. Apply **both** `dev` and `demo`. Remote state per environment. No state files in git. No secrets in Terraform source; apps read secrets at runtime from Key Vault.

Module layout, networking, and identity wiring: council, within locked auth/cost/isolation guidelines.

---

## 7. Backend

ASP.NET Core modular monolith + worker in `backend/`. Module boundaries follow the product spec (identity/workspace, documents/contracts, suppliers/products, renewals, savings, quotes, benchmark, chat, audit, AI gateway).

Benchmark is an interface + replaceable adapter. Calculations that the spec marks deterministic (dates, money) stay in code, not in the LLM. Extraction is staged and schema-constrained, with source + confidence. Ask Raffa routes structured vs semantic queries as in the spec.

Solution layout, libraries, and API versioning scheme: council, as long as Appendix A capabilities exist.

---

## 8. Microsoft Foundry

All model I/O through the AI Gateway. Customer contract content must not train public/shared models. Log enough to reproduce (model, version, prompt version, timestamp, input hash) without leaking unauthorized content.

**Guideline:** choose the cheapest Foundry models that still perform classification, structured extraction, grounded Q&A with citations, and embeddings. Confirm IDs and price in the target region at implementation time.

OCR vs native document parse: council, provided full contract documents can be processed (not a 2-page-only path for real MSAs).

---

## 9. Frontend and mobile

**Web:** council picks the stack. The client must deliver the product UX (auth, portfolio, Contract 360, evidence, review, Ask Raffa, then renewals/savings/quotes as those slices land) against the API.

**Mobile:** council picks the stack. Product V1 topology is web-first; native must not block `dev`/`demo`. The `mobile/` folder still exists in the monorepo.

Hosting choice for the web client: council, under the cost guideline.

---

## 10. Security and tenancy

Follow product §14. `tenant_id` on business data; isolation in **both** Azure environments; RAG must not retrieve unauthorized documents; TLS in transit; managed identity for Azure resources; audit of access and corrections.

How that is implemented (RLS, query filters, etc.): council.

---

## 11. Delivery order

Follow product §16 (R0–R4). First technical slice is platform: the public `lucalamalfa91/raffa` monorepo (folder layout above) + Terraform for `dev` and `demo` + CI/CD to both + git-flow ADR, then an API that can be deployed.

R3/R4 must not depend on a paid external benchmark API for the first `demo`.

---

## 12. Helix process

- Inputs: this brief + the product spec + the constraints file.
- Council ADRs for everything in “Council decides”.
- Implementation phases: Claude Code on the **one** GitHub repository. Helix worktrees that repo; `.helix/` stays inside it and is not its own git toplevel.
- Do not encode extra platform choices into the Helix YAML that this brief did not lock.
- Do not design four product remotes or a `workspace/<repo>/` stand-in for them.

---

## 13. Done when

1. `dev` and `demo` exist in Azure, isolated, applied from Terraform.
2. GitHub can release to both environments, using the council’s git flow.
3. Backend API + worker run in both environments.
4. Product “Day 1” path works on `demo` (workspace, upload, extract, review) with Foundry, including Ask Raffa **with citations** or an explicit “cannot determine”.
5. The public repository [`lucalamalfa91/raffa`](https://github.com/lucalamalfa91/raffa) exists with `infra/`, `backend/`, `web/`, `mobile/`, and `.helix/`; all application and infra code came from Claude Code via Helix.
6. Cost stays on free/cheap SKUs; no production HA platform.
