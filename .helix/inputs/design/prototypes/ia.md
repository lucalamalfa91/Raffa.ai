# Raffa — Information architecture (web V1)

## Roles on the Day-1 path
- **Workspace Admin** — everything below + Workspace & members.
- **Procurement** — everything below except member management (sees a "request access" state).
- Legal / Finance / Read-only exist in the permission model; not separate nav variants in V1.

## Navigation (left rail)
1. Home (Savings KPIs + opportunities) — R3
2. Portfolio — R1
3. Renewals — R2
4. Ask Raffa (⌘K) — R1
5. Quote check — R4
6. Documents (upload + processing status) — R0
7. Review queue — R1
8. Workspace & members (admin only) — R0

Global: Ask bar on every screen; user + sign out in rail footer.

## Route map
| Route | Screen | Primary object |
|---|---|---|
| /signin | Entra sign-in → workspace picker | Tenant |
| /workspace/members | Members & roles, invite | User, Role |
| /documents | Upload + document list + status | Document |
| /contracts | Portfolio (attention strip, filters, table) | Contract |
| /contracts/:id | Contract 360 — tabs Overview · Commercials · Products · Clauses · Obligations · Risks · Documents · Benchmark · Renewal · Activity | Contract + children |
| /contracts/:id/review | Field review / correction with evidence pane | Extraction, Correction |
| /ask | Ask Raffa — chat, citations, abstain | Query |
| /renewals | Pipeline: threshold strip, priority table, insight card + actions | Renewal |
| / (home) | Savings KPIs + opportunities | SavingsOpportunity |
| /quotes/:id | Quote check stepper: Extract → Assessment → Target → Negotiation (+ outcome) | Quote, NegotiationOutcome |

## Object model → screens
- Contract → Portfolio row, Contract 360, Review, Renewal row, Savings opportunity, Ask citation target.
- Document → Documents list, Contract 360 › Documents, Evidence pane.
- Renewal → Renewals row + insight card; Contract 360 › Renewal tab.
- SavingsOpportunity → Home table; created from Renewal action or Quote outcome.
- Quote → Quote check; outcome feeds Savings Realized.

## Cross-links (all implemented in the prototype)
- Document row → Contract 360.
- Portfolio row → Contract 360 › Overview.
- Contract 360 "Review extraction" → Review; "Open in renewals" → Renewals with contract selected.
- Review "Mark as validated" → Contract 360.
- Ask citation chip → Contract 360 › Clauses.
- Renewal action → creates opportunity → visible on Home; "Open Contract 360" → Renewal tab.
- Home opportunity row → Contract 360 › Benchmark (or Quote check for quote-type).
- Quote outcome → "See it on Home".

## Day-1 path (single clickable flow)
Sign in → pick workspace → invite Procurement user → upload contract → processing → needs_review → review 2 critical fields → Contract 360 → Ask a question with citations (+ one abstain) → Renewals: act on Salesforce → Home shows opportunity → Quote check: map SKU → assessment → target → negotiation → record outcome → Home Savings Realized updates.
