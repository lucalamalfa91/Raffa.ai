---
id: us-02
type: user-story
parent: feature-04
wave: R1
status: active
---

# us-02-rag-citations — RAG retrieval + grounded answer with citations

## Story

As a **Procurement** user, I want Ask Raffa to answer semantic/legal questions with
source citations (or an explicit "cannot determine"), so that I can trust the answer.

## Acceptance criteria

- [ ] AC-1 Semantic questions retrieve authorized evidence (auth-before-retrieval).
- [ ] AC-2 Answer carries citations (document + page/section) or "cannot determine".
- [ ] AC-3 Unauthorized documents never enter the LLM context.

## Definition of done

- [ ] `dotnet test` proves citations present and cross-tenant retrieval blocked.
- [ ] honours ADR-004 (answer role), ADR-011 (auth-before-retrieval), App C #2/#4/#10.

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01 (this feature) | router dispatches to RAG |
| feature-02 us-02 (embedding) | similarity retrieval |

## Architecture decisions in force

- ADR-004 (answer role), ADR-011 (isolation), ADR-003 (pgvector), App C #2/#4/#10.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | RAG retrieval + grounded answer + citations | L | phase-16 |
| task-02 | Abstain ("cannot determine") + no-fabrication guard | M | phase-17 |

## Council decisions carried into this story

Citations or explicit abstain; auth-before-retrieval is non-negotiable.

## Open questions

- none.
