**RAFFA**

# **V1 Technical & Product

Specification**

AI-native Procurement & Contract Intelligence Platform


|     | **Developer handoff objective**Define the implementation-ready scope for Contract Intelligence, Renewal Intelligence, Savings Intelligence and New Purchase / Quote Check, while preserving a modular data architecture that can evolve into an autonomous procurement platform. |
| --- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |



| **Document**     | **Value**                                                                                               |
| ---------------- | ------------------------------------------------------------------------------------------------------- |
| Version          | 1.0                                                                                                     |
| Status           | V1 Developer Handoff                                                                                    |
| Date             | 25 August 2026                                                                                          |
| Primary audience | Technical co-founder / software developer / solution architect                                          |
| Core principle   | Raffa owns the normalized procurement intelligence layer; external data providers remain replaceable. |


**North Star**

*“Raffa knows what we bought, what we pay, when we need to act, and where we can save money.”*

# **Contents**

**1.** Product objective and V1 scope

**2.** Architectural principles

**3.** Users, tenancy and permissions

**4.** V1 functional requirements

**5.** High-level system architecture

**6.** Core procurement data model

**7.** Document ingestion and AI extraction

**8.** Contract Intelligence

**9.** Renewal Intelligence

**10.** Savings Intelligence and benchmarking

**11.** New Purchase / Quote Check

**12.** Negotiation intelligence and data flywheel

**13.** API, events and integration strategy

**14.** Security, privacy and governance

**15.** Reliability, observability and cost controls

**16.** Delivery plan and implementation backlog

**17.** Acceptance criteria and KPIs

**18.** Key challenges and architectural mitigations

**19.** Long-term platform strategy

**20.** Definition of V1 done

**Appendix A —** Core API catalogue

**Appendix B —** Core event catalogue

**Appendix C —** Developer decision rules

# **1. Product Objective and V1 Scope**

Raffa is an AI-native Procurement Intelligence Platform. V1 transforms contracts and supplier quotes into structured, queryable and actionable procurement intelligence. The product is not intended to replace ERP, CLM or P2P systems in its first phase; it sits above them as the intelligence and decision layer.


|     | **V1 mission**Give Procurement a trusted view of contracts, renewals, pricing position and savings opportunities — and make a new supplier quote assessable in minutes. |
| --- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |



| **V1 job**                    | **User outcome**                                                          | **Primary value**                |
| ----------------------------- | ------------------------------------------------------------------------- | -------------------------------- |
| 1. Contract Intelligence      | Upload contracts, structure key terms, ask questions with evidence.       | Visibility and speed             |
| 2. Renewal Intelligence       | Prioritize upcoming renewals and cancellation deadlines.                  | Avoid leakage and late action    |
| 3. Savings Intelligence       | Compare current prices with market benchmarks and quantify opportunities. | Measurable savings               |
| 4. New Purchase / Quote Check | Assess a supplier proposal before signature.                              | Negotiate before spend is locked |




## **1.1 Product loops**


| Existing contract: Contract → Structure → Benchmark → Identify Opportunity → Negotiate → Record Outcome → Renewal New purchase: Quote → Benchmark → Assessment → Negotiate → Contract → Monitor → Renewal |
| --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |




## **1.2 Explicit V1 non-goals**

Full CLM and contract authoring

Electronic signature

Purchase order and invoice management

Supplier onboarding

Full sourcing / RFP platform

ERP replacement

Autonomous supplier communication without human approval

Complex enterprise approval orchestration

# **2. Architectural Principles**

**System of intelligence, not system of record.** ERP/CLM/P2P can remain authoritative transaction systems; Raffa creates the normalized intelligence layer.

**Source evidence is mandatory.** Every critical extracted fact must preserve document, page/section and confidence.

**AI is not the database.** The LLM extracts and reasons; canonical facts live in structured storage.

**Benchmark providers are interchangeable.** Business logic calls a Raffa Benchmark Service, never a provider directly.

**Human-in-the-loop for consequential decisions.** Low-confidence extraction, benchmark matching and negotiation recommendations must be reviewable.

**Modular monolith first.** Prefer a clean modular backend plus workers and queues over premature microservices.

**API-first and event-ready.** All application capabilities should be accessible by API and emit domain events.

**Data flywheel by design.** Capture corrections and negotiation outcomes from day one; these become proprietary intelligence.

# **3. Users, Tenancy and Permissions**



## **3.1 Roles**


| **Role**             | **Core permissions**                                                                       |
| -------------------- | ------------------------------------------------------------------------------------------ |
| Workspace Admin      | Workspace config, users, roles, uploads/deletion, integrations, all contracts, audit logs  |
| Procurement          | Contracts, spend, renewals, benchmarks, savings, quote checks, negotiation recommendations |
| Legal                | Clauses, risks, liability, obligations, termination, evidence                              |
| Finance              | Spend, financial obligations, payment terms, savings                                       |
| Read-only / Business | Authorized search and Q&A without editing                                                  |




## **3.2 Multi-tenancy**

Every business object must carry tenantid. Tenant isolation must be enforced at both application and database level. No cross-tenant query path is acceptable.


| User → Tenant → Role / Object Permission → Application Query → Retrieval / AI Context |
| ------------------------------------------------------------------------------------- |


Use database Row Level Security or equivalent where practical.

Design storage paths, caches, queues and search indexes with tenant scoping.

Future enterprise option: dedicated database/storage per tenant without rewriting the domain model.

Authorization must also constrain RAG retrieval; inaccessible contracts must never enter the LLM context.

# **4. V1 Functional Requirements**



## **4.1 Contract Intelligence**

Upload PDF, DOCX and XLSX commercial/contract documents; support email attachments later.

Classify document type: MSA, Order Form, SOW, Amendment, Quote, Invoice, Price List, NDA, DPA, Other.

Extract supplier, product/SKU, spend/TCV, quantity, dates, renewal, cancellation, auto-renewal, price uplift, payment terms, SLA, termination, liability, obligations and risks.

Allow portfolio search and Contract 360.

Support natural-language Q&A with source citations.

Allow user validation/correction; preserve original AI extraction and correction history.

## **4.2 Renewal Intelligence**

Calculate renewal dates and cancellation deadlines from validated structured data.

Generate renewal opportunities and configurable threshold alerts.

Combine spend, time urgency, benchmark opportunity, uplift risk and contract risk into a priority score.

Recommend an action and show the supporting facts separately from AI recommendations.

## **4.3 Savings Intelligence**

Display annual spend analyzed, savings identified, savings realized, savings in progress, contracts analyzed and upcoming renewals.

Normalize current unit price and compare with benchmark P25/P50/P75 where supported.

Calculate current percentile, recommended target and savings range.

Show benchmark confidence and provenance.

Create a trackable SavingsOpportunity with status, owner and realized outcome.

## **4.4 New Purchase / Quote Check**

Upload supplier proposal.

Extract line items, quantities, SKU/edition, prices, discounts and terms.

Match the quote to the benchmark model.

Flag above/in-line/below market positions.

Recommend target range and potential savings.

Generate a negotiation strategy with explainable levers.

Allow the user to correct product/SKU matching before accepting the assessment.

# **5. High-Level System Architecture**

  
Figure 1 — Raffa target architecture and roadmap

## **5.1 Deployable V1 topology**


| Browser / Web App │ ▼ Backend API (modular monolith) ├── Identity / Workspace ├── Documents / Contracts ├── Suppliers / Products ├── Renewals / Savings / Quotes ├── Benchmark Service └── AI Gateway │ ├──────────────► Background Worker / Queue │ ├──────────────► PostgreSQL + pgvector │ ├──────────────► Object Storage │ └──────────────► External Providers / Enterprise Integrations |
| --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |



|     | **V1 architecture choice**Deploy a modular monolith, a separate background worker, relational database, object storage and queue. Keep module boundaries explicit so services can be separated later only when scale or team ownership requires it. |
| --- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |




# **6. Core Procurement Data Model**

The normalized data model is a strategic asset. It must not mirror a single external provider or one customer's CLM schema.


| **Entity**         | **Minimum V1 fields**                                                                                                                                                             |
| ------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Supplier           | id, tenant_id, canonical_name, legal_name, website, category, country, parent_company, external_ids                                                                               |
| Product            | id, supplier_id, name, SKU, product_family, edition, category, billing_metric                                                                                                     |
| Contract           | id, tenant_id, supplier_id, type, status, currency, start/end/effective dates, annual_spend, TCV, auto_renewal, renewal_term, cancellation_deadline, payment terms, governing law |
| ContractLineItem   | contract_id, product_id, SKU, description, quantity, unit, unit_price, list_price, discount, billing_period, annual_cost, total_cost                                              |
| ContractClause     | contract_id, clause_type, raw_text, normalized_value, risk_level, source_document, page/section, confidence                                                                       |
| Obligation         | contract_id, party, type, description, due date/recurrence, criticality, status, source                                                                                           |
| Document           | tenant_id, contract_id/quote_id, filename, document_type, mime_type, storage_path, checksum, version, processing_status                                                           |
| Renewal            | contract_id, renewal_date, cancellation_deadline, days_until_renewal, benchmark_status, savings range, risk/priority scores, owner                                                |
| Quote / QuoteLine  | supplier, dates, currency, values, status; line-level product/SKU, quantity, unit price, discount, cost                                                                           |
| SavingsOpportunity | supplier, contract/quote, type, current_spend, estimated savings range, confidence, status, owner                                                                                 |
| NegotiationOutcome | original quote, target, final price, savings, discount, duration, levers used                                                                                                     |




## **6.1 Contract hierarchy**


| Supplier └── Contract Family ├── MSA ├── Order Form ├── Amendment ├── SOW └── Renewal Letter |
| -------------------------------------------------------------------------------------------- |


The relationship model must exist from V1 even if advanced legal precedence resolution is introduced later. Amendments and renewals may override earlier terms.

# **7. Document Ingestion and AI Extraction**



## **7.1 Asynchronous pipeline**


| Upload ↓ Object Storage ↓ Processing Job ↓ Document Classification ↓ Native Text Extraction / OCR if required ↓ Section + Table Detection ↓ Structured AI Extraction ↓ Schema Validation ↓ Entity Resolution / Normalization ↓ Canonical Data ↓ Embeddings / Search Index ↓ Ready / Needs Review |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |



| **Status**   | **Meaning**                                     |
| ------------ | ----------------------------------------------- |
| uploaded     | Document stored; processing not started         |
| processing   | Parser/OCR/extraction active                    |
| needs_review | Critical low-confidence or ambiguous fields     |
| completed    | Canonical contract data ready                   |
| failed       | Processing error requiring retry or user action |




## **7.2 Extraction strategy**

Avoid one giant prompt. Split extraction into bounded, schema-constrained tasks:

Document classification

Basic metadata

Commercial terms

Dates and renewal terms

Price / SKU / line-item extraction

Legal clauses

Obligations

Risk analysis

## **7.3 Structured output and confidence**


| { "auto_renewal": true, "renewal_term_months": 12, "cancellation_notice_days": 90, "source": {"page": 12, "section": "8.4"}, "confidence": 0.97 } |
| ------------------------------------------------------------------------------------------------------------------------------------------------- |



| **Confidence** | **Default behavior**                                             |
| -------------- | ---------------------------------------------------------------- |
| > 95%          | Automatically accept unless field is configured as always-review |
| 80–95%         | Accept but visually flag                                         |
| < 80%          | Require human review before consequential use                    |



|     | **Critical fields**Contract value, cancellation, termination, renewal date and price uplift require stricter validation because an error can create direct financial or legal impact. |
| --- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |




# **8. Contract Intelligence**



## **8.1 Portfolio screen**

Required columns and filters should support rapid procurement triage.


| **Columns**                                                                                              | **Filters**                                                           |
| -------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------- |
| Supplier, Contract, Annual Spend, Start, End, Renewal, Cancellation Deadline, Auto-renewal, Risk, Status | Supplier, Category, Renewal period, Spend, Status, Risk, Auto-renewal |




## **8.2 Contract 360**

Header: supplier, contract name/type, annual spend, TCV, start/end, renewal date, cancellation deadline.

Tabs: Overview, Commercials, Products, Clauses, Obligations, Risks, Documents, Benchmark, Renewal, Activity.

## **8.3 Ask Raffa**

The query engine must route structured questions to deterministic queries and semantic/legal questions to RAG.


| User question ↓ Authorization filter ↓ Intent detection ├── Structured query (SQL / filters) └── Semantic retrieval (contract sections / clauses) ↓ Relevant evidence ↓ LLM ↓ Answer + citations |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |



| **Question**                                 | **Preferred retrieval**                              |
| -------------------------------------------- | ---------------------------------------------------- |
| Which contracts renew in the next 120 days?  | Structured SQL on validated renewal fields           |
| What is our Microsoft annual spend?          | Structured aggregation on supplier + contract values |
| What liability do we have with AWS?          | Clause retrieval + structured clause metadata        |
| Which contracts contain unlimited liability? | Clause classification / semantic search + evidence   |




## **8.4 Evidence requirement**


|     | **No evidence, no claim**Every consequential answer must expose the source document and page/section. The user must be able to open the original evidence directly. |
| --- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |




# **9. Renewal Intelligence**



## **9.1 Renewal generation**


| Daily scheduler for each active contract: calculate renewal date calculate cancellation deadline calculate days remaining create/update renewal opportunity emit threshold events if applicable |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |


Default threshold windows: 365 / 270 / 180 / 120 / 90 / 60 / 30 days; keep configurable.

## **9.2 Priority score**


| Priority Score = Spend Weight + Time Urgency + Benchmark Opportunity + Price Increase Risk + Contract Risk |
| ---------------------------------------------------------------------------------------------------------- |


Store both total score and component scores so the recommendation is explainable and tunable.

## **9.3 Renewal insight card**


| **Field**             | **Example**           |
| --------------------- | --------------------- |
| Supplier / renewal    | Salesforce — 134 days |
| Annual spend          | CHF 640k              |
| Cancellation deadline | 90 days               |
| Annual uplift         | 7%                    |
| Market position       | 18% above benchmark   |
| Potential savings     | CHF 80–120k           |
| Recommended action    | Start negotiation now |




# **10. Savings Intelligence and Benchmarking**



## **10.1 Procurement homepage**


| **KPI**               | **Meaning**                                        |
| --------------------- | -------------------------------------------------- |
| Annual Spend Analyzed | Spend represented by processed/validated contracts |
| Savings Identified    | Potential range or approved opportunity            |
| Savings Realized      | Verified negotiated/implemented savings            |
| Savings In Progress   | Approved/negotiating opportunities                 |
| Contracts Analyzed    | Contracts with completed processing                |
| Upcoming Renewals     | Actionable renewal pipeline                        |




## **10.2 Benchmark service boundary**


| Application / Renewal / Quote modules ↓ Benchmark Service ↓ Provider Adapter API ├── Provider A ├── Provider B ├── Internal Dataset └── Customer History |
| -------------------------------------------------------------------------------------------------------------------------------------------------------- |



|     | **Strategic requirement**No business module should depend on Tropic, Vendr or any single provider schema. Only the adapter understands provider-specific APIs and licensing constraints. |
| --- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |




## **10.3 Internal benchmark contract**


| getBenchmark( supplier, product, sku, geography, quantity, term, currency, purchase_date ) |
| ------------------------------------------------------------------------------------------ |



| **Normalized response** | **Required**                            |
| ----------------------- | --------------------------------------- |
| P25 / P50 / P75         | Yes when provider supports distribution |
| Metric / currency       | Yes                                     |
| Sample size             | If available                            |
| Confidence              | Yes — Raffa score                     |
| Source/provider         | Yes                                     |
| Updated at              | Yes                                     |
| Comparison dimensions   | Yes                                     |
| License restrictions    | Store internally where relevant         |




## **10.4 Benchmark matching**

Matching must use more than supplier name. Relevant dimensions include supplier, product, SKU, edition, geography, currency, quantity tier, contract term, customer size, purchase date and billing metric.


|     | **Benchmark trust**Expose benchmark confidence and comparability. A precise-looking number from weak comparables is more dangerous than an explicit 'insufficient market data' result. |
| --- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |




# **11. New Purchase / Quote Check**



## **11.1 Workflow**


| Upload supplier quote ↓ Identify supplier ↓ Extract products / SKUs / quantity / price / discount / term ↓ Normalize unit economics ↓ Match benchmark ↓ Market assessment ↓ Savings range + target range ↓ Negotiation recommendation ↓ User validation / outcome tracking |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |




## **11.2 Assessment output**


| **Field**             | **Example**               |
| --------------------- | ------------------------- |
| Supplier quote        | CHF 520k                  |
| Expected market range | CHF 390–470k              |
| Comparable median     | CHF 430k                  |
| Assessment            | Above market              |
| Recommended target    | CHF 410–440k              |
| Potential saving      | CHF 80–110k               |
| Benchmark confidence  | High / Medium / Low       |
| Next action           | View negotiation strategy |




## **11.3 Guardrails**

Do not generate a savings target if line-item normalization is unresolved.

Show unmatched SKUs and allow manual product mapping.

Do not present a provider percentile without provenance/confidence.

Separate arithmetic calculations from LLM-generated negotiation language.

# **12. Negotiation Intelligence and Data Flywheel**



## **12.1 Negotiation recommendation**

V1 recommendations can combine contract data, benchmark data, supplier/quote details, renewal timing, term, volume and known historical outcomes.


| **Output**                       | **Example**                                                                 |
| -------------------------------- | --------------------------------------------------------------------------- |
| Opening target                   | CHF 400k                                                                    |
| Acceptable target range          | CHF 410–440k                                                                |
| Walk-away / escalation threshold | CHF 470k                                                                    |
| Levers                           | Volume, term, utilization, alternatives, quarter-end, bundle, payment terms |
| Rationale                        | Explicit evidence for each recommended lever                                |




## **12.2 Negotiation outcome capture**


| Original Quote: CHF 520k Target: CHF 420k Final Price: CHF 435k Realized Saving: CHF 85k Discount: 16.3% Duration: 24 days Levers Used: 36-month commitment; quarter-end timing |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |




## **12.3 Strategic data flywheel**


| Customer documents / quotes ↓ Normalized commercial data ↓ External + internal benchmark ↓ Recommendation ↓ Negotiation ↓ Final outcome ↓ Anonymized / permissioned market intelligence ↓ Better benchmark + better recommendation |
| ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |



|     | **Long-term moat**Capture supplier/product taxonomy, pricing observations, benchmark matches, negotiation levers, corrections and outcomes. Subject to customer permission and legal constraints, this progressively reduces dependence on external benchmark providers. |
| --- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |




# **13. API, Events and Integration Strategy**



## **13.1 API-first domains**


| /api/workspaces /api/users /api/documents /api/contracts /api/suppliers /api/products /api/renewals /api/quotes /api/benchmarks /api/savings /api/chat /api/audit |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |




## **13.2 Event-ready architecture**


| document.uploaded document.processed contract.created contract.updated renewal.approaching benchmark.updated savings.opportunity.created quote.assessed negotiation.completed |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |




## **13.3 Background jobs**

OCR and document parsing

AI extraction and embeddings

Benchmark retrieval and refresh

Renewal calculations

Bulk imports

Email / notification delivery

## **13.4 Integration priority**


| **Priority** | **Integrations**                                                      |
| ------------ | --------------------------------------------------------------------- |
| P1 / V1      | Manual upload, benchmark API, email notifications, SSO-ready identity |
| P2           | SharePoint, Google Drive, Outlook/Gmail                               |
| P3           | SAP, Coupa, Ariba, Ivalua and CLM platforms                           |




# **14. Security, Privacy and Governance**



## **14.1 Minimum enterprise controls**

TLS encryption in transit and strong encryption at rest.

Strict tenant isolation.

RBAC; architecture ready for object/category/department-based authorization.

MFA capability and SSO-ready architecture (OIDC/SAML; Entra ID / Okta).

Secret management; no credentials in code or client-side configuration.

Encrypted backups and restore testing.

Comprehensive audit logging for access and data changes.

## **14.2 AI privacy**

Customer contract content must not be used to train public/shared models.

Implement an AI Gateway so provider calls are centralized, logged and replaceable.

Provider abstraction should support enterprise-grade LLM vendors without modifying domain modules.

Log model, model version, prompt version, timestamp and input hash for reproducibility.

## **14.3 Data lifecycle**


| Create / Ingest → Store → Use / Analyze → Retain → Archive → Delete |
| ------------------------------------------------------------------- |


Support export and deletion at tenant/workspace level.

Deletion must propagate to documents, embeddings, indexes and caches according to policy.

Retain original currency/value and preserve version history instead of destructive overwrite.

Architecture should permit regional deployment/data residency choices.

# **15. Reliability, Observability and Cost Controls**



## **15.1 Operational telemetry**


| **Category**        | **Measure**                                                    |
| ------------------- | -------------------------------------------------------------- |
| Application         | API latency, error rate, request volume                        |
| Document processing | Processing duration, success/failure, OCR/parser errors        |
| AI                  | Latency, tokens, cost, retries/fallbacks, model/provider       |
| Benchmark           | API calls, latency, match rate, provider errors, provider cost |
| Search              | Query latency, retrieval success, citation accuracy            |
| Tenant economics    | Storage, documents, AI calls, benchmark calls, processing cost |




## **15.2 Cost architecture**

Track processing cost per document, quote and tenant. Cache deterministic outputs and avoid repeated AI extraction when source content has not changed.

## **15.3 AI evaluation**

Maintain a golden dataset of representative contracts with expected values.

Evaluate extraction accuracy, dates, financials, clause classification, hallucination rate and citation accuracy.

Run regression tests on prompt/model changes.

Do not deploy AI changes solely because individual examples 'look better'.

## **15.4 Backup / recovery**

Daily database backups and point-in-time recovery where supported.

Object storage versioning.

Encrypted backup copies.

Periodic restore tests.

Define RPO/RTO before enterprise production commitments.

# **16. Delivery Plan and Implementation Backlog**


| **Release**                | **Scope**                                                                  | **Definition of success**                                |
| -------------------------- | -------------------------------------------------------------------------- | -------------------------------------------------------- |
| R0 — Foundation            | Auth, workspace, multi-tenancy, roles, upload, storage, DB, audit baseline | A secure workspace can ingest documents                  |
| R1 — Contract Intelligence | Extraction, schema, portfolio, Contract 360, Q&A, citations, validation    | Customer can upload contracts and ask reliable questions |
| R2 — Renewals              | Dates, cancellation deadline, alerts, dashboard, priority, recommendations | Procurement does not miss material renewal windows       |
| R3 — Savings               | Benchmark service/adapters, price comparison, savings dashboard/workflow   | Raffa quantifies credible savings opportunities        |
| R4 — Quote Check           | Quote extraction, benchmark, assessment, target, negotiation strategy      | A new proposal can be assessed in minutes                |




## **16.1 Recommended first backlog**


| **Priority** | **Backlog item**                                           |
| ------------ | ---------------------------------------------------------- |
| P0           | Tenant-aware authentication and workspace model            |
| P0           | Object storage upload + document metadata                  |
| P0           | PostgreSQL domain schema + migrations                      |
| P0           | Async queue / worker                                       |
| P0           | Document parser and classification                         |
| P0           | Structured contract extraction with source/page/confidence |
| P0           | Human validation UI                                        |
| P0           | Contract portfolio and Contract 360                        |
| P0           | Structured + semantic query router with citations          |
| P0           | Renewal calculator and scheduled jobs                      |
| P0           | Benchmark Service interface + first provider adapter       |
| P0           | Savings calculation + opportunity entity                   |
| P0           | Quote upload/extraction/assessment                         |
| P1           | Negotiation recommendation and outcome capture             |
| P1           | Email alerts                                               |
| P1           | Observability and tenant cost metrics                      |
| P1           | Prompt/model versioning + evaluation harness               |




# **17. Acceptance Criteria and KPIs**



## **17.1 Feature acceptance**


| **Area**              | **Acceptance criteria**                                                                                                                                        |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Contract Intelligence | 100 supported contracts can be processed; key dates/commercials/auto-renewal/clauses extracted; evidence shown; corrections stored; cross-portfolio Q&A works. |
| Renewal Intelligence  | Every active contract has deterministic renewal/cancellation calculation where data exists; threshold events work; recommendations do not invent dates.        |
| Savings Intelligence  | Matched contracts show current price, P25/P50/P75 where available, percentile, target range, potential saving, confidence and provenance.                      |
| Quote Check           | Quote PDF → line items → benchmark match → market assessment → savings range → negotiation recommendation; user can correct SKU matching.                      |




## **17.2 Product metrics**

Documents uploaded and contracts processed

Extraction correction rate and processing time

AI queries per active user

Renewals identified / acted on

Benchmark coverage and match rate

Savings identified / approved / realized

Quotes analyzed and quote-to-saving conversion

Negotiation outcomes captured

## **17.3 Technical metrics**

Document processing success rate

Critical-field extraction accuracy

Low-confidence field rate

Q&A citation accuracy and hallucination rate

API/search latency

AI and benchmark cost per tenant/document

# **18. Key Challenges and Architectural Mitigations**


| **Challenge**        | **Risk**                                            | **Mitigation**                                                                           |
| -------------------- | --------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| Contract variability | Different formats, language, tables, pricing models | Normalized schema + staged extraction + validation + supplier-specific enrichment        |
| Benchmark matching   | False comparables create wrong negotiation target   | Multi-dimensional matching + confidence + user correction                                |
| Provider dependency  | Pricing/API/licensing changes                       | Benchmark abstraction + multiple adapters + proprietary outcome data                     |
| AI hallucination     | Wrong legal/financial conclusion                    | Structured extraction, citations, confidence, human review, deterministic calculations   |
| Contract hierarchy   | Amendment overrides MSA                             | Document relationships + versions + later precedence engine                              |
| Access control       | Sensitive contracts exposed                         | Tenant + role + future object/category/department authorization applied before retrieval |
| Data trust           | Few visible errors destroy adoption                 | Prefer 'cannot determine reliably' over invented values; quality UI                      |
| Data residency/GDPR  | Enterprise sales blocker                            | Regional storage architecture, deletion/export, provider governance                      |
| Scaling and cost     | AI/OCR/benchmark spend grows with volume            | Async workers, caching, cost observability, modular architecture                         |




# **19. Long-Term Platform Strategy**


| **Phase** | **Product evolution**    | **Capabilities**                                                                                                        |
| --------- | ------------------------ | ----------------------------------------------------------------------------------------------------------------------- |
| V1        | Procurement Intelligence | Ask what we bought, pay, when to act, where to save                                                                     |
| V2        | Workflow Intelligence    | Tasks, alerts, approvals, supplier scorecards, negotiation packs                                                        |
| V3        | Procurement Agent        | Prepare renewals, collect evidence, create strategy, draft supplier communication, track responses — human approval     |
| V4        | Autonomous Procurement   | Goal-driven savings, prioritized sourcing/negotiation, approvals, supplier actions, system updates, continuous learning |




## **19.1 Target intelligence stack**


| Procurement Agent │ Workflow / Actions │ ┌─────────────────┼─────────────────┐ │ │ │ Contract Pricing Supplier Intelligence Intelligence Intelligence └─────────────────┼─────────────────┘ │ Procurement Graph │ Unified Data Layer │ Contracts / ERP / P2P / Market Data |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |



|     | **Architecture north star**The Unified Procurement Data Layer + Procurement Graph + proprietary pricing and negotiation outcome dataset should become Raffa's long-term moat. |
| --- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |




# **20. Definition of V1 Done**



## **Day 1**

Create a workspace and invite Procurement users.

Upload a portfolio of contracts.

Automatically classify, extract and structure supported documents.

## **After processing**

Ask reliable questions across the contract portfolio with source evidence.

See renewal and cancellation deadlines.

See relevant contract/commercial risks.

See market benchmarks where data is available.

See prioritized savings opportunities.

## **During a new purchase**

Upload a supplier quote.

Receive a line-level market assessment.

Receive a recommended target range and potential savings.

Receive an explainable negotiation strategy.

## **After negotiation**

Record the final negotiated outcome.

Track realized savings.

Use the outcome as permissioned proprietary learning data.


|     | **V1 customer promise**Raffa knows what we bought, what we pay, when we need to act, and where we can save money. |
| --- | -------------------------------------------------------------------------------------------------------------------- |




# **Appendix A — Core API Catalogue**


| **Method** | **Endpoint**                            | **Purpose**                               |
| ---------- | --------------------------------------- | ----------------------------------------- |
| POST       | /api/documents                          | Upload document and create processing job |
| GET        | /api/documents/{id}                     | Document metadata/status                  |
| GET        | /api/contracts                          | Portfolio list/filter                     |
| GET        | /api/contracts/{id}                     | Contract 360 data                         |
| PATCH      | /api/contracts/{id}                     | Validated field corrections               |
| POST       | /api/chat/query                         | Authorized structured/RAG query           |
| GET        | /api/renewals                           | Renewal pipeline                          |
| POST       | /api/renewals/{id}/action               | Update owner/status/action                |
| GET        | /api/benchmarks                         | Normalized benchmark lookup               |
| GET        | /api/savings                            | Savings opportunities                     |
| PATCH      | /api/savings/{id}                       | Status/owner/realized value               |
| POST       | /api/quotes                             | Upload/create quote                       |
| GET        | /api/quotes/{id}/assessment             | Quote assessment                          |
| POST       | /api/quotes/{id}/assessment/recalculate | Re-run after product mapping correction   |
| POST       | /api/negotiations/outcomes              | Record outcome                            |
| GET        | /api/audit                              | Authorized audit query                    |




# **Appendix B — Core Event Catalogue**


| **Event**                   | **Producer**       | **Typical consumer/action**             |
| --------------------------- | ------------------ | --------------------------------------- |
| document.uploaded           | Document API       | Start processing                        |
| document.processed          | Worker             | Update UI / trigger contract enrichment |
| document.needs_review       | Worker             | Notify owner                            |
| contract.created            | Contract module    | Index / derive renewal                  |
| contract.updated            | Contract module    | Recompute affected intelligence         |
| renewal.approaching         | Renewal engine     | Create alert/task                       |
| benchmark.updated           | Benchmark service  | Recompute opportunities                 |
| savings.opportunity.created | Savings engine     | Surface on dashboard                    |
| quote.assessed              | Quote module       | Notify requester                        |
| negotiation.completed       | Negotiation module | Update realized savings/data flywheel   |




# **Appendix C — Developer Decision Rules**

Never store critical contract truth only inside an LLM response.

Never show a consequential extracted fact without source evidence and confidence metadata.

Never call a benchmark provider directly from renewal, savings or quote business logic.

Never include data in AI retrieval that the current user is not authorized to access.

Never destructively overwrite contract history or human corrections.

Prefer deterministic arithmetic/date calculations to LLM reasoning.

Prefer a modular monolith + workers before microservices.

Instrument AI, benchmark and processing cost from the first customer.

Capture negotiation outcomes and corrections from day one.

If data quality is insufficient, return uncertainty instead of fabricated precision.


|     | **Final engineering test**For every architectural decision ask: Does this help Raffa build its own procurement intelligence layer, or are we simply building a UI around somebody else's API? |
| --- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |


