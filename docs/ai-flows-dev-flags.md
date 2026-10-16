# AI flows: behaviour flags enabled in dev

Every Raffa.AiFlows behaviour change shipped Off / Off-equivalent / Shadow by default in the options
classes. The **dev** environment turns **all of them** on (owner decision: everything is tested in dev);
demo and the code defaults are untouched. Only Jev and the visible market-provenance label stay off, see
"Left off" below.

## How it is delivered

- `infra/environments/dev/variables.tf`: `variable "ai_flows_env"` (`map(string)`, the full flag map as its
  default), merged in `infra/environments/dev/main.tf` into `module.foundry`'s `extra_gateway_env`. Like
  every `AiGateway__*` knob it is published to **both** Container Apps only while `ai_gateway_wired` is
  true. Neither `modules/foundry` nor `modules/containerapps` restricts key prefixes (the latter emits one
  env block per map entry). Set `ai_flows_env = {}` in the HCP workspace to return dev to code defaults;
  an entry in `ai_gateway_extra_env` overrides the same key.
- `Chat__WebResearch__DomainTiers__<domain>` keys contain dots (`salesforce.com`). ASP.NET Core keeps a
  dot inside a key segment, so they bind to the `Dictionary<string,int>`; the HCL keys are quoted.
  If Azure Container Apps ever rejects a dotted env name at apply time, drop those entries:
  `VendorMasterAsTier1` already ranks the code's own master domains at tier 1.
- `Market:Ingestion:*` cannot ride that map: only the one-shot `ingest-market` command reads it, and it
  runs from `.github/workflows/seed-market-intelligence.yml`, not from a Container App. The workflow sets
  those flags for `target_environment == dev` only (job `env`).
  - **Foundry for that job (dev).** Curation runs the AI agents, so the step "Resolve the Foundry gateway
    (dev)" copies the non-secret `AiGateway__*` env (endpoint, project, model ids) from
    `ca-raffa-dev-worker` onto the job. The runner calls Foundry as the OIDC deploy principal, which had
    no data-plane role on `aisvc-raffa`: `ci_deploy_ai_access` (dev, default true) grants it Cognitive
    Services User + OpenAI User. **What can still be missing:** (1) the HCP apply of this change has to
    have run before the workflow is dispatched; (2) `ai_gateway_wired = true` (else no endpoint is
    published and the step switches Curation off with a `::warning::`, staying on the fixture gateway);
    (3) the shared account must accept calls from the runner (public network access; if it is
    private-endpoint only the job cannot reach it); (4) if the deploy principal's object id is already in
    `ai_operator_principal_ids`, nothing changes (`distinct()`), but if it was granted the roles outside
    Terraform the apply fails with `RoleAssignmentExists`: set `ci_deploy_ai_access = false` and keep the
    manual grant.
  - `EmbedTokensPerMinute` = 80000 = 80% of the 100K TPM `text-embedding-3-small` deployment of dev.
- `AiGateway__QueryHash__Key` is a secret, wired like `AiGateway__Jev__ApiKey`: HCP sensitive variable
  `query_hash_key` (category Terraform) -> Key Vault secret `query-hash-key` (count-gated by
  `modules/keyvault`) -> secret handle `query-hash-key` -> env `AiGateway__QueryHash__Key` on the **API**
  app (the app that writes web-research audit rows). Absent = empty literal = unkeyed SHA-256 plus one
  warning, exactly as before. **Owner action:** create the HCP workspace variable `query_hash_key` in
  `raffa-dev` (sensitive, e.g. `openssl rand -base64 48`) and apply; nothing is in the repo. Rotating it
  changes the hash of the same query, so audit rows before and after no longer correlate.
- `backend/tests/Raffa.AiFlows.Tests/Configuration/DevAiFlowsEnvBindingTests.cs` reads both files, binds
  the keys through the real options classes and fails on a key that names no option (a typo would
  otherwise bind silently and leave the flag off).

## Enabled

| Env var (`:` is `__`) | Value | Purpose |
|---|---|---|
| `Documents__Extraction__UseContextDependencies` | true | F5 scheduling: list stages wait for scalar stages and receive their facts as context |
| `Documents__Extraction__MaxConcurrentStages` | 4 | F5 scheduling: cap on in-flight gateway calls (0 = all at once) |
| `Documents__Extraction__TransientRetriesPerStage` | 1 | F5 scheduling: one immediate in-run retry of a transient stage failure |
| `Documents__Extraction__SpanVerification` | **Gate** | F5-T07 a fact whose span is not found (or only on another page) goes to review; the value is kept |
| `Documents__Extraction__CrossStageValidation` | **Gate** | F5-D19 involved fields of a violated rule go to review with the rule id as reason; values never edited |
| `Documents__Extraction__DocumentFirstPrompt` | true | F5-T03 document-first, cache-friendly prompt layout (pipeline `staged-v2-docfirst`, never mixes checkpoints) |
| `Documents__Extraction__SecondOpinionShadow` | true | A-07 second reading of end date / notice / annual fee, shadow only (one extra call per stage) |
| `Documents__Extraction__TokensPerMinuteLimit` | 60000 | F5-T10 pacing, **per Worker process**: dev chat capacity 300K TPM (gpt-5.4-nano) / 5 replicas (`worker_max_replicas`). Expect a long document to take minutes (7 stages x ~document tokens at 60K TPM); if all 5 replicas are not busy the quota is under-used |
| `QuoteExtraction__HeaderStageEnabled` | true | F6-T07 header stage (supplier, date, currency, geography) |
| `Chat__AskTurn__BudgetBeforeAgents` | true | F1-T03 bound the intent pack before the agents read it |
| `Chat__AskTurn__AgentPackReserveTokens` | 1500 | F1-T03 tokens (of the 6000 default budget) kept for agents' own items |
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
| `Chat__WebResearch__VendorMasterAsTier1` | true | F3-D12 the code's vendor master domains are tier 1 |
| `Chat__WebResearch__MaxSourceTier` | 3 | F3-D08 tier policy on. An unlisted domain is tier 3, so 3 drops nothing; set 2 to keep only listed domains (strict, most non-vendor questions then abstain) |
| `Chat__WebResearch__DomainTiers__<domain>` | 1 | F3-D08 20 official domains from `golden/pending/vendor-master.v1.json`; **not** taken: `atlassian.net`, `amazonaws.com`, bare `google.com` (tenant-hosted content). No `DeniedDomains` (none invented) |
| `Market__Retrieval__Hybrid` | true | F7-T07 lexical (supplier/product named in the query) fused with the vector ranking (RRF) |
| `Market__Retrieval__Diversify` | true | F7-T07 at most 2 hits per supplier in the top-k. Hybrid/Diversify are on **without** the F7-D08 golden set (owner decision for dev) |
| `Market__Ingestion__BlueGreen` (workflow) | true | F7-T08 build the corpus as a new version and switch atomically |
| `Market__Ingestion__CheckpointPerBatch` (workflow) | true | F7-T03 commit after every embedding batch |
| `Market__Ingestion__EmbedBatchSize` (workflow) | 64 | F7-T03 batched embedding (1 = one call per record) |
| `Market__Ingestion__EmbedTokensPerMinute` (workflow) | 80000 | F7-T03 embed pacing (80% of the 100K TPM deployment) |
| `Market__Ingestion__Curation__Enabled` (workflow) | true | A-11/F7-T09 data-quality agent + provenance curator on every ingest batch (switched off with a warning when Foundry cannot be resolved) |
| `AiGateway__QueryHash__Key` (Key Vault secret) | secret | F3-T11 keyed HMAC for the `queryHash=` of audit rows |

Already on by default, nothing to do: per-agent / per-flow kill switches (`Agents__Agents__<name>__Enabled`,
`Agents__Flows__<flow>__Enabled`), `Chat__Council__Enabled`, `Chat__Drafting__Enabled`,
`Chat__GapInvestigation__Enabled`, `Chat__Interview__Enabled`, `Market__CorpusCache__Enabled`,
`Chat__WebResearch__Enabled` (own variable `web_research_enabled`, default true), and the F6 durable quote
queue (`ServiceBus__*` is already published by `modules/containerapps`).

## Left off, on purpose

| Flag | Reason |
|---|---|
| `AiGateway__Jev__Enabled` / `AiGateway__Jev__ApiKey` | Own variables (`jev_enabled`, `jev_api_key`). The pilot needs a real OpenRouter key; with `jev_enabled = true` and no key every classify call fails. Not faked. |
| `Market__Provenance__ShowLabel` (+ `RealSourceName`, `DemoLabel`) | Decision D5 = no visible label on market figures. |
| `Chat__WebResearch__DeniedDomains` | No deny-list is invented; add one when the owner has it. |
| F2-D07 lever enum | Not a flag: the 12-value enum is a draft in `golden/pending` awaiting the owner's review (`seat_true_down`). |

To turn any of the above off in dev without a code change: edit the value in `ai_flows_env` (HCP
workspace variable override) or the workflow `env`; the "se si comporta male" column of the checklist
names the one key to flip.

## Checklist di test (dev) — per l'owner

Convenzioni: "riga di audit" = la riga della conversazione (`chat` / turn audit) con i frammenti
`turnId=... runId=... flow=...`; "log" = log del Container App `ca-raffa-dev-api` o `ca-raffa-dev-worker`
(Log Analytics); "metrica" = contatore OpenTelemetry. Dopo ogni modifica a `ai_flows_env` serve un
apply HCP e un nuovo revision dei Container Apps.

### F1 — Ask (turno di domanda)

| Cosa fare | Cosa osservare | Risultato atteso | Se si comporta male |
|---|---|---|---|
| Fare una domanda su un portafoglio con molti contratti (es. "quali rinnovi scadono nel trimestre e come li negozio?") | Riga di audit del turno: `budgetBeforeAgents=on budgetDroppedBeforeAgents=N revalidatePlays=on playsUngrounded=M` | Il pacchetto è tagliato prima degli agenti; nessun gioco (play) del council cita elementi tagliati (`playsUngrounded` conta quelli scartati) | `Chat__AskTurn__BudgetBeforeAgents` e/o `Chat__AskTurn__RevalidatePlaysAfterBudget` = false |
| Stessa domanda, controllare le letture | Nessun doppio caricamento dello stesso contratto nel log; stessa risposta di prima | Risposta equivalente, meno letture | `Chat__AskTurn__MemoizeTurnReads` = false |
| Domanda normale | Frammento `runId=<guid>` sulla riga; passo audit `answer-composer` con il nome dello step | Un `runId` su ogni turno; la risposta compare come step del runner | `Chat__AskTurn__AnswerOnRunner` = false |
| Provocare lentezza (domanda pesante, o abbassare temporaneamente `Chat__AskTurn__AgentPhaseDeadline` a 00:00:05) | Frammento `deadline=agents\|answer\|turn turnMs=N` sulla riga; metrica `raffa.agent.deadline.exceeded` (tag `raffa.deadline.scope`) | La fase scaduta è degradata esplicitamente (la risposta dichiara che gli agenti sono stati saltati), mai troncata in silenzio; `deadline=none` nei turni normali | Alzare/rimuovere la chiave: `Chat__AskTurn__AgentPhaseDeadline` / `AnswerDeadline` / `TurnDeadline` |

### F2 — Negoziazione / council

| Cosa fare | Cosa osservare | Risultato atteso | Se si comporta male |
|---|---|---|---|
| Domanda di risparmio/negoziazione su un contratto con fornitore noto | Frammento `flow=` della riga di audit: step del council (analisti, stratega), query di mercato, `PlaysDropped`; log dei tempi delle query di mercato | Query di mercato in parallelo (tempo totale minore), 1–4 giochi classificati con citazioni dal pacchetto | `Chat__Council__MarketResearchParallelQueries` = false |
| Caso in cui lo stratega sbaglia una citazione (frequente con modello nano) | Nel flow: due chiamate dello strategist invece di una (retry semantico) | Al secondo tentativo almeno un gioco valido, invece di nessun gioco | `Chat__Council__SemanticPlayRetry` = false |
| Guardare le note di mercato nella risposta / carta evidenze | Valori `sample size` e `as-of` accanto allo snippet; il guard numerico accetta una cifra citata da lì | Cifre di mercato ancorate con data e numerosità | `Chat__Council__MarketNoteValues` = false |

### F3 — Ricerca web

| Cosa fare | Cosa osservare | Risultato atteso | Se si comporta male |
|---|---|---|---|
| Con opt-in dell'Admin del workspace e consenso nella domanda, chiedere "cerca sul web: novità prezzi Salesforce" | Citazioni: URL senza `utm_*`/`gclid`; fonti `salesforce.com` presenti; riga di audit con `queryHash=<64 hex>` | Fonti ufficiali del fornitore incluse, URL puliti; la query inviata non contiene IBAN/P.IVA/CF/telefono se presenti nel testo | `Chat__WebResearch__StripTrackingParameters` / `RedactSensitiveQueryPatterns` = false |
| Domanda fuori tema o senza fonti | Il contatore giornaliero del tenant non scala (unità restituita); se la ricerca trova poco, l'abstain mostra le fonti trovate come link | Abstain con citazioni cliccabili, nessun testo respinto | `Chat__WebResearch__ChargeOnlyWhenSourcesFound` / `CiteSourcesOnAbstain` = false |
| Frase con cifra esplicita (percentuale, importo con codice ISO, data) con fonte ma senza citazione testuale | La frase è mantenuta (non tagliata) | Cifre esplicite e citate restano; cifre con simbolo/parola (`$36`, `dieci euro`) sono ancora tagliate | `Chat__WebResearch__AllowReportedFigures` = false |
| Fonte fuori dall'elenco (dominio non fornitore) | Con `MaxSourceTier=3` nulla è scartato; con 2 solo i domini elencati | Nessuna fonte persa in dev | Per tornare alla policy spenta: `Chat__WebResearch__MaxSourceTier` = 0 |
| Dopo aver creato il segreto `query_hash_key` (vedi sopra) | Nel log all'avvio dell'API NON compare più il warning "AiGateway:QueryHash:Key is not configured"; lo stesso testo domandato due volte dà lo stesso `queryHash`, diverso da SHA-256 semplice | Hash con chiave | Eliminare la variabile HCP: torna l'hash non firmato (solo warning) |

### F4 — Lacune di capacità, bozze, feedback

| Cosa fare | Cosa osservare | Risultato atteso | Se si comporta male |
|---|---|---|---|
| Chiedere qualcosa che Raffa non sa fare ("manda un reminder Slack al fornitore"), poi una domanda di follow-up breve ("e per l'altro fornitore?") | Riga di audit `gapInvestigation=<esito>` (per i turni non innescati: nessuna chiamata); per l'esito `busy` il controllo è stato scartato per il bulkhead | L'investigatore parte solo sui turni innescati (`Mode=Triggered`), il follow-up è giudicato nel contesto delle 2 domande precedenti; compare l'offerta di feedback | `Chat__GapInvestigation__ContextTurns` = 0 |
| Dopo un turno di negoziazione con giochi del council, chiedere "scrivimi la mail" | Riga di audit con `draftReusedCouncilPlays=true`; una chiamata modello in meno (niente offer-planner) | La bozza riprende i giochi del council | `Chat__Drafting__ReuseCouncilPlays` = false |
| Bozza con tono troppo informale / non giuridico | Nel risultato della bozza `ToneFindings`; uno scrittore chiamato una volta in più | Dopo il rinvio, la bozza passa il controllo del tono | `Chat__Drafting__EnforceTone` = false |
| Bozza di disdetta o in FR/ES/DE con il modello non disponibile (fallback deterministico) | Il testo segue il tipo di bozza e la lingua della richiesta | Testo coerente col tipo (rinegoziazione, disdetta, controproposta). Attenzione: FR/ES/DE e disdetta non hanno revisione legale | `Chat__Drafting__LocalizedTemplates` = false |
| Domanda ambigua ("quando scade?" con più contratti dello stesso fornitore) | Intervista "quale contratto?" invece di un abstain; con modello: interpretazioni proposte | Domande di chiarimento al posto dell'abstain | `Chat__Interview__UseModelStage` = false |
| Rispondere al feedback card | Riga `feature_request` salvata (poi, se `Feedback__GitHub__Enabled`, issue su GitHub) | Conferma mostrata | (flag già esistente, non toccato da questa PR) |

### F5 — Estrazione documenti

| Cosa fare | Cosa osservare | Risultato atteso | Se si comporta male |
|---|---|---|---|
| Caricare un contratto PDF di prova | Log del Worker: `Span verification (Gate) for stage X: found=.. otherPage=.. notFound=.. noSpan=..`; metrica `raffa.extraction.span_verifications` | I fatti con span non trovato vanno in revisione (review_required) con valore intatto | `Documents__Extraction__SpanVerification` = Shadow (poi Off) |
| Stesso documento | Log: `Cross-stage validation (Gate): <regola>=<stato>` e `Cross-stage violation ... on <campi>`; metriche `raffa.extraction.cross_stage_checks` / `cross_stage_violations` | Campi di date incoerenti (inizio > fine, scadenza, TCV vs spesa annua) in revisione con l'id regola | `Documents__Extraction__CrossStageValidation` = Shadow |
| Stesso documento, osservare i tempi | Log di start/end per stage; metrica `raffa.extraction.tpm_waits` > 0 se il pacing interviene; stage liste che partono dopo gli scalari; `runId=` in ogni riga | Max 4 chiamate in volo, ritentativo immediato su errore transitorio; documento lungo = minuti (60K TPM per processo) | `Documents__Extraction__MaxConcurrentStages` = 0, `TokensPerMinuteLimit` = 0, `UseContextDependencies` = false |
| Controllare la versione di pipeline sulla riga di audit della run | `staged-v2-docfirst` + versione del prompt | Mai checkpoint misti tra layout diversi; ri-estrarre un documento già fatto produce un nuovo hash | `Documents__Extraction__DocumentFirstPrompt` = false |
| Documento con data fine / preavviso / canone annuo | Log: `Second opinion (shadow) for run <runId>: endDate=<esito>, ...`; metrica `raffa.extraction.second_opinion` | Solo contatori e log (nessun valore cambia); costo = una chiamata extra per stage | `Documents__Extraction__SecondOpinionShadow` = false |

### F6 — Estrazione preventivi

| Cosa fare | Cosa osservare | Risultato atteso | Se si comporta male |
|---|---|---|---|
| Caricare un preventivo con intestazione incompleta (senza fornitore/valuta) | Il job passa dalla coda durevole (`ServiceBus__*`) e viene eseguito dal Worker; secondo passaggio modello per l'intestazione | Fornitore, data, valuta, area geografica compilati solo se la confidenza ≥ 0,7, altrimenti vuoti da completare | `QuoteExtraction__HeaderStageEnabled` = false |
| Ri-elaborare lo stesso preventivo (reprocess) | Stato job `Uploaded/Processing` per un po', poi completato; nessun duplicato | Stesso risultato, nuovo job | (coda: nessun flag di questa PR) |

### F7 — Mercato

| Cosa fare | Cosa osservare | Risultato atteso | Se si comporta male |
|---|---|---|---|
| Lanciare il workflow `seed-market-intelligence` (target `dev`) | Log del job: step "Resolve the Foundry gateway (dev)" ("Foundry gateway env copied: ...") oppure il `::warning::` che spiega perché la curation è spenta; riga `Ingested feed '...': N inserted, M updated, K unchanged.`; seconda esecuzione `0 inserted, 0 updated` | Corpus nuovo costruito accanto al vivo e scambiato (BlueGreen); record scartati dalla curation elencati; la seconda run non cambia nulla | Nel workflow, `Market__Ingestion__Curation__Enabled` / `BlueGreen` / `CheckpointPerBatch` -> `'false'` per dev |
| Domanda di mercato che cita un fornitore ("prezzi di mercato per HubSpot") | Nota di mercato del fornitore citato in cima; non più di 2 note per fornitore | Il fornitore nominato è davanti anche se il vettore non lo trova primo; nessun fornitore domina le note | `Market__Retrieval__Hybrid` / `Market__Retrieval__Diversify` = false (senza golden set F7-D08 il ranking non è misurato) |
| Dopo l'ingest, interrogare di nuovo | Le note usano la versione attiva del corpus; l'ultima versione ritirata è conservata per il rollback | Nessun risultato misto fra versioni | `Market__Ingestion__BlueGreen` = false per tornare all'aggiornamento in-place |
