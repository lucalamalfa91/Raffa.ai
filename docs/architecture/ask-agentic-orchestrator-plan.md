# Ask: dal router a intenti all'orchestratore agentico

Stato: piano, 2026-11-05. Flag `Chat:AskTurn:Agentic` (off di default).

## Perché

Oggi `AskCopilotService` è un router: `DomainGate` (regex) → `TurnUnderstanding` (una chiamata al modello
vincolata a un intent chiuso) → `ArtifactCommandResolver` (regex) → `IIntentHandler` per intent → pack →
`AnswerComposer`. Il modello non decide mai cosa fare: classifica, poi scrive. Ogni frase che il C# non
prevede finisce sull'handler sbagliato (incidente Coupa del 2026-11-05). Gli "agenti" esistenti
(`AskAgentFlow`: market-data-check → market-researcher → council; `ArtifactComposer`;
`CapabilityInvestigatorAgent`; `ContractSituationAgent`) sono specialisti senza strumenti, in ordine fisso.

Obiettivo: un modello orchestratore legge la conversazione e **sceglie quali strumenti di Raffa chiamare**,
in che ordine, poi risponde. Gli specialisti e i pack builder restano e diventano strumenti. Le garanzie
(solo dati del tenant, cifre solo dai tool, citazioni verificate, audit per chiamata) restano come vincoli
sul prompt e sugli output dei tool, non come regex sull'input.

## Vincoli dal codice

- **ADR-024 / R-AI-03**: il ruolo `answer` non ha `tools` sulla richiesta (test `FoundryAnswerClientTests`
  sul JSON grezzo). Il divieto riguarda grounding/web sul deployment; non si tocca `ChatCompletionRequest`.
  L'orchestratore è un **nuovo ruolo** `agent` con il proprio wire type e il proprio deployment
  (`AiGateway:Models:Agent`, fallback sul deployment `Answer` come fa `Analyst`). Nota di emendamento
  all'ADR nel PR.
- Il contratto di risposta (`CopilotReply`: kind, markdown, citations, actions, payload, interview,
  conversationState) non cambia: il web client resta com'è.
- Gli id (contratti, documenti, artefatti) non arrivano mai dal testo del modello: i tool risolvono per
  nome fornitore dentro il portafoglio del tenant e restituiscono chiavi di citazione, come già fanno i pack.

## Cosa implemento

### PR 1 — Gateway: ruolo `agent` (Raffa.AiGateway)

- `IAiGateway.ConverseAsync(AiConversationRequest) → AiConversationResult`: messaggi (system, history,
  tool results), definizioni tool (nome, descrizione, JSON schema), schema dell'output finale. Risultato:
  o una lista di `ToolCall(id, name, argumentsJson)` o il JSON finale.
- `FoundryAgentClient` su Chat Completions con `tools` / `tool_choice: auto` / `parallel_tool_calls`;
  wire records nuovi (`AgentChatCompletionRequest`), strict JSON schema sul turno finale.
- `AiGatewayModelOptions.Agent` (opzionale → `Answer`).
- `FixtureAiGateway.ConverseAsync`: implementazione scriptata deterministica (sequenza di tool call
  decisa dal fixture) per golden set e test API senza rete.
- `LoggingAiGateway`: una riga di audit `ai.agent.turn` per chiamata (token, tool chiamati — nomi e
  conteggi, mai testo).

### PR 2 — Strumenti (Raffa.AiFlows/Ask/Agentic/Tools)

`IAskTool { Name, Description, ParametersSchema, ExecuteAsync(argsJson, AskToolContext) → AskToolResult
{ ResultJson, PackItems, Citations, UiActions?, Artifact?, Interview? } }`. Ogni tool è un adattatore
sottile su codice che esiste già:

| Tool | Sopra | Note |
| --- | --- | --- |
| `list_portfolio` | `PortfolioQueryService` + `PortfolioStrategyPackBuilder` | filtri: scadenza, spesa, rischio |
| `get_contract` | `ContractContextReader.GetContract360Async` + `StructuredFactPackBuilder` | per fornitore (fuzzy sui nomi del tenant) o "questo contratto" (scope) |
| `search_clauses` | `ClausePackBuilder` | tema (recesso, SLA, responsabilità…) |
| `get_renewal_notice` | `NoticeFacts`, `RenewalStrategyPackBuilder`, `ContractSituationAgent` | date, preavviso, situazione |
| `get_market_benchmark` | `MarketDataCheck` + `MarketResearcher.QueryMarketRagAsync` + `MarketComparePackBuilder` | sostituisce il rifiuto `NoBenchmark`: se il pack è vuoto lo dice |
| `run_negotiation_council` | `NegotiationCouncil.RunAsync` | leve e findings; costoso, il prompt dice quando |
| `get_savings` | `SavingsPackBuilder`, obiettivo da `SavingsGoalParser` | |
| `get_document_status` | `DocumentStatusPack` | |
| `compose_artifact` | `ArtifactComposer.ComposeAsync` | kind, supplier, usesMarket, usesPortfolio; restituisce payload + `OpenArtifact` |
| `update_artifact` / `open_artifact` | composer / `CurrentArtifactHint` | solo l'artefatto persistito della conversazione |
| `navigate` | `CapabilityCatalog` | route reali, mai inventate |
| `web_research` | `WebResearchFlow` | senza consenso restituisce l'interview di consenso (ADR-030) |
| `draft_email` | `CapabilityGapTurnHandler` drafting | |

Tenant scope: `AskToolContext` porta `TenantId`, `ScopeContractId`, `ConversationState`, `CurrentArtifact`;
nessun tool accetta un GUID dal modello.

### PR 3 — Orchestratore (Raffa.AiFlows/Ask/Agentic/AgenticAskTurn)

- System prompt versionato (`Prompts/agent/v1.md`): consulente procurement di Raffa; leggi: usa solo i
  tool, ogni cifra e data viene da un risultato di tool, cita le chiavi, rispondi nella lingua dell'utente,
  chiedi chiarimenti solo se nessuna lettura è sensata, fuori dominio/legale → risposta breve senza tool,
  di' cosa hai fatto quando crei o aggiorni qualcosa.
- Input: history (stesso `BuildPromptHistory`), `ConversationState`, memoria preferenze, scope contratto,
  artefatto corrente.
- Loop: max 8 tool call per turno, deadline di fase (`TurnDeadline` esistente), tool in parallelo quando il
  modello li chiede insieme (`StepRunner.FanOutAsync` come modello). Ogni tool è uno step `RunContext`:
  l'audit del turno scrive `stepsRun=… steps=… failures=…` come oggi.
- Output finale strutturato: `{ answerMarkdown, citationKeys, actionKeys, followUps, abstainReason? }`
  + ciò che i tool hanno prodotto (artefatto, uiActions, interview). Passa dai guard esistenti
  (`CitationGuard`, `NumericGuard`, `Guards.RegenerateOnce` → downgrade ad abstain), quindi
  `ConversationStateReducer.Reduce` (il "plan" diventa il tool principale usato) e `AskTurnAudit`.
- `AskCopilotService.AskAsync`: `if (options.Agentic && hints non è una risoluzione di interview) →
  AgenticAskTurn` altrimenti il percorso attuale. Kill switch a caldo via `IOptionsMonitor`.

### PR 4 — Verifica e rollout

- Test unitari per ogni tool (dati in memoria) e per il loop (fixture scriptata: sequenza, limite, deadline,
  tool fallito → il turno continua).
- Test API end-to-end sulla trascrizione Coupa: riassunto → "crea un report … con i dati di mercato"
  (→ `get_market_benchmark` + `compose_artifact` document/market) → "perché non hai il benchmark?"
  (risposta dal pack, non rifiuto) → "crea un kpi" (Coupa) → "ma il contratto era coupa" (ricostruisce)
  → "e Oracle rispetto a questo?" (confronto).
- Golden set in modalità agentica (`Trait Agentic`): stessi 40+ casi, stesse invarianti (zero guard,
  zero chiamate su off-domain, citazioni reali, audit con turnId).
- Rollout: flag off → on in dev → confronto golden set e trascrizioni → on in demo/prod. Rollback: flag.

## Cosa sparisce (quando il flag è on)

`DomainGate`, `TurnUnderstanding`, `ArtifactCommandResolver`, `IntentPlanner`, `IIntentHandler`,
`IntentPolicy` come decisori. Restano come codice del percorso legacy finché il flag non diventa default,
poi si rimuovono.

## Stato

- PR 1 (ruolo `agent` nel gateway): fatto — `IAiGateway.ConverseAsync`, `FoundryAgentClient`,
  `AiGatewayModelOptions.Agent` (fallback `Answer`: dev `gpt-5.4-nano-dev`, demo `gpt-6-astra-demo`),
  `IFixtureAgentScript` per il fixture, riga di audit `conversed` per step. ADR-004 e ADR-024 emendati.

- PR 2+3 (strumenti + orchestratore): fatto — `Raffa.AiFlows.Ask.Agentic`: `IAskTool` e 12 tool
  (`list_portfolio`, `get_contract`, `search_clauses`, `get_renewal_strategy`, `get_market_benchmark`,
  `get_savings`, `run_negotiation_council`, `get_document_status`, `compose_artifact`, `open_artifact`,
  `navigate`, `web_research`), `AgenticAskTurn` (loop, max 8 call, deadline, guard + rigenerazione, audit
  `agentic=true stepsRun=…`), prompt `Prompts/agent/v1.md`, flag `Chat:AskTurn:Agentic` (off), fallback al
  router se il ruolo non risponde. `AskAgentFixtureScript` per i test senza rete. `draft_email` rinviato: la
  persona scrive già le mail con i fatti dei tool.

- PR 4 (verifica e rollout): fatto — `AskAgenticConversationTests` riproduce la trascrizione Coupa sul
  fixture host (riassunto → report con mercato su Coupa → KPI su Coupa, mai Zscaler → domanda di mercato
  risposta, mai rifiutata; nessun follow-up "non posso generare un report" dopo averlo generato); il golden
  set gira anche con `AiEval__Agentic=true` (job CI "AI eval (agentic mode)"): invarianti obbligatorie
  (una riga di audit con turnId e stepsRun, zero guard, nessun chrome, route reali, zero tool su
  greeting/off-domain/legal), aspettative per caso advisory nel report `reports/last-run-agentic.md`.
  `Chat__AskTurn__Agentic=true` in `infra/environments/dev` (demo resta sul router).

## Altri usi dell'AI trovati nell'app (da aggiungere, in ordine di valore)

Tutti riusano il ruolo `agent` e gli stessi tool, quindi costano poco una volta fatto l'orchestratore.

1. **Brief di rinnovo proattivo** (Renewals): per ogni contratto con preavviso entro 90 giorni, un
   brief scritto dall'agente (situazione, leve del council, benchmark, azione consigliata), rigenerato
   quando cambiano i fatti. Oggi la schermata mostra dati e il `contract-situation-assessor` scrive
   solo la headline.
2. **"Cosa è cambiato"** (Documents): all'upload di una nuova versione di un contratto già validato,
   un confronto in linguaggio naturale clausola per clausola (prezzo, preavviso, SLA, responsabilità)
   con le citazioni. Oggi l'utente confronta a mano.
3. **Spiegazione del rischio in Contract 360**: su una clausola segnalata, "perché è un rischio e cosa
   chiedere al fornitore", dal pack delle clausole + market practice. Oggi c'è solo il flag.
4. **Quote check narrativo** (Quotes): oltre ai numeri dell'estrazione, una raccomandazione scritta
   (accetta / negozia su X / rifiuta) ancorata a contratto corrente e benchmark.
5. **Ricerca in linguaggio naturale nei documenti** (Documents): "trova tutti i contratti con
   auto-rinnovo e preavviso < 60 giorni" → lista con citazioni, stessa `search_clauses` + `list_portfolio`.
6. **Triage dei feedback** (ADR-030): il `capability-investigator` già classifica i gap; l'agente può
   proporre il testo dell'issue e raggruppare i duplicati prima della pubblicazione su GitHub.
7. **Titoli e riassunti delle conversazioni**: titolo generato e un riassunto di 2 righe nella lista
   "Recenti", oggi è la prima domanda troncata.
