# Pannello admin: Playground degli agenti

Stato: spec approvata 2026-11-19. PR 1 (fondamenta + Ask con trace), PR 2 (varianti e confronto) e PR 3 (altri workflow) e PR 4 (giudice LLM) e PR 5 (export) implementate: la consegna è completa; vedi "Stato della consegna" in fondo.

## Perché

Oggi migliorare un agente di Raffa significa: modificare un `vN.md`, aggiornare versione e hash, lanciare il
golden set in CI e leggere i log `ai.*`. Nessuno può **provare** un'idea su una domanda reale, vedere cosa ha
fatto l'agente passo per passo e confrontarla con la versione attuale prima di aprire una PR.

Il Playground è lo strumento del **Super Admin** (allowlist `PlatformAdmin:ObjectIds`, nessun altro lo vede)
per giocare con gli agenti e affinarne l'efficacia: domanda → trace completa → modifica prompt/modello/tool
→ rilancio → confronto affiancato → voto del giudice → PR verso il repo.

## Decisioni prese (intervista con l'owner, 2026-11-19)

| Tema | Decisione |
|------|-----------|
| Chi | Solo Super Admin, dentro il Pannello admin (`/admin/playground`). 404 per tutti gli altri, come `/api/admin/*`. |
| Dati | Workspace sintetici (fixture `empty`, `seeded`, `needs-review` del golden set) **più** una sandbox dove il Super Admin carica i propri PDF di test. Mai workspace clienti. |
| Sandbox | **Una, condivisa** fra tutti i Super Admin. Esclusa da metriche, elenchi workspace e utenti del pannello. |
| Workflow v1 | Ask Raffa (orchestratore agentico), negotiation council, estrazione documenti, web research e drafting. |
| Modifiche | **Override effimeri** (valgono solo nelle run del playground). Una variante buona diventa una **PR automatica su GitHub**: la produzione cambia solo passando dal repo e dalla review umana. |
| Modelli | Solo i deployment Foundry già configurati nell'ambiente, scelti per ruolo. |
| Valutazione | **Confronto affiancato** di 2–3 varianti + **LLM-as-judge** con rubrica partendo da `ANNOTATION_RUBRIC.md`, modificabile nel playground. |
| Trace | **Completa**: prompt effettivo, messaggi, tool call con argomenti e risultati grezzi, output grezzo, guard e rigenerazioni, latenza, token, costo. |
| Storico | **Persistente**: varianti, run, trace e voti si salvano e si riaprono. |
| Costi | Nessun limite; il costo stimato di ogni turno è mostrato. |
| Ambienti | Rilasciato ovunque (dev, demo e futuri). Nessun feature flag di ambiente: l'accesso è l'allowlist. |

## Concetti

- **Variante**: un insieme nominato di override sopra la baseline del codice. Campi: per agente, il testo del
  prompt (partendo dalla versione registrata); per ruolo (`agent`, `answer`, `analyst`, `extract`, …) il
  deployment e i knob di `AiModelSelection` (`ReasoningEffort`, `MaxCompletionTokens`, `Temperature` sempre
  ≤ 0.2 come da ADR-024); l'elenco dei tool Ask abilitati; i flag `AiFlows` del turno (es.
  `Chat:AskTurn:BudgetBeforeAgents`, `Documents:Extraction:SpanVerification`). La **baseline** è la variante
  vuota: ciò che gira in produzione in quell'ambiente.
- **Run**: un'esecuzione di un workflow su un input (domanda + workspace, o documento) con una variante.
  Salva output, trace, costo e durata. Un **confronto** è la stessa entrata eseguita su 2–3 varianti.
- **Rubrica**: criteri pesati usati dal giudice. Versionata come una variante (si modifica, non si sovrascrive).

## Architettura

### 1. Scope del playground (override sicuri)

Un `PlaygroundScope` ambientale (`AsyncLocal`, come `RunContext`) porta la variante dentro la run. Lo
consultano solo tre punti:

1. **Prompt**: `AgentPromptResource` / `AgentDefinition` e `AgenticAskTurn.BuildSystemPrompt` usano il testo
   della variante se lo scope ne ha uno per quell'agente, altrimenti il prompt registrato. Il drift test
   hash/versione resta intatto: l'override non tocca i file.
2. **Modello**: il punto in cui il gateway risolve `AiModelSelection` per ruolo prende quella della variante,
   validata contro i deployment configurati (`AiGateway:Models:*`): non si può indicare un deployment
   arbitrario.
3. **Tool e flag**: il catalogo `IAskTool` è filtrato; le options dei flow leggono l'override del flag.

Difesa in profondità: lo scope si apre **solo** dagli endpoint `/api/admin/playground/*` dopo
`PlatformAdminGuard`, e viene **ignorato** (con log di warning) se il tenant della run non è una sandbox.
Un test di architettura vieta di aprirlo da altri assembly.

### 2. Cattura della trace

- Un decoratore `RecordingAiGateway` (sopra `LoggingAiGateway`), attivo solo con lo scope aperto, registra
  per ogni chiamata: ruolo, deployment, messaggi inviati, risposta grezza, token in/out, latenza, retry,
  `RunContext` (turn/run/step).
- `AgenticAskTurn` e `StepRunner` aggiungono eventi strutturati: tool call (nome, argomenti, pack restituito,
  chiavi di citazione), guard scattati, rigenerazione, fallback, abstain.
- Gli eventi formano una timeline salvata in `playground_run.trace` (JSONB) e trasmessa in streaming (SSE)
  alla UI mentre la run procede.
- Costo: token × prezzo per 1k del deployment, da una nuova sezione di config `Playground:Pricing:<deployment>`
  (input/output). Senza prezzo il costo è "n/d", non zero.

### 3. Workspace sandbox

- Colonna `is_sandbox` su `platform_workspace`, più `sandbox_kind`: `fixture-empty`, `fixture-seeded`,
  `fixture-needs-review`, `uploads`. Quattro tenant creati al primo accesso al playground.
- I tre fixture si popolano dai dati di `TenantFixtureCatalog` (gli stessi del golden set), con un seeder
  idempotente invocabile dal playground ("ripristina fixture").
- `uploads` è la sandbox condivisa: l'upload passa dalla pipeline normale (Worker: classify, OCR, estrazione
  baseline), così il documento diventa un contratto interrogabile da Ask.
- Il Super Admin **non diventa membro** di questi workspace. Gli endpoint del playground impostano
  `app.tenant_id` sul tenant sandbox dopo aver verificato `is_sandbox = true`: RLS resta il backstop e nessuna
  eccezione viene aggiunta. Overview, Workspaces e Users del pannello escludono le sandbox.

### 4. Workflow supportati

| Workflow | Input | Cosa si può variare | Output mostrato |
|----------|-------|---------------------|-----------------|
| Ask Raffa | conversazione multi-turno su un workspace sandbox | prompt `agent`, modello `agent`/`answer`, tool, flag | `CopilotReply` renderizzata come in `/ask` + trace |
| Negotiation council | contratto sandbox + obiettivo | prompt dei 3 analisti e dello stratega, modello `analyst` | leve, piani, step saltati/falliti |
| Estrazione | documento della sandbox `uploads` | prompt/modello per stadio, `SpanVerification`, `CrossStageValidation`, `DocumentFirstPrompt` | campi estratti per stadio con span ed esito verifica, diff contro i valori correnti del contratto |
| Web research | domanda + consenso simulato | prompt `research`, purpose | query uscente, fonti, cifre verificate/rimosse |
| Drafting | contratto + tipo di bozza | prompt `draft`, tono | bozza, check di tono e tempi |

L'estrazione nel playground **non scrive** sui fatti del contratto: rilancia gli stadi in-process sul testo
OCR già salvato e conserva il risultato nella run.

### 5. LLM-as-judge

- Nuovo agente registrato `playground/judge` (`Prompts/playground/judge/v1.md`, schema strict) che riceve: la
  domanda, il contesto (pack o documento), una o più risposte anonimizzate (A/B/C in ordine casuale) e la
  rubrica. Restituisce per ogni risposta un punteggio per criterio (1–5) con motivazione, e una preferenza
  complessiva.
- Rubrica di partenza derivata da `ANNOTATION_RUBRIC.md` §1: correttezza del tipo (answer/abstain/redirect),
  grounding (cifre e citazioni solo dai tool), utilità per un buyer, tono, lingua. Una rubrica per workflow
  (estrazione: accuratezza dei campi; drafting: tono e completezza), tutte modificabili.
- Il modello del giudice si sceglie fra i deployment configurati; di default il più capace disponibile.
- Il voto è un segnale, non una verità: la UI mostra sempre le risposte accanto ai punteggi.

### 6. Export verso il repo (PR automatica)

- Pulsante "Proponi come nuova versione" su una variante. Il backend genera, per ogni agente con prompt
  modificato: il nuovo `Prompts/<flow>/<agent>/vN+1.md`, la versione e l'hash registrato aggiornati
  (`AgentDefinition`; per l'orchestratore anche `AgentPrompt.Version` e `SystemPrompt`), e apre una PR su
  `main` con nel corpo il diff del prompt, i confronti e i voti del giudice che la motivano.
- Le modifiche al modello o ai flag non diventano codice: la PR le elenca come "da applicare in
  `infra/environments/*/variables.tf`".
- I drift test esistenti in CI verificano che file, versione e hash siano coerenti; merge solo umano.
- Serve una credenziale GitHub lato backend (GitHub App con `contents:write` e `pull_requests:write` solo su
  questo repo), segreto in Key Vault come `AiGateway__Jev__ApiKey`. **Azione owner**, vedi punti aperti.

## Modello dati (tabelle di piattaforma, senza RLS: non contengono dati clienti)

- `playground_variant` (id, nome, base_ref = commit/versioni dei prompt di partenza, overrides JSONB,
  creato_da oid, creato_il, archiviata).
- `playground_rubric` (id, workflow, nome, versione, criteri JSONB, creato_da, creato_il).
- `playground_run` (id, workflow, variant_id, sandbox_tenant_id, input JSONB, output JSONB, trace JSONB,
  tokens_in, tokens_out, costo_stimato, durata_ms, stato, comparison_id, creato_da, creato_il).
- `playground_judgement` (id, comparison_id o run_id, rubric_id, judge_deployment, risultato JSONB,
  creato_il).
- `playground_export` (id, variant_id, pr_url, stato, creato_da, creato_il).

Le sandbox contengono solo dati sintetici o PDF di test caricati dal Super Admin, quindi la trace può
conservare payload grezzi.

## API (`/api/admin/playground/*`, tutte dietro `PlatformAdminGuard`)

- `GET  /sandboxes` · `POST /sandboxes/{kind}/reset` · upload via l'endpoint documenti con tenant sandbox
- `GET  /baseline` — agenti, versioni e testi dei prompt registrati, deployment per ruolo, tool, flag
- `GET|POST|PUT /variants` · `GET|POST /rubrics`
- `POST /runs` (workflow, variante, input; risposta in SSE con eventi della trace) · `GET /runs` · `GET /runs/{id}`
- `POST /comparisons` (input + 2–3 varianti, in parallelo) · `POST /comparisons/{id}/judge`
- `POST /variants/{id}/export` → URL della PR

## UI (`/admin/playground`, web)

Tre colonne, come uno strumento di laboratorio:

1. **Sinistra — setup**: workflow, workspace sandbox, varianti selezionate (fino a 3), storico run.
2. **Centro — conversazione/risultato**: una colonna per variante, la stessa domanda inviata a tutte;
   risposte renderizzate con i componenti di `/ask` (citazioni, azioni, artefatti).
3. **Destra — ispettore**: la trace del turno selezionato come timeline espandibile (step → chiamata modello
   → tool → guard), con token, costo e durata per step; tab "Prompt effettivo" e "JSON grezzo".

Editor variante in un pannello: diff del prompt contro la baseline, selettori di modello/knob per ruolo,
checkbox per tool e flag. Voti del giudice sopra ogni colonna. Testi in italiano e inglese tramite i
cataloghi i18n esistenti.

## Sicurezza

- Accesso: allowlist esistente; nessun ruolo di workspace dà accesso.
- Gli override non escono dallo scope del playground (test di architettura + guard sul tenant sandbox).
- Nessun workspace cliente è selezionabile: gli endpoint accettano solo tenant `is_sandbox`.
- Audit: ogni run, export e reset scrive una riga `platform.playground.*` con l'oid del Super Admin.
- Web research dal playground resta soggetta a `WebQuerySanitizer`: dalla sandbox escono solo le parole della
  query, come in produzione.

## Piano di consegna (PR incrementali)

1. **Fondamenta + Ask con trace**: tabelle, sandbox e seeder fixture, `PlaygroundScope`,
   `RecordingAiGateway`, eventi di `AgenticAskTurn`, endpoint run in SSE, UI con chat e ispettore (solo
   baseline).
2. **Varianti e confronto affiancato**: editor variante (prompt, modello, tool, flag), override nei tre punti,
   confronto fino a 3 colonne, storico.
3. **Altri workflow**: council, estrazione (rilancio stadi su OCR salvato, diff dei campi), web research,
   drafting; upload nella sandbox `uploads`.
4. **LLM-as-judge**: agente `playground/judge`, rubriche per workflow modificabili, voti nella UI.
5. **Export PR**: generazione file/versione/hash, GitHub App, PR con report.

Ogni PR passa i test esistenti più: guard 404 per non-admin, override ignorato fuori dalla sandbox, drift test
dei prompt invariato, trace senza dati di tenant non sandbox.

## Punti aperti

1. **Credenziale GitHub** per l'export (PR 5): creare una GitHub App installata solo su questo repo e salvarne
   chiave/ID in Key Vault per ambiente. Fino ad allora il pulsante scarica il `.patch`.
2. **Prezzi per deployment** (`Playground:Pricing`): servono i prezzi per 1k token dei deployment di dev e demo.
3. **Concorrenza col Worker**: un confronto a 3 varianti sull'estrazione triplica le chiamate; il playground usa
   il proprio pacing (`TokensPerMinuteLimit`) per non sottrarre quota al Worker, da tarare in dev.
4. **Conservazione**: per ora le run si tengono indefinitamente; valutare un TTL se le trace diventano grandi.

## Stato della consegna

**PR 1 (fatta):** tabelle `playground_run` e flag `platform_workspace.is_sandbox` (migration
`AddPlaygroundTables`); i tre workspace sandbox sintetici creati al primo uso (`PlaygroundSandboxSeeder`, dati
allineati ai fixture del golden set); `PlaygroundTrace` + `TraceRecordingAiGateway` (ogni chiamata al modello con
richiesta esatta e risposta grezza, token, durata) ed eventi di tool e guard in `AgenticAskTurn`;
`/api/admin/playground/{sandboxes,runs}` dietro `PlatformAdminGuard`; costo stimato da `Playground:Pricing`;
tab Playground nel Pannello admin con conversazione multi-turno e ispettore della trace.

**PR 2 (fatta):** tabella `playground_variant` e colonne `variant_id`/`variant_name`/`comparison_id` su
`playground_run` (migration `AddPlaygroundVariants`); `PlaygroundScope` con i tre punti di override: testo del
prompt dell'orchestratore, deployment e knob di ogni ruolo (`agent`, `answer`, `analyst`, `extract`, `classify`,
`research`) nei client Foundry, tool disattivati e limite di tool call in `AgenticAskTurn`;
`PlaygroundCatalog` che rifiuta con il motivo una variante che nomina un deployment, un tool o un valore che
l'ambiente non ha; `GET /baseline`, CRUD `/variants` (la cancellazione archivia), `POST /runs` con `variantId` e
`comparisonId`; UI con tre sezioni (Compare, Variants, History): fino a 3 corsie affiancate che ricevono la
stessa domanda in parallelo, editor di variante con diff del prompt contro la produzione, storico con riapertura
della trace. Ogni run conserva la copia degli override usati, quindi modificare o archiviare una variante non
cambia le run passate.

**PR 3 (fatta):** `PlaygroundWorkflowRunner` (pubblico, in `Raffa.AiFlows/Playground`) porta nel playground gli
altri workflow con gli stessi oggetti di produzione: **council** (pack di leve e controllo dati di mercato sul
contratto della sandbox, poi i tre agenti), **drafting** (stesso pack, poi lo scrittore con template, controllo di
tono), **web research** (il composer di ricerca, con la stessa sanificazione della query del consenso di produzione:
dalla sandbox esce solo la query pulita, mostrata nel risultato; non si applicano opt-in del workspace e budget
giornaliero, perché qui il consenso è dell'admin) ed **estrazione** (testo incollato, oppure un file .txt/.md/.pdf/
immagine letto in memoria con l'OCR del gateway: gli stadi girano e ognuno restituisce il payload grezzo del
modello). `POST /runs` prende `workflow` e `input`; ogni workflow ha la stessa trace, variante, costo e storico.
I prompt sostituibili da una variante sono ora tutti: orchestratore, prompt di sistema dell'estrazione (`extract`) e
ogni agente registrato (council, capability gaps, drafting, contract situation, market ingestion...).
`GET /baseline` li elenca con flow, versione e testo di produzione. La UI ha il selettore di workflow, i form di
input e i renderer dei risultati (leve del council, email, fonti, payload per stadio).

**PR 4 (fatta):** tabelle `playground_rubric` e `playground_judgement` (migration `AddPlaygroundJudging`);
rubriche predefinite per ogni workflow (`PlaygroundRubrics`, derivate da `ANNOTATION_RUBRIC.md`, in codice quindi
non modificabili: si copiano) e rubriche custom modificabili; `PlaygroundJudge` (prompt `playground-judge-v1`, schema
strict): vede le risposte **anonime** (A, B, C in ordine casuale) con l'evidenza da cui sono nate (risultati dei tool
per Ask, pack per council e drafting, fonti per la ricerca, testo del documento per l'estrazione) e dà un punteggio
1-5 per criterio con motivazione; con 2 passaggi la seconda lettura inverte l'ordine, i punteggi si mediano e un
verdetto che cambia con l'ordine è segnalato come inaffidabile; la preferenza si calcola dai punteggi pesati (con
margine di pareggio), non dalla parola del giudice. Il modello del giudice si sceglie fra i deployment configurati
(applicato solo a quella chiamata). Ogni giudizio salva uno snapshot della rubrica, la trace delle chiamate del
giudice e il costo. API: `GET|POST|PUT|DELETE /rubrics`, `POST /judgements` (`comparisonId` o 1-3 `runIds`),
`GET /judgements?comparisonId=|runId=`. UI: pannello *Judge* sotto le corsie, sezione *Rubrics*, giudizi di una run
nella *History*.

**PR 5 (fatta):** tabella `playground_export` (migration `AddPlaygroundExports`); da una variante, "Propose…" nella
sezione Variants produce una **proposta** in due forme. (1) Una **PR draft** su un branch nuovo
(`playground/<variante>-<data>`), mai sul branch base e mai fusa da Raffa: contiene `docs/playground/proposals/<data>-<variante>/`
con il report (cosa cambia, prove e voti del giudice che la motivano, con l'avviso se un voto cambiava con l'ordine delle
risposte), il testo proposto di ogni prompt e il suo diff contro la produzione; per i modelli e i limiti l'elenco delle
variabili `AiGateway__Models__<Ruolo>__...` / `Chat__AskTurn__AgenticMaxToolCalls` da impostare in
`infra/environments/<env>/variables.tf` (non applicate), per i tool disattivati la nota che serve una modifica di codice. Per il
**prompt dell'orchestratore** la PR fa anche la modifica di codice: nuovo `Prompts/agent/vN+1.md`, `AgentPrompt.cs` (versione,
costante, note) e il test di drift puntato al file nuovo; la trasformazione è stata verificata ricompilando e facendo girare il
vero test di drift sui file modificati. (2) Un **bundle** `.zip` con gli stessi file ai percorsi del repo e un `APPLY.md`,
quando GitHub non è collegato. La PR usa la connessione GitHub già presente per il feedback (`Feedback__GitHub__Repository` e
`Feedback__GitHub__Token`, spenta negli ambienti esposti): **al token servono i permessi Contents e Pull requests in lettura e
scrittura**, un token solo-Issues riceve 403 e l'errore lo dice. Ogni export (anche fallito) è registrato.

Scostamenti dalla spec, da chiudere nelle PR successive:
- La risposta di `POST /runs` è un JSON unico a fine turno, **non ancora SSE**: la trace compare tutta insieme.
- Gli override coprono prompt (tutti gli agenti registrati, orchestratore ed estrazione), modelli per ruolo, tool e
  limite di tool call. I flag di `AiFlows` (es. `SpanVerification`, `Chat:AskTurn:Agentic`) restano quelli
  dell'ambiente: non sono ancora sostituibili. Non sono sostituibili nemmeno il prompt della risposta Ask (`answer`)
  e quello della ricerca web, che non sono agenti registrati.
- **Estrazione:** non passa dalla sandbox `uploads` né dalla pipeline documenti (storage, Worker, run store): il
  playground legge testo o file in memoria e rilancia gli stadi. Mancano quindi verifica degli span, validazione fra
  stadi e diff contro i valori del contratto, che dipendono dal run store. Va aggiunto se serve confrontare con un
  contratto già validato.
- Le rubriche si modificano sul posto (non c'è una cronologia di versioni): la riproducibilità è garantita dallo
  snapshot salvato in ogni giudizio. Il prompt del giudice non è fra quelli sostituibili da una variante.
- I prompt degli altri agenti (council, drafting, ricerca, estrazione...) non sono modificati nel codice dalla PR: il testo e
  il diff sono nella proposta, con il nuovo hash da registrare. Ogni flow tiene versione, file e hash in modo diverso e una
  riscrittura automatica generale sarebbe fragile; si applicano a mano (o si estende `OrchestratorPromptEdit` agente per agente).
- Le righe di audit `platform.playground.*` previste in Sicurezza non sono scritte: run, giudizi ed export sono comunque
  registrati nelle loro tabelle con l'autore e la data.
- I prezzi in `appsettings.json` coprono `gpt-5.4` e `gpt-5.4-nano` (listino Azure, North Europe, DataZone
  Standard). `gpt-5.6-terra` e `gpt-6-astra` non risultano nel listino pubblico per North Europe: finché non
  si configura un prezzo il loro costo è "n/d".
