---
id: epic-02
type: epic
wave: R1
status: active
---

# epic-02-contract-intelligence — Contract Intelligence (R1)

## Business capability

Turn uploaded contracts into structured, queryable procurement intelligence: a
staged, schema-constrained extraction pipeline with **OCR in V1** (ADR-017: hybrid
native-text + Document Intelligence, full document), source/page + confidence, the
normalized Documents/Contracts schema, a portfolio screen plus Contract 360, Ask
Raffa with citations, and a human validation/correction loop that versions facts
instead of overwriting them — so that **a customer can upload contracts (digital and
scanned) and ask reliable questions** (spec §16 R1).

## Product coverage

| Source | Item |
|--------|------|
| spec §16 | R1 — Contract Intelligence (definition of success) |
| spec §4.1 | extract/classify/portfolio/360/Q&A/validation |
| spec §7 | async pipeline, staged extraction, confidence |
| spec §8 | portfolio, Contract 360, Ask Raffa, evidence |
| spec §17.1 | 100 contracts; evidence; corrections; cross-portfolio Q&A |
| ADR-002 | .NET modular monolith (Documents/Contracts, Chat contexts) |
| ADR-003 | PostgreSQL + pgvector (contract/clause/embedding schema) |
| ADR-004 | Foundry model roles (ocr/classify/extract/embed/answer) |
| ADR-017 | OCR in V1 (hybrid native-text + Document Intelligence behind the gateway) |
| ADR-009 | tenancy / RLS (tenant_id on every extracted fact) |
| ADR-011 | Key Vault + RAG isolation (auth-before-retrieval) |

## Features

| ID | Title | Wave |
|----|-------|------|
| feature-01 | extraction-pipeline (ocr/parse → classify → extract → persist) | R1 |
| feature-02 | contract-schema (Documents/Contracts entities + migrations) | R1 |
| feature-03 | portfolio-contract-360 (portfolio list + 360) | R1 |
| feature-04 | ask-raffa-citations (structured/RAG Q&A + evidence) | R1 |
| feature-05 | validation-corrections (human review + versioned history) | R1 |
| feature-06 | r1-integration | R1 |

## Success looks like

On `demo`: a Procurement user uploads a contract PDF (born-digital **and** at least
one scanned/image fixture), the worker OCRs/parses the **full** document, classifies
and extracts key terms with page + confidence, the contract appears in the portfolio
and Contract 360, Ask Raffa answers a cross-portfolio question with citations, and a
low-confidence field can be corrected without destroying the original extraction.

## Architecture decisions in force

- ADR-002, ADR-003, ADR-004, ADR-009, ADR-011, ADR-017.

## Out of scope

- Renewal/savings/quote computations (R2–R4) beyond what the extraction schema exposes.
- Benchmark provider calls (R3) and negotiation strategy (R4).
- Full CLM authoring, e-signature, supplier onboarding (spec §1.2 non-goals).
