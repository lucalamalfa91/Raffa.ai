# Benchmark Jev: prima (Foundry) e dopo (Jev)

Questo documento spiega come misurare **quanto Jev migliora il processo** con un confronto A/B su
dati etichettati, da eseguire a mano in dev con una chiave Jev reale. Il benchmark e **separato**
dalle suite normali: non gira in CI, non chiama nulla senza un opt-in esplicito e non richiede la
chiave per compilare o per i test unitari.

Codice: `backend/tests/Raffa.JevBenchmark.Tests`. Workflow manuale: `.github/workflows/jev-benchmark.yml`.

## 1. Cosa confronta

| Braccio | Configurazione | Cosa succede |
|---|---|---|
| **A (baseline)** | `AiGateway:Jev:Enabled=false` | `classify` e servito solo da Foundry. Il capability investigator decide il verdetto con la sola chiamata Foundry `analyst`. Il braccio A non puo raggiungere Jev: il suo trasporto HTTP verso Jev lancia un'eccezione se viene toccato. |
| **B (con Jev)** | `AiGateway:Jev:Enabled=true` (forzato dall'harness) | `classify` passa da `JevAiGateway`: prima Jev, poi Foundry se Jev fallisce o risponde sotto `ClassifyMinConfidence` (o se le due direzioni dell'ordine delle opzioni non concordano). L'investigator prende il verdetto da Jev (`JevVerdictClient`) e chiama Foundry solo per scrivere la descrizione di un gap; se Jev fallisce ricade sul percorso solo-Foundry. |

Gli stessi casi passano nei due bracci, uno dopo l'altro, **alternando chi parte per primo** a ogni
ripetizione (cosi cache e throttling non favoriscono sempre lo stesso braccio). Le chiamate sono
sequenziali: si misura il modello, non la coda.

Due **tracce**:

1. `classify`: tipo di documento (`Msa`, `OrderForm`, `Sow`, `Amendment`, `Quote`, `Invoice`,
   `PriceList`, `Nda`, `Dpa`, `Other`). Set: `cases/classify-cases.v1.json`, **30 casi sintetici**
   (3 per tipo, in italiano, inglese e tedesco) scritti per questo benchmark. La provenienza e scritta
   nel file e nel report: sono estratti inventati, nessun documento reale. Sono volutamente
   non ambigui, quindi il set e **facile e ottimistico** (effetto soffitto: se entrambi i bracci
   sono vicini al 100% non si vede nessuna differenza). Va sostituito o esteso con documenti reali,
   anonimizzati e con consenso, prima di trarre conclusioni sul traffico vero.
2. `gaps`: "questo turno chiede qualcosa che Raffa non sa fare?" (`gap` / `no-gap`). Set: i **150 casi**
   di `Raffa.AiEval/investigator-set/investigator-eval.v1.json` (INV-04), collegati (non copiati) nel
   progetto. L'etichetta e `gapWorthy` degli autori INV-04: vero = `gap`, falso = `no-gap`. Includono
   i *hard negative* (domande sui contratti che contengono un verbo operativo ma non sono una richiesta
   di funzione). Anche questo set e un seme sintetico in attesa di revisione del product owner.

I casi golden di `Raffa.AiEval/golden/*.json` **non sono riusabili** qui: sono domande Ask end-to-end
su tenant di prova e non etichettano ne tipi di documento ne verdetti del gap investigator; il ruolo
`classify` non e coinvolto.

## 2. Cosa misura

Per ogni braccio e per ogni caso (con N ripetizioni, warm-up scartato):

- **Correttezza** rispetto all'etichetta: accuratezza (un errore conta come sbagliato), accuratezza sulle
  sole risposte date, precision/recall/F1 per classe, F1 macro, matrice di confusione (con colonna `(error)`).
- **Latenza** end to end del braccio (compresi tentativi falliti e fallback): media, p50, p95, p99.
  Il campione contiene solo le chiamate andate a buon fine.
- **Token e costo**: token Foundry (dal gateway), token Jev (dalla risposta), **costo Jev riportato da
  OpenRouter** (`usage.cost`, letto a livello HTTP perche il gateway non lo espone), costo Foundry stimato
  solo se si danno i prezzi (`RAFFA_JEV_BENCH_FOUNDRY_USD_PER_1M_IN/_OUT`). Senza prezzi si riportano i
  token e il costo non viene inventato.
- **Errori, timeout, fallback**: tasso di errori e timeout per braccio; per B quante volte Foundry ha
  servito al posto di Jev, distinguendo `jev-error` (Jev non ha dato una risposta utilizzabile) da
  `low-confidence` (Jev ha risposto ma il gate di confidenza/ordine ha rimandato a Foundry). Sono contati
  anche i tentativi HTTP verso Jev (retry inclusi) e quelli falliti.
- **Accordo tra i bracci**: percentuale di risposte uguali sulle coppie (caso, ripetizione) in cui entrambi
  hanno risposto, e kappa di Cohen.
- **Stabilita**: stesso input ripetuto N volte; per caso, quota della risposta piu frequente; elenco dei
  casi "instabili" (risposte diverse).

### Statistiche del verdetto

Il confronto e **accoppiato** (stessi casi nei due bracci).

- A livello di caso, un braccio e "giusto" se lo e nella maggioranza (> 50%) delle ripetizioni.
- **McNemar esatto** (due code, binomiale sui casi discordanti): p-value sulla differenza di accuratezza.
- **Bootstrap percentile** (95%, seed fisso, 10 000 iterazioni di default) sulla differenza per caso del
  tasso di risposte giuste (B meno A).
- Latenza: bootstrap sulla differenza accoppiata delle **mediane per caso** (B meno A), piu i delta di
  p50/p95/p99 sui campioni completi.
- Regola: la qualita e **improvement** (o **regression**) solo se McNemar ha p < 0.05 **e** l'intervallo
  bootstrap esclude lo zero; uno solo dei due segnali = **inconclusive**; nessuno = **no-significant-difference**.
  La latenza e **faster/slower** solo se l'IC della differenza accoppiata esclude lo zero. Il costo e
  **cheaper/more-expensive** se differisce di oltre il 5% a parita di chiamate, **not-computable** se mancano i prezzi.
- Avvertenze automatiche: meno di 30 casi, meno di 10 casi discordanti (McNemar debole), fallback di B
  sopra il 20% (B misura in gran parte Foundry), errori, ripetizioni = 1, costo incompleto.

## 3. Cosa il benchmark NON puo misurare

- Il **traffico reale**: i dati sono sintetici o semi (vedi sopra). Niente documenti reali, niente
  distribuzione reale dei tipi, niente rumore OCR.
- L'effetto sull'**intero** prodotto (estrazione, Ask, qualita delle risposte): Jev tocca solo `classify` e il
  verdetto dell'investigator. Gli altri ruoli restano su Foundry in entrambi i bracci.
- La **qualita del testo** che l'investigator scrive per un gap (lo scrive sempre Foundry) e l'utilita del
  follow-up per l'utente.
- La **latenza percepita** dall'utente nell'app (l'investigator gira accanto alla risposta), la latenza sotto
  carico/concorrenza e i limiti di rate del provider: le chiamate sono sequenziali.
- La **deriva nel tempo**: Jev e un alias di modello pinnato (`typesafe/jev-1.13`); il modello servito
  (`typesafe/jev-1.13-<data>`) e registrato nei metadati ma il risultato vale per il momento dell'esecuzione.
- Il **costo Foundry reale** senza prezzi forniti, e il costo di Jev se la chiamata va diretta a TypeSafe
  (il campo `cost` esiste solo via OpenRouter).
- La **correttezza assoluta** dell'etichetta: le etichette sono giudizi degli autori dei set.
- Le **cause** di un fallback oltre le due categorie `jev-error` / `low-confidence`.

## 4. Abilitare Jev in dev

Il benchmark forza `Enabled=true` nel solo braccio B, quindi non richiede `jev_enabled` per girare. Serve
pero la chiave, e per provare Jev anche nel prodotto in dev:

1. Nel workspace HCP `raffa-dev` crea (o aggiorna) la variabile **Terraform** (categoria *Terraform
   variable*, non *Environment variable*) `jev_api_key`, marcata **Sensitive**. E una chiave OpenRouter.
   **Mai** metterla nel repo, in un `.tfvars`, in un workflow o in un file locale committato.
2. Imposta `jev_enabled = true` (variabile normale, pubblica `AiGateway__Jev__Enabled=true` sulle due
   Container App). Senza chiave, con `jev_enabled=true`, ogni chiamata `classify` fallisce in modo controllato.
3. Esegui l'apply HCP. Terraform scrive la chiave nel Key Vault come segreto `jev-api-key` e la espone
   all'app API come `AiGateway__Jev__ApiKey` (riferimento al segreto). Dettagli e altri flag:
   [`ai-flows-dev-flags.md`](ai-flows-dev-flags.md).
4. Verifica che il segreto esista: `az keyvault secret show --vault-name <kv-dev> --name jev-api-key --query name`.

Il workflow legge la chiave direttamente dal Key Vault a runtime (con il principal OIDC di dev, ruolo *Key
Vault Secrets User*) e la maschera: nessun secret GitHub, nessun valore nel repo.

## 5. Variabili d'ambiente

Per il gating servono tutte e tre le prime:

| Variabile | Obbligatoria | Significato |
|---|---|---|
| `RAFFA_JEV_BENCH` | si | Deve essere esattamente `1`. Altrimenti il test e **saltato** (con motivo). |
| `AiGateway__Endpoint` | si | Endpoint Foundry (stesso formato dell'API deployata). |
| `AiGateway__Jev__ApiKey` | si | Chiave OpenRouter. Solo in ambiente, mai su file. |
| `AiGateway__Models__*`, `AiGateway__ProjectName`, `AiGateway__OpenAiApiVersion`, `AiGateway__Jev__*` (modello, soglie, `CheckOptionOrder`), `Chat__GapInvestigation__*` | no | Stessa forma dell'host: sono letti come lo legge l'API, e il loro valore finisce nel report. |
| `RAFFA_JEV_BENCH_OUT` | no | Cartella di output (default `./jev-benchmark-out`). |
| `RAFFA_JEV_BENCH_TRACKS` | no | `classify`, `gaps` o `classify,gaps` (default entrambe). |
| `RAFFA_JEV_BENCH_REPS` | no | Ripetizioni per caso e braccio (default 3, 1..100). Servono >= 2 per la stabilita. |
| `RAFFA_JEV_BENCH_WARMUP` | no | Chiamate di warm-up per braccio, scartate (default 2). |
| `RAFFA_JEV_BENCH_MAX_CASES` | no | Solo i primi N casi per traccia (0 = tutti). Per un primo giro economico. |
| `RAFFA_JEV_BENCH_DELAY_MS` | no | Pausa tra le chiamate, se il provider limita il rate. |
| `RAFFA_JEV_BENCH_GAP_TIMEOUT_S` | no | Timeout dell'investigator (default 30 s). |
| `RAFFA_JEV_BENCH_FOUNDRY_USD_PER_1M_IN` / `_OUT` | no | Prezzi Foundry (USD per milione di token) per stimare il costo. |
| `RAFFA_JEV_BENCH_BOOTSTRAP` / `RAFFA_JEV_BENCH_SEED` | no | Iterazioni (default 10000) e seed (default 20261016) del bootstrap. |

Ordine di grandezza delle chiamate (default: 3 ripetizioni, 2 warm-up): `classify` 30 x 3 x 2 = **180**
chiamate misurate; `gaps` 150 x 3 x 2 = **900** (piu le chiamate Foundry che B fa per descrivere i gap e
per i fallback). Per iniziare: `RAFFA_JEV_BENCH_MAX_CASES=10 RAFFA_JEV_BENCH_REPS=2`.

## 6. Eseguire in locale

Servono `az login` con accesso dati al deployment Foundry di dev (ruoli *Cognitive Services User* e
*OpenAI User*): l'autenticazione Foundry e `DefaultAzureCredential`, come nell'API.

```bash
cd backend
export RAFFA_JEV_BENCH=1
export AiGateway__Endpoint="https://<endpoint-foundry-dev>"      # come sulla Container App ca-raffa-dev-api
# gli id dei modelli: copia i valori AiGateway__Models__* della stessa app
export AiGateway__Jev__ApiKey="$(az keyvault secret show --vault-name <kv-dev> --name jev-api-key --query value -o tsv)"
export RAFFA_JEV_BENCH_OUT="$PWD/jev-benchmark-out"
dotnet test tests/Raffa.JevBenchmark.Tests --filter "Category=JevBenchmark" --logger "console;verbosity=detailed"
```

Senza `RAFFA_JEV_BENCH=1` il test risulta `Skipped` con il motivo; i test unitari dello stesso progetto
girano comunque (`--filter "Category!=JevBenchmark"` per escludere esplicitamente quello live).

## 7. Eseguire da GitHub Actions

Workflow **jev-benchmark** (Actions, *Run workflow*): solo `workflow_dispatch`, solo ambiente `dev`,
login Azure OIDC (come `seed-demo-fixture`), nessun segreto nel repo.

1. Legge dalla Container App `ca-raffa-dev-api` la configurazione **non segreta** del gateway
   (`AiGateway__*`, `Chat__GapInvestigation__*`), cosi si misura cio che dev esegue davvero.
2. Legge `jev-api-key` dal Key Vault di dev e la maschera.
3. Compila solo il progetto del benchmark ed esegue `--filter Category=JevBenchmark`.
4. Scrive il report nel *job summary* e carica `jev-benchmark-result.json` e `jev-benchmark-report.md` come
   artefatto (90 giorni).

Input: `tracks`, `repetitions`, `warmup_calls`, `max_cases_per_track` e i due prezzi Foundry opzionali.
Prerequisiti (stessi di `seed-market-intelligence`): apply HCP eseguito, `ai_gateway_wired = true`,
`ci_deploy_ai_access = true` (il principal di deploy ha i ruoli dati sul Foundry di dev), account Foundry
raggiungibile dal runner, e `jev_api_key` impostata.

## 8. Come leggere il report

File prodotti nella cartella `RAFFA_JEV_BENCH_OUT`:

- `jev-benchmark-report.md`: per traccia, **Verdetto**, tabella affiancata A/B con delta (B - A), accordo e
  test statistici, dettaglio per braccio (precision/recall/F1 per classe, matrice di confusione), e in fondo
  la **configurazione esatta** usata.
- `jev-benchmark-result.json`: stessi dati in forma leggibile da macchina (`schemaVersion` 1), incluse le
  osservazioni grezze per caso/ripetizione/braccio (predizione, confidenza, latenza, chiamate con token e
  costo, motivo di fallback). Contiene id dei casi ma non i loro testi.

La configurazione registrata include: modelli Foundry (classify e analyst) con versione, endpoint (solo
host), modello e soglie di Jev, `CheckOptionOrder`, soglie dell'investigator, retry e timeout, parametri
del run (ripetizioni, warm-up, seed), hash SHA-256 di ogni set di casi, commit, URL del run, runtime e OS.
La chiave Jev e riportata solo come "configurata si/no".

## 9. Come interpretare il miglioramento

1. **Guarda prima le avvertenze.** Poche coppie di casi o pochi casi discordanti rendono il test poco
   potente: "no-significant-difference" allora significa "non lo so", non "uguali".
2. **Il tasso di fallback di B decide cosa stai misurando.** Se Foundry serve molte richieste di B, il
   risultato di B e in gran parte Foundry. Un fallback alto per `low-confidence` suggerisce di tarare
   `ClassifyMinConfidence`; alto per `jev-error` indica problemi di rete, rate limit o chiave.
3. **Qualita**: leggi la differenza di accuratezza con il suo IC95% e il p di McNemar; guarda i casi
   "corretti da Jev" e "peggiorati da Jev" (sono elencati per id) prima di fidarti della media. F1 macro e
   le righe per classe mostrano se il guadagno e su classi rare o solo su quelle facili.
4. **Latenza**: usa il delta accoppiato per caso (con IC) e p95/p99 per le code; la latenza di B include il
   tentativo Jev fallito piu la chiamata Foundry di fallback. Un p50 piu basso con un p99 piu alto e un
   segnale di fallback frequenti.
5. **Costo**: confronta il costo stimato per 1000 chiamate solo se la riga e completa (prezzi Foundry dati e
   costo Jev riportato). Altrimenti confronta i token medi per chiamata.
6. **Stabilita**: una quota vicina al 100% e attesa; i casi instabili sono quelli a bordo decisione.
7. **Un solo run non basta**: ripeti in momenti diversi (il modello servito e registrato) e conserva i JSON.
   Sul set sintetico di `classify` aspettati un effetto soffitto; il segnale piu informativo e quello della
   traccia `gaps` e, appena disponibile, di un set di documenti reali etichettati.

Un esito "improvement" su questi set e un'evidenza di miglioramento **su questi casi**, non una garanzia
sul traffico di produzione.

## 10. Test deterministici e CI

I test del progetto `Raffa.JevBenchmark.Tests` (calcolo delle metriche, test statistici, generazione del
report, cablaggio dei bracci con gateway e trasporto Jev finti, gating) non fanno rete, non usano la chiave
e girano in pochi secondi nella suite normale (`dotnet test Raffa.slnx`). Il test live e marcato
`[Trait("Category","JevBenchmark")]` e viene **saltato** se manca `RAFFA_JEV_BENCH=1`, l'endpoint Foundry
o la chiave Jev, quindi la CI resta verde e non chiama mai verso l'esterno. Il workflow `jev-benchmark` e
solo manuale: non e agganciato a push, pull request o schedule.

## 11. Estendere i set

- `classify`: aggiungi casi a `backend/tests/Raffa.JevBenchmark.Tests/cases/classify-cases.v1.json`
  (`id` unico, `language`, `expected` = nome di `AiDocumentType`, `text`). Aggiorna `provenance` e incrementa
  `version` se i casi sono reali: documenti reali solo anonimizzati e con consenso.
- `gaps`: si modifica il set INV-04 in `Raffa.AiEval/investigator-set/`; qui viene letto cosi com'e.
- L'hash SHA-256 di ogni set e nel report: confronta due run solo se l'hash coincide.
