---
id: feature-01
type: feature
parent: epic-05
wave: R4
status: active
---

# feature-01-quote-extraction — Quote upload → line items → normalization

## Slice

Upload a supplier quote, extract line items with quantity/SKU/price/discount/term,
normalize unit economics, and allow manual product/SKU mapping correction before
assessment.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Quote upload + line-item extraction | R4 |
| us-02 | SKU normalization + correction | R4 |

## Architecture decisions in force

- ADR-002 (Quotes context)
- ADR-004 (extract + ocr roles)
- ADR-003 (PostgreSQL)
- ADR-017 (OCR in V1: scanned/image quotes reuse the same gateway `ocr` path)

## Target repo

`raffa-backend`
