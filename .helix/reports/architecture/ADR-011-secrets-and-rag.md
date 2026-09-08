# ADR-011 — Key Vault layout, CI auth, RAG authorization, audit, no-training

- **Status**: accepted
- **Date**: 2026-09-01
- **Deciders**: security-architect (owner); delivery-manager and cloud-architect concur at council-close
- **Locked citations**: `locked-decisions.md` row "Auth/secrets" (secrets in Key Vault; no secrets in
  code, client bundles, or Terraform source); row "AI" (Foundry only via AI Gateway); row "Delivery"
  (GitHub CI/CD to `dev` and `demo`). Product spec §14.1 (secret mgmt, TLS, encrypted backups, audit of
  access and changes), §14.2 (AI privacy: no training on public/shared models, centralized logged
  gateway, log model/version/prompt/timestamp/input hash), §14.3 (export/deletion), §8.3 (authorization
  filter before retrieval). Brief §8/§10 (tenant_id, RAG must not retrieve unauthorized docs, audit of
  access and corrections, managed identity).

## Context and problem statement

Three security concerns share one root cause (secrets and the boundary between "user may see it" and
"model may see it"):

1. **Secrets** must live in Key Vault and reach compute without ever appearing in code, client bundles,
   or Terraform source. CI must authenticate to Azure to deploy without a stored secret.
2. **RAG must not leak cross-tenant content.** Ask Contigo (spec §8.3) shows the authorization filter
   **before** retrieval — intent detection and semantic retrieval are downstream of an authorization
   decision, so an unauthorized contract can never be embedded into LLM context.
3. **Audit** of access and corrections is a listed enterprise control, and **customer contract content
   must not train public/shared models.**

## Decision drivers

- **No secrets in code/bundles/Terraform** (locked) — anything repository-visible is non-secret config.
- **Managed identity** (brief §10) — compute authenticates to Key Vault and Foundry via workload
  identity, not stored keys.
- **Authorization before retrieval** (spec §8.3, Appendix C #4) — the retrieval pipeline cannot run
  before a tenant+role+object authorization check.
- **Reproducible AI logging without leaking content** (§14.2) — we log model/version/prompt/timestamp/
  input **hash**, never the raw prompt or retrieved contract text.

## Considered options

1. **Per-environment Key Vault + managed-identity access + federated OIDC for CI** — one Key Vault per
   env; apps/worker use managed identity; GitHub Actions use workload-identity federation (no stored
   secret).
2. **One shared Key Vault for both envs + service-principal secrets in Terraform state** — fewer objects
   but crosses the isolation boundary and stores a secret in IaC.
3. **Key Vault per env but CI via long-lived service-principal client secret stored in GitHub** — no
   Terraform secret but a GitHub secret still exists.

## Decision outcome

**Chosen: Option 1 — one Key Vault per environment (`kv-contigo-dev`, `kv-contigo-demo`), accessed via
Azure managed identity for the API and worker, and GitHub Actions authorized via OpenID Connect /
workload-identity federation (subject-claim scoped to the repo + environment).** Authorization in the
Ask Contigo path is enforced **before** retrieval: the chat endpoint resolves the caller's tenant +
role + object permissions, and only the resulting authorized scope is passed to the semantic/vector
retrieval, which adds a `tenant_id` filter at the database/index level. Audit records access and
corrections; AI logs capture model/version/prompt-version/timestamp/input-hash — never raw prompt or
retrieved content. Foundry calls flow only through the AI Gateway, which is configured for a
no-training model endpoint.

### Consequences

- **Good**: No secret ever transitively stored in Git (federation exchanges a short-lived OIDC token for
  a short-lived Azure token). Isolation preserved (per-env Key Vault). RAG isolation is structural
  (authz scope computed first, then retrieval filtered by tenant).
- **Good**: Audit trail satisfies §14.1 "comprehensive audit logging for access and data changes" and
  brief "audit of access and corrections" without logging unauthorized content.
- **Good**: No-training enforced at the gateway: the Foundry deployment/model selected must be a
  no-training endpoint, and the gateway is the single choke point that proves it.
- **Bad**: Two Key Vaults + federation config add Terraform surface; each env's managed-identity
  assignments must be kept in sync.
- **Neutral**: Audit log retention/query cost is a delivery-manager/cloud-architect concern at the
  cheapest SKU.

## Pros and cons of the options

### Option 1 — per-env Key Vault + managed identity + OIDC federation (chosen)
- Good: no stored secret anywhere in the delivery path; isolation intact; managed identity means no
  runtime secret materialization.
- Bad: more IaC wiring per env.

### Option 2 — shared Key Vault + SP secret in state
- Good: simplest.
- Bad: violates environment isolation and "no secrets in Terraform source"; rejected outright.

### Option 3 — per-env Key Vault + GitHub-stored SP secret
- Good: keeps KV separated.
- Bad: a long-lived client secret still exists (not in Terraform, but in GitHub), weaker than federation.

## Implications for the decomposition

- Terraform (cloud-architect ADR) creates `kv-contigo-dev` and `kv-contigo-demo`, and grants the API and
  worker managed identities `get`/`list` on their own env's vault. No access-policy cross-env.
- GitHub Actions (delivery-manager ADR) authenticate via OIDC/`azure/login` with `client-id`,
  `tenant-id`, `subscription-id` only (non-secret) and a subject claim pinned to the repo + environment
  (`repo:lucalamalfa91/contigo:environment:dev|demo`). No `AZURE_CREDENTIALS` secret.
- The API reads connection strings, Foundry endpoint/key, and signing config from Key Vault at startup,
  not from appsettings committed to Git (appsettings may hold only non-secret keys like the Vault URI and
  non-secret config).
- The chat/Ask Contigo service must implement: resolve tenant/role/object authz → build authorized
  retrieval scope → run semantic/vector retrieval with a mandatory `tenant_id` filter → assemble LLM
  context. Retrieval cannot be invoked before the authz step (enforced in code, cited by §8.3/§C.4).
- The AI Gateway (software-architect ADR) is the only component that calls Foundry; it enforces the
  no-training model and emits the reproducible log record (model, model version, prompt version,
  timestamp, input hash). Raw prompt and retrieved contract text are **never** written to logs.
- Audit of access and corrections: an `audit` domain logs who/when/what changed (create/update/correct/
  delete of contract facts) and access events, keyed by tenant; unauthorized-content data is excluded
  from any log payload.

## Assumptions

- Microsoft Foundry in the chosen region offers a no-training model endpoint; if the only available
  model trains on shared data, the AI Gateway must be configured to opt out (or the cheapest
  compliant model selected — resolved jointly with software-architect + cloud-architect in the Foundry
  model-ID ADR). Recorded in `reports/open-questions.md`.
- "Input hash" for reproducibility = a content hash (e.g. SHA-256) of the retrieved evidence/prompt, so
  we can verify a given model/version ran on a given input without storing the confidential input itself.

## Amendment (2026-09-08, epic-12 / ADR-023)

Ask Contigo keeps **two corpora**, never one blended index:

1. **Tenant RAG** — this workspace’s contracts only (authz → `tenant_id` filter
   → retrieval). Unchanged from the original decision.
2. **Market** — `IBenchmarkService` (fixture now). Not written into tenant
   pgvector and not sourced from another tenant’s PDFs.

Off-domain questions must not run RAG. See ADR-023.
