---
id: E25/F02/US01/T01
type: task
story: us-01-citation-card-backend
wave: w18
status: live
target_repo: raffa-backend
---

# task-01-citation-card-backend — Tenant citations: real viewer preview + deep-link

## Coding objective
In `backend/src/Raffa.Api/AskCopilotService.cs` (the pack composition root),
for the `PackCorpus.Tenant` clause/fact items built in `BuildClausePackAsync`
/ `BuildContractFactItem`, set `PackItem.Href` to the viewer deep-link
`/documents/{sourceDocumentId}/viewer?page={SourcePage}&clause={SourceId}`
(instead of the bare `/contracts/{id}`) and set `PackItem.PreviewUrl` to
`/api/documents/{sourceDocumentId}/preview?page={SourcePage}` (the existing
preview route) so the tenant citation card deep-links and shows a real page
preview. Leave `PackCorpus.Raffa`/`Market` items with `PreviewUrl = null` and an
`Href` CTA to their capability/contract route (never a page-preview slot), per
`PackItem`'s own doc contract (`backend/src/Raffa.Chat/Application/Pack/PackItem.cs`).
Keep the client's existing `buildTenantCitationHref` (`?page=` branch) working:
the viewer route already resolves `page` back to a clause.

## Parent story AC covered
- AC-1 tenant citation carries a real `previewUrl` + viewer `href`.
- AC-2 raffa/market citation carries CTA `href`, no page-preview `previewUrl`.
- AC-3 `PackItem` divides by corpus.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Api/AskCopilotService.cs | set viewer deep-link `Href` + real `PreviewUrl` for tenant clause/fact items |

## Context the implementer needs

**Closes: NW-55**

- **Architecture decisions in force**: ADR-024 (corpus division; `PackItem.PreviewUrl` only for tenant pages); ADR-018 w17 cl 9 (viewer route `/documents/:id/viewer?page&clause`); ADR-012 w14 cl 1 (citation deep-link).
- **Do not touch**: the viewer route itself (w17); `CitationCard.tsx` (web phase 3); the market/raffa `Href` (already a CTA route).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0, with a test proving a tenant clause pack item carries a viewer `href` + real `previewUrl` and a raffa item carries `previewUrl == null`

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | tenant item gets viewer href + previewUrl; raffa item stays null-preview | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E25/F02/US01/T01
  prompt: reports/workitems/epic-25-ask-and-quote-product-completeness/feature-02-citation-cards/us-01-citation-card-backend/tasks/task-01-citation-card-backend.md
  produces: [citation-viewer-deeplink]
  depends_on: []
  effort: M
  layer: backend
  status: live
```
