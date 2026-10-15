# Annotation rubric (F1-D05) v0.1 DRAFT

Applies to the Ask golden set (`golden/*.json`), the investigator set (`investigator-set/`), and the drafts in this folder. Read together with `GLOSSARY.md`. Annotate in the language of the case; do not translate the question.

## 1. Ask answer cases (golden/*.json schema)

1. `expectedKind`: `answer` only if the tenant fixture holds the facts to answer it; `abstain` when the data is missing (never "answer with a guess"); `redirect` for greetings/off-domain; `refusal` for legal-advice or unsafe requests.
2. `expectedNumbers`: only figures/dates the calculator or the pack produces, copied verbatim in the fixture's format. Never a figure from your own knowledge.
3. `expectedCitationCorpora`: the corpora a careful reader would need (`tenant`, `market`, `raffa`, `calc`); leave empty when unconstrained.
4. `expectedMarkdownContains`: short fragments of prototype wording; in FR/ES/DE keep them language-neutral (a number, a supplier name, an href) until localized copy exists.
5. `knownGap`: only if the engine provably diverges and the gap id exists in `GoldenSetKnownGaps.All`.

## 2. Investigator set (INV-04)

- `gapWorthy` = true when a reasonable person says the phrase asks for something Raffa cannot do today (export, schedule, generate a deliverable, send, integrate).
- `hard` = true for contract questions that contain an operational verb or deliverable noun as the supplier's duty, the user's own obligation, or the text of a clause ("the supplier must send the invoice by the 5th").
- `category`: gap-request (explicit verb), gap-implicit (no verb; lexicon is expected to miss it), ordinary, hard-negative, unanswerable (T2).
- `expect.t3` is what the lexicon must say, not what a human would wish. Disagreements go to the changelog, not to silent relabeling.

## 3. Negotiation cases (F2-D06)

- `expectedLevers`: values of `leverEnum` (F2-D07 draft). List the levers a procurement professional would put first; 1-3 per case.
- `expectedVerdict`: `reachable` / `partial` / `not_reachable` is decided by the calculator from the grounded levers (F2-D03), so annotate only when the case declares enough facts; otherwise `abstain`.
- `goalPolarity`: `reduce_cost`, `increase_value` (the user accepts paying more), `neutral`, `none` (off-topic). A polarity case fails if the answer frames the outcome as a discount.
- `mustNotInventFigures` is always true: any figure not in `declaredFacts` or the pack is a failure.
- `category`: positive, polarity, abstain, off_topic.

## 4. Web cases (F3-D04)

- `purpose`: one of MarketPractice, SupplierNews, BenchmarkRange, NegotiationTactics, Open.
- `expectedBehaviour`: `answer` (with clickable sources), `abstain`, `off_context` (leisure question with no business angle).
- `figureStates`: `verified_or_removed` or `none` (see file). Never "unverified shown".
- `forbidTenantFiguresInWebQuery`: the query that leaves the platform must not carry tenant amounts or names beyond the supplier asked about.
- Assertions are structural because live web content changes; never annotate an expected number.

## 5. Agreement measurement (F1-D05 exit criterion)

Two annotators label the same 20 cases independently (fields `expectedKind`/`gapWorthy`/`expectedLevers`); report Cohen's kappa per field. Target kappa >= 0.7; below that, refine this rubric before scaling. The owner has stated no second annotator is available at this stage, so this measurement is PENDING and must not be claimed.
