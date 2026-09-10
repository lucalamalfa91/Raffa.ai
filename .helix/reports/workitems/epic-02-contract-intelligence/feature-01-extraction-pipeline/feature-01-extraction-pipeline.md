---
id: feature-01
type: feature
parent: epic-02
wave: R1
status: active
---

# feature-01-extraction-pipeline — Document classification → extraction → persistence

## Slice

Run the async ingestion pipeline behind the AI Gateway: document classification,
native-text + OCR hybrid parsing, staged schema-constrained extraction with source
spans + confidence, and persistence of canonical facts into the Documents/Contracts
schema.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | AI Gateway + document classification | R1 |
| us-02 | Staged extraction with evidence + confidence | R1 |

## Architecture decisions in force

- ADR-004 (role-split Foundry models behind AI Gateway, including `ocr`)
- ADR-002 (Documents/Contracts context)
- ADR-011 (RAG isolation, no-training, logging)
- ADR-017 (OCR in V1: hybrid native-text + Document Intelligence, full document)

## Target repo

`raffa-backend`
