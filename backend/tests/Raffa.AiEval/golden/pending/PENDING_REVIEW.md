# Pending review and owner data (Phase 1 content)

Everything below was NOT invented. Each item names who must act and which file/task it blocks. Provenance and licence notes live inside each data file (`provenance` block). Nothing in `golden/pending/` is loaded by CI.

## A. Native-speaker review (FR / ES / DE; IT/EN also by a domain reader)

| Item | File | Why |
|---|---|---|
| Synonyms and preferred terms, 15 clause types x 5 languages | `procurement-ontology.v1.json` (clauseTaxonomy) | Model translation, not validated. Legal terms (manleva/Freistellung, Haftungsbeschränkung, prórroga tácita) need a lawyer-linguist. |
| Lever labels, renewal phase next-step copy x 5 languages | `procurement-ontology.v1.json` (leverEnum, renewalWindows) | Copy shown to users eventually; register and gendering. |
| 81 translated Ask questions (FR/ES/DE) | `ask-annotation-cases.v1.json` | Model translation of existing cases. Check naturalness and that each still triggers the same intent. |
| Obligation question templates x 5 languages | `ask-annotation-cases.v1.json` | Same. |
| Earlier drafts (glossary FR/ES/DE, negotiation and web cases) | existing files | Already listed in `CONTENT_README.md` "Still needs". |
| Inter-annotator agreement on 20 cases (F1-D05) | `ANNOTATION_RUBRIC.md` | Needs a second human annotator. |

## B. Legal review (lawyer, per jurisdiction IT / FR / ES / DE where relevant)

- Clause-type risk defaults (`riskDefault`) are starting annotations, not legal opinions. A lawyer should confirm or drop them (F1-D12, F1-D19).
- Any term reaching a customer-facing template (F1-D20, LANG-07).
- Information vs legal-advice policy (F1-D19) and AI Act art. 50 wording (F1-D22): not drafted here, no sandbox basis.

## C. Owner / product decisions (needed before the data is used)

1. Savings policy (F1-D15): is the first-12-months-only rule for hard savings a Raffa rule? How are multi-year effects shown? (`savingsRules`).
2. Renewal phase boundaries (F1-D16): proposed mapping of phases onto `Renewals:Thresholds` (365,270,180,120,90,60,30). Confirm or change.
3. Lever enum (F2-D07): add `seat_true_down` (and possibly `usage_rightsizing`)? Advisory sources list true-down as a common lever; the 12-value enum has none. Not added silently.
4. Which 15 vendors belong in the initial vendor master (F3-D12); the 15 chosen are well-known public names only.
5. Whether public list prices may be kept in the repo (see licence note in `public-price-lists.v1.json`) or must be replaced by owner-supplied offers.

## D. Needs owner or tenant data (cannot be produced from public sources)

| Item | Task | What is missing |
|---|---|---|
| 12-15 annotated offers | F6-D03 | `public-price-lists.v1.json` holds 10 public list prices (real, dated 2026-10-15). Real quotes with validity, discounts, taxes, multi-currency, scanned PDFs, IT/FR/ES/DE wording must come from the owner or partners. |
| 10 obligation/SLA questions with expected answer + cited page | F1-D14 | Needs real validated contracts. Only question templates exist. |
| Ask golden v2 (30-40 cases with required facts, PortfolioMarketPosition and Clause with empty pack) | F1-D09 | Facts depend on tenant fixtures and the live engine; only translations of existing cases were produced. |
| Category of spend, contract geography, invoicing currency fields | F1-D13 | Tenant data. |
| Real benchmark data sourcing shortlist | F1-D26 | Commercial decision. |
| RAG corpus of clauses with 90 questions and spans | F1-D27 | Needs documents with redistribution rights. CUAD (CC BY 4.0 per the Hugging Face card; Atticus page not reachable from the sandbox, verify) is an option for English only. |
| Email/PEC templates 6 x 5 languages | F1-D20 / LANG-07 | Blocked on legal review; deliberately not drafted. |
| Regulatory calendar with primary sources | F3-D11 | Not started: needs primary legal sources checked against official journals and date-stamped "verificato il". |
| Playbook v2 per category | F1-D21 | Needs a senior negotiator and a lawyer; vendor-blog percentages are not usable as benchmarks. |

## E. Data checks that could not be completed in the sandbox

- Vendor `legalEntityHints`: model knowledge, `verified: false`, no company-register check.
- `domainCheck`: Microsoft, Adobe, Oracle, SAP returned HTTP 403 (bot protection); ServiceNow did not respond. Re-check from a normal network.
- Price pages are geo-dependent; GitHub values were read through a summariser (marked `lowerConfidence`); Notion, Figma and Dropbox pages did not show whether prices were annual or monthly, recorded as `unspecified`.
- Several vendor sites (Atlassian, Zoom, HubSpot, Google Workspace, Microsoft 365) render prices client-side or block bots; no prices were taken from them.
