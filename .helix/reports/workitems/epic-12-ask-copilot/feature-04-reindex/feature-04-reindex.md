---
id: F04
type: feature
parent: epic-12
wave: 12
status: active
---

# feature-04-reindex — Load bytes, re-OCR, re-embed

## Slice

Add `IDocumentStorage.LoadAsync`. Re-process existing tenant documents so
Ask retrieval is text, not `%PDF-1.4`.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Re-OCR / re-embed existing docs | 12 |

## Architecture decisions in force

- ADR-017, ADR-004 amendment, ADR-023

## Target repo

`raffa-backend`
