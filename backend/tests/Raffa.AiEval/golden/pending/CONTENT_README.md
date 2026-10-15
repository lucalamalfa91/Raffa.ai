# Phase 1 content drafts (LANG-05/06/10, F1-D05, F2-D06, F3-D04, INV-04)

Everything here is data/docs. `GoldenSet.cs` enumerates `golden/*.json` with `SearchOption.TopDirectoryOnly` and the csproj copies only `golden\*.json`, so this `pending/` folder is NOT loaded and CI behaviour is unchanged. Promote a file by moving it up once a loader/evaluator for its schema exists.

## Already on main (not duplicated)

- INV-04 set: `investigator-set/investigator-eval.v1.json` already holds 150 phrases, 30 per language (20 normal + 10 hard negatives = 33% per language), with `InvestigatorEvalSet`/evaluator in `Raffa.AiEval/Investigator`. No new file written.
- T3 lexicon: `src/Raffa.AiFlows/CapabilityGaps/Investigation/Lexicon/investigator-trigger-lexicon.json` already carries `fr`/`es`/`de` entries (status `seed`). Left byte-identical (embedded-resource names untouched) rather than churn a lexicon the INV-04 set was tuned against. The other lexicons (`WebModeLexicon`, `WebResearchTopicLexicon`, IntentPlanner, DomainGate) are C# regexes, not data files, and IT/EN only: extending them is a code change (LANG-03) outside this data-only task. The off-context web cases (`web-fr-06`, `web-es-05`, `web-de-05`) and off-topic negotiation cases are written to expose that gap.

## Produced

| File | Content |
|---|---|
| `GLOSSARY.md` | 33 concepts x IT/EN/FR/ES/DE, jurisdiction notes, SOURCES |
| `ANNOTATION_RUBRIC.md` | rubric for Ask, investigator, negotiation and web cases; inter-annotator procedure |
| `negotiation-cases.v1.json` | 39 F2 cases (IT 8, EN 7, FR 8, ES 8, DE 8): positive, polarity, abstain, off-topic |
| `web-cases.v1.json` | 30 F3 cases (6 per language) over the 5 purposes, both consent paths, off-context and tenant-leak negatives |

Schemas are new and deliberately NOT the Ask `GoldenCase` schema (that one asserts `kind`, citations, hrefs against the Ask host). They are structural/behavioural; an evaluator is a later task. No live model run was done.

## Defaults taken

- Preferred term per language = first listed in the glossary; variants go to lexicons.
- Fixture-style supplier names (Salesforce, Microsoft, AWS, DocuSign) reused; all figures in cases are "declared by the user" and never expected as output numbers.
- Lever enum is a 12-value draft for F2-D07; verdict enum reachable/partial/not_reachable/abstain.
- FR figures written with a space before `%` (typographic norm); ES/DE likewise.
- Web assertions are structural (live content is non-deterministic).

## Still needs

- Native-speaker review of FR/ES/DE glossary columns and all FR/ES/DE questions (model translation only; no human annotator was available).
- Legal review per jurisdiction before any term reaches a template (LANG-07).
- Inter-annotator agreement on 20 cases (needs a second annotator).
- Decision on whether the `seed` lexicon status of fr/es/de can become `baseline`.
- An evaluator/loader for the negotiation and web schemas.

## SOURCES

See the SOURCES section of `GLOSSARY.md`.
