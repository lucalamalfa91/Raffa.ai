# AI flows: behaviour flags enabled in dev

Every Raffa.AiFlows behaviour change shipped Off / Off-equivalent / Shadow by default in the options
classes. The **dev** environment turns them on so the new flows can be tested; demo and the code
defaults are untouched.

## How it is delivered

- `infra/environments/dev/variables.tf`: `variable "ai_flows_env"` (`map(string)`, the full flag map as its
  default), merged in `infra/environments/dev/main.tf` into `module.foundry`'s `extra_gateway_env`. Like
  every `AiGateway__*` knob it is published to **both** Container Apps only while `ai_gateway_wired` is
  true. Neither `modules/foundry` nor `modules/containerapps` restricts key prefixes (the latter emits one
  env block per map entry). Set `ai_flows_env = {}` in the HCP workspace to return dev to code defaults;
  an entry in `ai_gateway_extra_env` overrides the same key.
- `Market:Ingestion:*` cannot ride that map: only the one-shot `ingest-market` command reads it, and it
  runs from `.github/workflows/seed-market-intelligence.yml`, not from a Container App. The workflow sets
  those three flags for `target_environment == dev` only.
- `backend/tests/Raffa.AiFlows.Tests/Configuration/DevAiFlowsEnvBindingTests.cs` reads both files, binds
  the keys through the real options classes and fails on a key that names no option (a typo would
  otherwise bind silently and leave the flag off).

## Enabled

| Env var (`:` is `__`) | Value | Purpose |
|---|---|---|
| `Documents__Extraction__UseContextDependencies` | true | F5 scheduling: list stages wait for scalar stages and receive their facts as context |
| `Documents__Extraction__MaxConcurrentStages` | 4 | F5 scheduling: cap on in-flight gateway calls (0 = all at once) |
| `Documents__Extraction__TransientRetriesPerStage` | 1 | F5 scheduling: one immediate in-run retry of a transient stage failure |
| `Documents__Extraction__SpanVerification` | Shadow | F5-T07 span verifier runs, logs and meters, changes nothing (already the default; pinned) |
| `Documents__Extraction__CrossStageValidation` | Shadow | F5-D19 cross-stage rules run, log and meter, change nothing (already the default; pinned) |
| `Documents__Extraction__DocumentFirstPrompt` | true | F5-T03 document-first, cache-friendly prompt layout (pipeline `staged-v2-docfirst`, never mixes checkpoints) |
| `Documents__Extraction__SecondOpinionShadow` | true | A-07 second reading of end date / notice / annual fee, shadow only (one extra call per stage) |
| `QuoteExtraction__HeaderStageEnabled` | true | F6-T07 header stage (supplier, date, currency, geography) |
| `Chat__AskTurn__BudgetBeforeAgents` | true | F1-T03 bound the intent pack before the agents read it |
| `Chat__AskTurn__AgentPackReserveTokens` | 1500 | F1-T03 tokens (of the 6000 default budget) kept for agents' own items; needed for the budget-first mode to be effective |
| `Chat__AskTurn__RevalidatePlaysAfterBudget` | true | F1-T03 drop a council play whose cited items the budget cut |
| `Chat__AskTurn__MemoizeTurnReads` | true | F1-T01 read each contract once per turn |
| `Chat__AskTurn__AnswerOnRunner` | true | A-05 answer role runs as the audited `answer-composer` runner step |
| `Chat__AskTurn__AgentPhaseDeadline` / `AnswerDeadline` / `TurnDeadline` | 00:02:00 / 00:02:00 / 00:04:00 | A-04 caps; a phase that hits its cap degrades explicitly (null = no cap) |
| `Chat__GapInvestigation__Mode` | Triggered | investigator only on flagged turns (already the default; pinned) |
| `Chat__GapInvestigation__ContextTurns` | 2 | INV-06 investigator also reads the previous 2 user questions (0 = off) |
| `Chat__Council__MarketResearchParallelQueries` | true | F2-T04 market researcher queries run concurrently |
| `Chat__Council__SemanticPlayRetry` | true | F2-T09 one semantic retry when no strategist play survives validation |
| `Chat__Council__MarketNoteValues` | true | F2-T10/F2-D09 market notes carry sample size and as-of date as pack values |
| `Chat__Drafting__ReuseCouncilPlays` | true | F4-T05 plan the offer from the pack's council plays, skip the planner call |
| `Chat__Drafting__EnforceTone` | true | formal / legal-tone findings send the writer back once |
| `Chat__Drafting__LocalizedTemplates` | true | LANG-07/F4-D10 fallback draft by draft type and language (FR/ES/DE and cancellation texts still need native + legal review before production) |
| `Chat__Interview__UseModelStage` | true | interview stage 2: analyst-role interpretations when the deterministic stage is unsure |
| `Chat__WebResearch__AllowReportedFigures` | true | F3-D02 keep explicit, cited figures that have no source quote (symbol/word/shorthand figures still need one) |
| `Chat__WebResearch__ChargeOnlyWhenSourcesFound` | true | F3-T03 refund the daily unit when a call finds no source |
| `Chat__WebResearch__StripTrackingParameters` | true | F3-T08 strip `utm_*`, `gclid`, ... from citation URLs |
| `Chat__WebResearch__CiteSourcesOnAbstain` | true | F3-D05 an abstain keeps its found sources as citations |
| `Chat__WebResearch__RedactSensitiveQueryPatterns` | true | F3-D10 remove IBAN / VAT / fiscal code / phone from the outbound query |
| `Market__Ingestion__BlueGreen` (workflow) | true | F7-T08 build the corpus as a new version and switch atomically |
| `Market__Ingestion__CheckpointPerBatch` (workflow) | true | F7-T03 commit after every embedding batch |
| `Market__Ingestion__EmbedBatchSize` (workflow) | 64 | F7-T03 batched embedding (1 = one call per record) |

Already on by default, nothing to do: per-agent / per-flow kill switches (`Agents__Agents__<name>__Enabled`,
`Agents__Flows__<flow>__Enabled`), `Chat__Council__Enabled`, `Chat__Drafting__Enabled`,
`Chat__GapInvestigation__Enabled`, `Chat__Interview__Enabled`, `Market__CorpusCache__Enabled`,
`Chat__WebResearch__Enabled` (own variable `web_research_enabled`, default true), and the F6 durable quote
queue (`ServiceBus__*` is already published by `modules/containerapps`).

## Not enabled, and why

| Flag | Reason |
|---|---|
| `Documents__Extraction__SpanVerification=Gate`, `CrossStageValidation=Gate` | Gate routes facts to review on false positives that Shadow has not measured yet (fuzzy span match, rule coverage). Shadow is the "on" state; flip after reading the shadow metrics. |
| `Market__Retrieval__Hybrid`, `Market__Retrieval__Diversify` | F7-D08: need the golden set to show they help; without it they only change ranking blindly. |
| `Market__Ingestion__Curation__Enabled` | Runs the data-quality and provenance agents inside `ingest-market`, whose job deliberately uses the fixture gateway (no Foundry endpoint is set). Enable together with injecting the Foundry env into that job. |
| `Market__Ingestion__EmbedTokensPerMinute` | Pure knob; moot under the fixture embed gateway. Set it with the provider quota when the job uses Foundry. |
| `Documents__Extraction__TokensPerMinuteLimit` | The limiter is per process (Worker scales to 5 replicas) so the value depends on the quota split; the gateway already retries 429. Needs an owner-chosen number. |
| `Chat__WebResearch__MaxSourceTier`, `DomainTiers`, `VendorMasterAsTier1`, `DeniedDomains` | F3-D08 source policy: needs the owner's tier list; with `MaxSourceTier` = 0 the tier policy is off and `VendorMasterAsTier1` is inert. |
| `AiGateway__QueryHash__Key` | Secret; must come from Key Vault like `AiGateway__Jev__ApiKey`, never a `.tf` literal (ADR-011). Without it audit rows keep the unkeyed hash and log one warning. |
| `AiGateway__Jev__Enabled` / `ApiKey` | Own variables (`jev_enabled`, `jev_api_key`); the pilot needs a real key. |
| `Market__Provenance__ShowLabel` (+ `RealSourceName`, `DemoLabel`) | Presentation decision D5; needs the label texts. |
| F2-D07 lever enum | Not a flag: the 12-value enum is a draft in `golden/pending` awaiting the owner's review (`seat_true_down`); nothing to switch. |
