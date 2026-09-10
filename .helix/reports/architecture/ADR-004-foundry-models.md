# ADR-004 — Foundry model roles and candidate IDs (AI Gateway)

- **Status**: accepted
- **Date**: 2026-09-02
- **Deciders**: software-architect (roles/selection owner) + cloud-architect (IDs/prices in region) jointly; security-architect (RAG isolation) reconciles at council-close
- **Locked citations**: AI — Microsoft Foundry only, via Raffa AI Gateway; domain modules never call a provider directly; use cheapest Foundry models that still meet the tasks (locked-decisions.md). Brief §8: log model/version/prompt/version/timestamp/input-hash; cheapest models for classification, structured extraction, grounded Q&A with citations, embeddings. OCR in V1: ADR-017.

## Context and problem statement

All model I/O flows through the AI Gateway (brief §8). The product needs five distinct *roles*: **ocr** (full-document text/layout; ADR-017), **classification** (document type), **structured extraction** (schema-constrained facts), **embedding** (semantic search/Ask Raffa RAG), and **grounded Q&A** (Ask Raffa with citations). The brief mandates the *cheapest* Foundry / Azure AI surfaces that still perform each role, and forbids customer contract content from training public/shared models. OCR is in V1 (ADR-017); it is not deferred.

The question is which role maps to which model family, and which concrete candidate IDs are preferred such that cost stays minimal and citation/quality is met (or an explicit "cannot determine" is returned).

## Decision drivers

- Cheapest model that meets each role, not one powerful model for everything (brief §8).
- Embeddings must feed pgvector (ADR-data-store) and support auth-before-retrieval RAG (brief §10).
- Extraction must be staged and schema-constrained with source + confidence (brief §7) — a structured-output-capable model (JSON schema) is preferred over free-text parse.
- Grounded Q&A must produce citations or an explicit "cannot determine" (brief §13, done-when #4).

## Considered options

1. **Role-specific split with gateway indirection** — a distinct model ID per role (ocr, classify, extract, embed, answer) behind the AI Gateway's interface, so model selection is a configuration concern and can be swapped without code change.
2. **One flagship model for everything** — a single powerful GPT-4-class model for classify/extract/answer plus a separate embedding model.
3. **No model pinning** — let the gateway ask Foundry for a default; acceptable only for exploration, not the demo.

## Decision outcome

**Chosen: Option 1** — the AI Gateway exposes role interfaces (ocr, classify, extract, embed, answer) and each role is bound to a **configuration-selected model ID**, defaulting to the cheapest model that meets the role, with IDs confirmed at implementation time in the target region by cloud-architect. This is because it satisfies "cheapest per task," keeps model swap a config change (provider locked to Foundry / Azure AI services, but model/version not hard-coded), and makes usage/billing auditable per role (brief §8). OCR is a first-class V1 role (ADR-017), not a later add-on.

### Candidate model IDs (to be confirmed by cloud-architect for price/availability in region)

| Role | Candidate family (Foundry) | Role requirement | Notes |
| --- | --- | --- | --- |
| OCR | Azure AI Document Intelligence `prebuilt-read` / `prebuilt-layout` | Full-document text + page map + layout/tables | In V1 (ADR-017). Hybrid: native text when sufficient; OCR/Layout when scanned, image, or table-poor. No 2-page cap. |
| Classify | Small instruction model (e.g. GPT-4o-mini / Phi-class) | Document type from first pages | Cheap; classification is low-complexity. |
| Extract (structured) | Structured-output-capable (e.g. GPT-4o-mini with JSON-schema mode) | Schema-constrained extraction with source spans + confidence | Must support JSON-schema/structured output, not free text (brief §7). |
| Embed | Foundry embedding model (e.g. `text-embedding-3-small` or `text-embedding-3-large` if needed) | Vectors for pgvector RAG | Dimension fixed at schema time; small dimension preferred for cost/size. |
| Answer (grounded Q&A) | Same instruction model as extract, or one tier up if citation quality is insufficient | Grounded answer with citations or "cannot determine" | Must be promptable to cite source and abstain rather than fabricate (Appendix C rule 10). |

Exact IDs and per-1k-token prices are **cloud-architect's** lane: they must confirm the identifier and price in the target region at implementation time and record it in their Azure SKU ADR. The software-architect fixes the *roles and selection rule*, not the dollar figure.

### Consequences

- **Good**: Cost scales to the cheapest model per role; no single expensive model monopolizes the bill; model swap is config-only, so the gateway is testable with a fixture model too; per-role usage logging satisfies brief §8 reproducibility.
- **Bad**: Five role bindings to maintain and confirm in region; a per-role model that underperforms (e.g. structured extraction) may force a one-tier upgrade, which is a config change but still a change; embedding dimension choice must be consistent into pgvector; OCR adds a pay-per-page line (ADR-017).
- **Neutral**: OCR vs native parse is closed by ADR-017 (hybrid, in V1, behind the gateway). Classification may still use "first pages" for type detection; OCR/extract must not.

## Pros and cons of the options

### Option 1 — role-specific split, gateway config
- Good: cheapest-per-role; swappable; auditable per role; keeps provider locked to Foundry.
- Bad: more bindings to confirm; gateway must expose per-role config, not one model knob.

### Option 2 — one flagship for all
- Good: one model to configure.
- Bad: pays flagship price for trivial classification/embedding; violates "cheapest per task"; harder to attribute cost per role.

### Option 3 — no pinning
- Good: zero config effort.
- Bad: non-deterministic cost/behavior; violates reproducibility (brief §8); unacceptable for `demo`.

## Implications for the decomposition

The AI Gateway MUST expose role-based interfaces (ocr, classify, extract, embed, answer) bound through configuration, never a hard-coded model ID in domain code. Every AI call MUST log model ID, version, prompt version, timestamp, and input hash (brief §8); OCR calls MUST also log page count (ADR-017). Structured extraction MUST return schema-constrained output with source spans and confidence; grounded Q&A MUST return citations or an explicit "cannot determine." Embedding dimension MUST be fixed and consistent with the pgvector column definition (coordinate with ADR-data-store). Model IDs/prices MUST be confirmed in the target region before the `demo` wiring task is accepted; until then a fixture gateway adapter satisfies R0 scaffolding. R1 extraction on `demo` MUST exercise the real OCR path for at least one scanned/image fixture (ADR-017).

## Assumptions

- The cheapest structured-output model is sufficient for contract commercial terms; if not, a one-tier upgrade is a config change only.
- OCR is in V1: hybrid native-text + Document Intelligence behind the gateway (ADR-017). Native parse is not assumed sufficient for scanned MSAs.
- Exact model IDs and prices (including Document Intelligence per-page) are filled by cloud-architect in the target region; candidate names above are placeholders, not final selections.

## Amendment (2026-09-08, epic-12 / ADR-023)

The **answer** role is a **savings / negotiation copilot**, not a concatenator
of retrieved chunks. It must: (1) stay behind `IAiGateway` with Azure SDKs only
in `Raffa.AiGateway`; (2) register Foundry when `AiGateway:Endpoint` is set,
else the fixture; (3) always wrap `LoggingAiGateway`; (4) narrate deterministic
benchmark and negotiation numbers rather than invent them; (5) abstain or
redirect on off-domain, greeting, legal, and insufficient-evidence turns.
Existing documents must be re-OCR’d / re-embedded so the embed/answer path
does not retrieve `%PDF-1.4`. See ADR-023.

## Amendment (2026-09-08, epic-13 / ADR-024)

`FoundryAiGateway` implements the five roles (`ocr` on Document Intelligence,
`classify`, `extract`, `embed`, `answer`), registered when
`AiGateway:Endpoint` is set, fixture otherwise, always wrapped by
`LoggingAiGateway`. `classify` is reused for the **document admission gate**
and for the Ask **domain gate** with fixed label sets. `answer` returns
**structured JSON** (`canDetermine`, `answerMarkdown`, `citationKeys`,
`actionKeys`, `abstainReason`, `followUps`) from a versioned persona prompt,
temperature <= 0.2, with **no tools, no web grounding, no browsing** on the
deployment or the request (compliance test on the fake HTTP handler).
Per-tenant daily token and OCR-page budgets fail visibly. This footer
supersedes the epic-12 amendment above. See ADR-024.

## Amendment (2026-09-09, confirmed model ids per environment)

The candidate table above is replaced by deployments confirmed in
`northeurope` for this subscription (`az cognitiveservices model list -l
northeurope`, 2026-09-09) and created by Terraform (`infra/modules/foundry`,
ADR-008 amendment of the same date). The backend binds
`AiGateway:Models:<Role>:ModelId` / `:ModelVersion` from the deployment
resources (env vars `AiGateway__Models__<Role>__ModelId` / `__ModelVersion`)
and never hard-codes a deployment name. `dev` is deliberately cheap; `demo`
deliberately frontier.

| Role | dev deployment (model, version, SKU) | demo deployment (model, version, SKU) |
| --- | --- | --- |
| classify | `gpt-5.4-nano-dev` (gpt-5.4-nano 2026-03-17, DataZoneStandard) | `gpt-5.4-nano-demo` (gpt-5.4-nano 2026-03-17, DataZoneStandard) |
| extract | `gpt-5.4-nano-dev` | `gpt-5.4-demo` (gpt-5.4 2026-03-05, DataZoneStandard) |
| answer | `gpt-5.4-nano-dev` | `gpt-5.4-demo` |
| embed | `text-embedding-3-small-dev` (version 1, GlobalStandard) | `text-embedding-3-large-demo` (version 1, GlobalStandard), request `dimensions = 1536` |
| ocr | Document Intelligence `prebuilt-read`, api-version 2024-11-30 (built in, no deployment) | same |

Rules: `version_upgrade_option = NoAutoUpgrade`, so the logged model version
(brief §8) is the version that answered; capacity (thousands of tokens per
minute: dev 300 for the chat deployment and 100 for embeddings, demo 200 /
200 / 100) is changed by pull request in the environment root;
`DataZoneStandard` (EU data zone) wherever the model offers it, else
`GlobalStandard`; `gpt-4o-mini` / `gpt-4.1-*` exist in this region only as
`GlobalProvisionedManaged` (fixed cost) and are rejected. The pgvector
column stays 1536-dimensional (ADR-003): `text-embedding-3-small` is 1536
natively and the gateway forces `dimensions = 1536` on
`text-embedding-3-large`, so promotion never changes the vector width. The
GPT-5.x family constrains the chat request (`max_completion_tokens`,
optional `reasoning_effort`, `temperature` only where the deployment accepts
it): the gateway keeps every knob configuration-driven and omits it when
unset, and the "temperature <= 0.2" rule above applies whenever temperature
is sent. Model swap remains config-only: a different deployment is a
Terraform change in the environment root, not code.
