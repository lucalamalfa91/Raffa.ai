# Ask Raffa V2 — architecture and data flow

> **The one rule.** Ask Raffa answers only from Raffa's own store — the
> Postgres tables and the two vector indexes that live in the same database —
> plus its deterministic calculators. Microsoft Foundry on Azure reads,
> classifies, embeds and narrates; it carries **no tools and no web access**.
> The market-intelligence source (a mock feed today, a third-party API
> tomorrow) is read **only by the ingestion job**, never while a user is
> asking.

Binding decisions: [ADR-024](../../.helix/reports/architecture/ADR-024-ask-raffa-v2.md),
requirements [`requirements.md`](../../.helix/inputs/requirements.md) (§2 three
sources, §5.1 intake, §5.3 engine, §5.8 market intelligence). Design
reference: `Raffa V2 Prototype.html`, unpacked under
[`raffa-v2/`](../../.helix/inputs/design/prototypes/raffa-v2/README.md).
Wave: epic-13 / slice `e13`.

**Colour legend used in every diagram**

| Colour | Meaning |
|---|---|
| dark blue | the user and the web SPA |
| teal | Raffa API — the modular monolith (gate, pipeline, engine, calculators, ingestion) |
| purple | Microsoft Foundry on Azure, always behind the AI Gateway |
| green | Raffa's store — Postgres + pgvector, Blob |
| amber | the market-intelligence source and its seam |
| red, dashed | paths that do **not** exist by design |

---

## 1. Big picture — three writers, one reader

```mermaid
flowchart LR
  classDef user fill:#111827,stroke:#111827,color:#ffffff
  classDef web fill:#1e3a8a,stroke:#1e3a8a,color:#ffffff
  classDef api fill:#0f766e,stroke:#0f766e,color:#ffffff
  classDef ai fill:#6d28d9,stroke:#6d28d9,color:#ffffff
  classDef store fill:#166534,stroke:#166534,color:#ffffff
  classDef market fill:#b45309,stroke:#b45309,color:#ffffff
  classDef never fill:#fef2f2,stroke:#b91c1c,color:#7f1d1d,stroke-dasharray:6 4

  U(["Procurement user"])

  subgraph WEB["Raffa web SPA"]
    direction TB
    DOCS["Documents<br/>drop one or more contracts"]
    ASKUI["Ask Raffa — home<br/>conversations · citation cards · actions"]
    SCREENS["Contract 360 · Renewals<br/>Quote check · Savings"]
  end

  subgraph API["Raffa API — Azure Container Apps"]
    direction TB
    GATE["Admission gate<br/>format → parse → classify → admit or refuse"]
    PIPE["Extraction pipeline<br/>facts · clauses · supplier · page-aware chunks"]
    ENGINE["Ask engine<br/>authz → domain gate → planner → context pack → guards"]
    CALC["Calculators<br/>renewals · criticality · levers · benchmark"]
    INGEST["Market ingestion job<br/>Worker command · CI workflow"]
  end

  subgraph AI["Microsoft Foundry on Azure — via the AI Gateway"]
    direction TB
    OCR["ocr<br/>Document Intelligence"]
    CLS["classify"]
    EXT["extract"]
    EMB["embed"]
    ANS["answer<br/>structured JSON · no tools · no web"]
  end

  subgraph STORE["Raffa store — Azure Postgres + pgvector"]
    direction TB
    T1["Tenant tables — RLS<br/>document · contract · clause · supplier<br/>conversation · message"]
    T2["Tenant RAG — RLS<br/>embedding, page-aware chunks"]
    M1["market_record — shared, read-only<br/>P25 · P50 · P75 · discounts · uplift caps · notice"]
    M2["Market RAG — shared, read-only<br/>market_embedding"]
  end

  BLOB[("Azure Blob<br/>originals + page-1 previews")]

  subgraph SRC["Market-intelligence source"]
    direction TB
    MOCK["Mock feed — today<br/>market-intelligence.mock.json"]
    LIVE["Third-party API — tomorrow"]
    PROV["IMarketIntelligenceProvider<br/>one seam, one implementation swap"]
    MOCK --> PROV
    LIVE -.-> PROV
  end

  WEBX["Public web · model memory"]

  U --> DOCS
  U --> ASKUI
  DOCS -- "1 upload" --> GATE
  GATE -- "2 parse and classify first" --> OCR
  GATE --> CLS
  GATE -- "3 refused: 422, nothing stored" --> DOCS
  GATE -- "3 admitted" --> PIPE
  PIPE --> BLOB
  PIPE -- "facts" --> EXT
  PIPE --> T1
  PIPE -- "chunks" --> EMB
  PIPE --> T2

  PROV -- "A read records" --> INGEST
  INGEST -- "B upsert numbers" --> M1
  INGEST -- "C notes to vectors" --> EMB
  INGEST --> M2

  ASKUI -- "4 question" --> ENGINE
  ENGINE -- "5 read facts" --> T1
  ENGINE -- "5 retrieve clauses" --> T2
  ENGINE -- "5 read bands" --> M1
  ENGINE -- "5 retrieve notes" --> M2
  ENGINE -- "5 compute" --> CALC
  ENGINE -- "6 pack + persona prompt" --> ANS
  ANS -- "7 answer + citation keys" --> ENGINE
  ENGINE -- "8 prose · citation cards · deep links" --> ASKUI
  ASKUI --> SCREENS
  ENGINE -. "never" .-x WEBX
  ENGINE -. "never at question time" .-x PROV

  class U user
  class DOCS,ASKUI,SCREENS web
  class GATE,PIPE,ENGINE,CALC,INGEST api
  class OCR,CLS,EXT,EMB,ANS ai
  class T1,T2,M1,M2,BLOB store
  class MOCK,LIVE,PROV market
  class WEBX never
```

Three things write into the store: the **Documents pipeline** (1–3, your
contracts, tenant-isolated by RLS), the **market ingestion job** (A–C, the
only path that ever touches the market source) and the **Ask engine**
(the two turns of every conversation). One thing reads at question time:
the Ask engine (4–8). There is no edge from the engine to the market
source and no edge from Foundry to the internet.

---

## 2. Documents intake — the admission gate runs before anything is written

```mermaid
flowchart TB
  classDef step fill:#0f766e,stroke:#0f766e,color:#ffffff
  classDef ai fill:#6d28d9,stroke:#6d28d9,color:#ffffff
  classDef ok fill:#166534,stroke:#166534,color:#ffffff
  classDef no fill:#b91c1c,stroke:#b91c1c,color:#ffffff
  classDef dec fill:#fff7ed,stroke:#b45309,color:#7c2d12

  IN["File from Documents<br/>POST /api/documents, one request per file"] --> F1{"Known format?"}
  F1 -- "no" --> R415["415 — Raffa reads PDF, Word, Excel and scanned images<br/>no AI call, nothing stored"]
  F1 -- "magic bytes: PDF · DOCX · XLSX · PNG · JPG, up to 50 MB" --> P["Parse in memory<br/>native text, or OCR through Foundry Document Intelligence"]
  P --> F2{"Readable text?"}
  F2 -- "under 200 characters — a photo of grandma" --> R422a["422 — no_readable_text<br/>nothing stored, audit hash only"]
  F2 -- "yes" --> C["classify — Foundry<br/>fixed label set + confidence"]
  C --> F3{"Contract-related?"}
  F3 -- "no — a recipe, Other, or confidence under 0.6" --> R422b["422 — not_a_contract<br/>nothing stored, audit hash only"]
  F3 -- "MSA · Order Form · SOW · Amendment · Renewal letter · Quote · Invoice · Price list · NDA · DPA" --> S["Store original + preview in Blob<br/>document row in Postgres, RLS"]
  S --> X["Staged extraction — Foundry extract<br/>dates · spend · notice · uplift · liability · supplier"]
  X --> I["Index page-aware chunks — Foundry embed<br/>tenant RAG"]
  I --> ST{"Weak critical field?"}
  ST -- "below 80 percent" --> NR["needs_review — accept or correct in Documents"]
  ST -- "all above threshold" --> DONE["completed — validated, askable"]
  NR -- "Mark as validated" --> DONE

  class IN,P,S,X,I,NR step
  class C ai
  class DONE ok
  class R415,R422a,R422b no
  class F1,F2,F3,ST dec
```

A refused file leaves one audit row (a hash, the detected type, the
confidence, the reason) and nothing else: no blob, no document row, no
embedding. Only `completed` documents feed Ask, Portfolio and Renewals.

---

## 3. Market intelligence — the source feeds the store, the store answers

```mermaid
flowchart LR
  classDef market fill:#b45309,stroke:#b45309,color:#ffffff
  classDef api fill:#0f766e,stroke:#0f766e,color:#ffffff
  classDef ai fill:#6d28d9,stroke:#6d28d9,color:#ffffff
  classDef store fill:#166534,stroke:#166534,color:#ffffff

  MOCK["Mock feed — today<br/>60+ records: SaaS · cloud · insurance (Allianz, AXA, Zurich)<br/>facilities · telco · logistics · services"]
  LIVE["Third-party market API — tomorrow<br/>how companies close contracts"]
  PROV["IMarketIntelligenceProvider<br/>MarketDeal records"]
  JOB["Ingestion job<br/>idempotent, versioned by feed<br/>Worker command or seed-market-intelligence.yml"]
  NOTE["Note composer<br/>one narrative per record"]
  EMB["Foundry embed"]
  REC["market_record<br/>P25 · P50 · P75 · sample size · discount achieved<br/>uplift cap · notice days · negotiated clauses<br/>provenance: representative · mock feed · updated date"]
  IDX["market_embedding<br/>shared, read-only vector index"]
  BENCH["IBenchmarkService<br/>deterministic band comparison"]
  RAG["IMarketKnowledgeRetrieval<br/>market notes for the context pack"]
  ASK["Ask engine"]

  MOCK --> PROV
  LIVE -.->|"same seam, one implementation swap"| PROV
  PROV -->|"A read"| JOB
  JOB -->|"B upsert numbers"| REC
  JOB --> NOTE --> EMB -->|"C upsert vectors"| IDX
  REC --> BENCH --> ASK
  IDX --> RAG --> ASK
  ASK x-. "never at question time" .-x PROV

  class MOCK,LIVE,PROV market
  class JOB,NOTE,BENCH,RAG,ASK api
  class EMB ai
  class REC,IDX store
```

Swapping the mock for the live API is one new implementation of
`IMarketIntelligenceProvider` behind the same job. Every market number Ask
shows carries its provenance label until the live provider lands. A thin
sample stays *insufficient market data* — never a precise-looking number.

---

## 4. One Ask turn, in sequence

```mermaid
sequenceDiagram
  autonumber
  actor U as Procurement user
  participant SPA as Web SPA — Ask Raffa
  participant API as Raffa API — Ask engine
  participant PG as Postgres + pgvector<br/>tenant tables · tenant RAG · market_record · market RAG
  participant F as Foundry on Azure<br/>classify · embed · answer
  participant G as Guards<br/>grounding · numeric · actions

  U->>SPA: Is my Allianz contract above market?
  SPA->>API: POST /api/conversations/:id/messages
  rect rgb(224, 242, 254)
    Note over API: Authorization scope first — tenant, user, role — before any retrieval
    API->>API: Domain gate — greeting · off-domain · legal · capability · needs-document · in-domain
    opt ambiguous turn
      API->>F: classify with a fixed label set
      F-->>API: label + confidence
    end
    API->>API: Planner — market_compare, renewal_strategy, portfolio_strategy, ...
  end
  rect rgb(220, 252, 231)
    Note over API,PG: Context pack — built only from Raffa's own store
    API->>PG: validated contract facts, clauses, weak facts (RLS)
    API->>F: embed the question
    F-->>API: vector
    API->>PG: tenant RAG top-k with tenant filter · market RAG top-k · market_record bands
    API->>API: calculators — band vs unit price, levers, criticality, strategy pack
  end
  rect rgb(237, 233, 254)
    Note over API,F: Narration — persona prompt + pack + last turns · no tools · no web
    API->>F: answer
    F-->>API: structured JSON — canDetermine, answerMarkdown, citationKeys, actionKeys, followUps
  end
  rect rgb(254, 243, 199)
    Note over API,G: Guards — every citation key, number, date and action must exist in the pack
    API->>G: verify
    alt guards pass
      G-->>API: ok
    else violation
      G-->>API: regenerate once, then abstain with the pack's own facts
    end
  end
  API->>PG: append both turns — citations, actions, AI metadata hash
  API-->>SPA: kind · answerMarkdown · citations · actions · provenance · followUps
  SPA-->>U: prose + citation cards (contract page · market record · Raffa feature) + deep links
```

---

## 5. Inside the engine — every question ends in exactly one kind of reply

```mermaid
stateDiagram-v2
  direction LR
  [*] --> Authz: question arrives
  Authz --> Gate: tenant, user and role resolved
  state Gate {
    direction TB
    [*] --> Classify
    Classify --> Greeting
    Classify --> OffDomain
    Classify --> Legal
    Classify --> Capability
    Classify --> NeedsDocument
    Classify --> InDomain
  }
  Greeting --> Redirect
  OffDomain --> Redirect
  NeedsDocument --> Redirect
  Legal --> Refusal
  Capability --> Catalog
  InDomain --> Planner
  Planner --> Pack: fixed intents
  Pack --> Answer: Foundry answer, no tools
  Answer --> Guards
  Guards --> Reply: every key, number, date and action is in the pack
  Guards --> Regenerate: violation
  Regenerate --> Answer: once
  Regenerate --> Abstain: still failing
  Redirect --> [*]: warm decline + a real contract of this tenant, zero retrieval
  Refusal --> [*]: commercial analogue + Contract 360 action
  Catalog --> [*]: feature cards + deep links
  Reply --> [*]: markdown + citations + actions
  Abstain --> [*]: insufficient evidence, the pack's facts shown
```

Four reply kinds, four layouts in the UI: `answer` (prose, cards,
buttons), `redirect` and `refusal` (warm prose + one CTA), `abstain` (the
accent-left block, only when there is truly nothing to stand on).

---

## 6. The store — what is tenant-isolated and what is shared

```mermaid
erDiagram
  TENANT ||--o{ DOCUMENT : owns
  TENANT ||--o{ CONTRACT : owns
  TENANT ||--o{ SUPPLIER : owns
  TENANT ||--o{ CONVERSATION : owns
  SUPPLIER ||--o{ CONTRACT : "is named on"
  CONTRACT ||--o{ DOCUMENT : "is proven by"
  CONTRACT ||--o{ CLAUSE : has
  CONTRACT ||--o{ RISK : has
  DOCUMENT ||--o{ EMBEDDING : "is chunked into"
  CONVERSATION ||--o{ CONVERSATION_MESSAGE : has
  MARKET_RECORD ||--o{ MARKET_EMBEDDING : "is narrated as"

  DOCUMENT {
    uuid id PK
    uuid tenant_id "RLS"
    string file_name
    string document_type "MSA OrderForm SOW Amendment RenewalLetter Quote Invoice PriceList NDA DPA"
    string processing_status "uploaded processing needs_review completed failed"
    int page_count
    string preview_path
  }
  CONTRACT {
    uuid id PK
    uuid tenant_id "RLS"
    uuid supplier_id FK
    date end_date
    date cancellation_deadline
    decimal annual_spend
    bool auto_renewal
  }
  SUPPLIER {
    uuid id PK
    uuid tenant_id "RLS"
    string name
    string normalized_name "unique per tenant"
  }
  EMBEDDING {
    uuid id PK
    uuid tenant_id "RLS"
    int page
    string section
    text chunk_text
    vector vector "1536"
  }
  CONVERSATION {
    uuid id PK
    uuid tenant_id "RLS"
    string user_id "token subject or X-User-Id"
    uuid scope_contract_id
    string title
  }
  CONVERSATION_MESSAGE {
    uuid id PK
    uuid tenant_id "RLS"
    string kind "answer abstain redirect refusal"
    text markdown
    json citations
    json actions
    string input_hash "AI metadata, never the raw pack"
  }
  MARKET_RECORD {
    string record_id PK
    string feed_version "shared, no tenant_id"
    string supplier
    string category
    decimal p25
    decimal p50
    decimal p75
    int sample_size
    string provenance "representative mock feed"
  }
  MARKET_EMBEDDING {
    uuid id PK
    string record_id FK "shared, no tenant_id"
    text chunk_text
    vector vector "1536"
  }
```

Every tenant table carries `tenant_id` and a Postgres row-level-security
policy (ADR-009): the application passes the tenant, the database is the
backstop. The two market tables have no `tenant_id`: they are Raffa's own
knowledge, readable by every workspace, writable only by the ingestion job,
never joined with tenant rows.

---

## 7. Where it runs — `dev` and `demo`, isolated twins

```mermaid
flowchart LR
  classDef web fill:#1e3a8a,stroke:#1e3a8a,color:#ffffff
  classDef api fill:#0f766e,stroke:#0f766e,color:#ffffff
  classDef ai fill:#6d28d9,stroke:#6d28d9,color:#ffffff
  classDef store fill:#166534,stroke:#166534,color:#ffffff
  classDef ops fill:#374151,stroke:#374151,color:#ffffff

  subgraph GH["GitHub — lucalamalfa91/raffa"]
    direction TB
    CI["CI — build, test, golden set<br/>schema apply on deploy (ADR-021)"]
    SEED["seed-market-intelligence.yml<br/>reprocess-tenant-documents.yml"]
  end

  subgraph ENV["One Azure environment — dev, and demo as its isolated twin (North Europe)"]
    direction LR
    SWA["Static Web Apps<br/>React SPA, Entra ID sign-in"]
    subgraph CA["Container Apps"]
      direction TB
      APIH["Raffa API<br/>admission gate · pipeline · Ask engine"]
      WRK["Raffa Worker<br/>renewal scheduler · market ingestion"]
    end
    PGX[("Postgres Flexible Server<br/>pgvector · RLS · one database")]
    BL[("Blob Storage<br/>tenant-prefixed")]
    KV["Key Vault<br/>managed identity, no secrets in code"]
    subgraph FH["Foundry hub — one project per environment"]
      direction TB
      FP["Foundry project<br/>classify · extract · embed · answer"]
      DI["Document Intelligence<br/>prebuilt-read · prebuilt-layout"]
    end
  end

  CI -- "deploy" --> CA
  CI -- "deploy" --> SWA
  SEED -- "operator jobs" --> WRK
  SWA -- "HTTPS, X-Tenant-Id" --> APIH
  APIH --> PGX
  APIH --> BL
  APIH -- "AI Gateway" --> FP
  APIH -- "AI Gateway" --> DI
  WRK --> PGX
  WRK -- "AI Gateway" --> FP
  APIH --> KV
  WRK --> KV

  class SWA web
  class APIH,WRK api
  class FP,DI ai
  class PGX,BL store
  class CI,SEED,KV ops
```

`dev` deploys on every merge to `main`; `demo` is promoted by tag with a
required review (ADR-016). The two environments share no data, no identity
and no Foundry project (ADR-006, ADR-008, ADR-011).

---

## 8. Where each claim in an answer comes from

| Claim in an answer | Source of truth | Written by | Read at question time |
|---|---|---|---|
| Dates, spend, notice, uplift, liability of *your* contract | Postgres tenant tables (RLS) | Documents pipeline (extract role), your corrections | yes |
| Clause wording with page and section | Tenant RAG `embedding` | Documents pipeline (embed role) | yes, tenant-filtered |
| Market band P25–P75, discounts, uplift caps, notice norms | `market_record` | Ingestion job from the provider | yes |
| "Companies of your size usually obtain…" | Market RAG `market_embedding` | Ingestion job (notes → embed role) | yes |
| Opening / range / walk-away, criticality, strategy steps | Calculators — `Raffa.Insights`, `Raffa.Renewals`, `Raffa.Benchmark` | computed per turn from the rows above | yes |
| "Open Renewals →", "Upload in Documents" | Capability catalog — `Raffa.Chat` | versioned in code | yes |
| Anything else — the web, the model's memory | — | — | **never**: the guards abstain |
