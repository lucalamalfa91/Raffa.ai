# Prompt — Redesign Raffa AI 4.0 ("meno rumore, più guida")

> Da incollare a Claude Code (o a un designer) come brief unico.
> Contesto già verificato sul repo: SPA React in `web/src/routes/` con le aree
> `ask`, `documents`, `contracts` (Portfolio), `renewals`, `savings`, `quotes`,
> `today`, `workspace`. Rail a due livelli (vedi `README.md`, sezione "What you use").

---

## 1. Ruolo e obiettivo

Sei un Product Designer + Frontend Engineer senior. Devi riprogettare l'esperienza
di Raffa AI per la **versione 4.0**.

La 3.x contiene già le funzioni giuste (Ask, Documents, Portfolio, Renewals,
Savings, Quote check). Il problema **non è cosa fa, ma come lo mostra**: molte
pagine sovraccaricano l'utente di KPI, tabelle, filtri, colonne, pannelli e
azioni contemporanee, e l'utente non capisce dove guardare né cosa fare adesso.

**Obiettivo 4.0:** a ogni schermata l'utente deve capire in 5 secondi
1. dove si trova,
2. qual è la cosa più importante in questo momento,
3. qual è il prossimo passo,
4. come tornare indietro o cambiare strada senza perdersi.

Non si aggiungono funzioni. Si **riorganizza, riduce e guida**.

## 1b. Punto di partenza: cosa contiene oggi la v3 (audit del prototipo)

Fonte: prototipo v3 (artifact `claude.ai/artifact/2h8ufejnnp2QbHKfTzGu14`, "Modernist" design system,
IT/EN, dati fittizi Helvetia Foods). Nota: il prototipo ha **Today** come coda di lavoro e una **guida**
integrata; il `README.md` del repo descrive invece un'app che atterra su Ask. Il redesign deve
riconciliare le due cose (vedi §3.1).

| Area | Cosa mostra oggi | Dove sta il rumore |
|------|------------------|--------------------|
| **Shell** | Barra "Pilot demo" (atti, View as, IT/EN, Reset) + guide strip scura + coach overlay + drawer "Raffa.ai guide" + rail con Workspace, New chat, "From your contracts", guida, Workspace & members | Tre livelli di guida/chrome sovrapposti sopra ogni schermata; la guida è un'aggiunta, non il modo in cui si naviga |
| **Today** | Coda per urgenza: *Notice in*, contratto + prossimo step, *Stage*, *With*; azioni Open in Renewals / Snooze / Done; blocco "Start here" a 3 passi (Upload · Process · Ask) | È già il punto giusto, ma mescola onboarding e coda di lavoro; 4 colonne per riga + 3 azioni |
| **Documents / Review** | Attention vs All, tabella (documento · fornitore/tipo · stato · next step), poi Review a tabella campo/valore/confidenza/decisione con legenda soglie (>95, 80–95, <80) | Review mostra tutti i campi insieme; la logica delle soglie è spiegata a testo invece che guidata (uno alla volta, solo i deboli) |
| **Renewals** | Lista per data di disdetta + filtri + vista a stati; pannello caso con ~14 blocchi: Days to notice, Notice by, Ends, Owner, Approver, Market check, Offers vs contract, Current vs proposed (tabella), Plan goals, Timeline, Decision log, Emails, Documents & versions, What to expect, Team & delegation | È la pagina più densa: tutte le informazioni del caso sono visibili insieme; il "cosa fare adesso" è uno dei 14 blocchi |
| **Renewal chat** | Header con 5 meta (Next, Notice by, Ends, Owner, Approver·ruolo) + Steps so far + Vendor thread + paste reply + chip azione + bozze mail con Accept/Correct | Il flusso a passi esiste già qui: è il modello da generalizzare |
| **Contract 360** | 6 sezioni numerate impilate: Leverage, Products & pricing, Clauses that matter, Obligations, Risk factors, Key terms | Pagina lunga a scroll; nessuna gerarchia tra "da fare" e "da sapere" |
| **Portfolio** | Tabella a 8 colonne (Supplier, Contract, Annual spend, Ends, Give notice by, Start, Auto, Risk, Status) | Colonne di dettaglio visibili di default |
| **Savings** | Barra filtri (supplier, category, owner, Clear) + goal ("Tell Raffa your goal") + 3 sezioni numerate (Verified/Where you can still save/When you saved) + 4 KPI con tooltip + by supplier + verified outcomes | 4 KPI + 3 filtri + obiettivo + 3 sezioni nella stessa vista |
| **Quote check** | Landing (drop + "Compared with" + "When to use it" ×3 + saved checks) e risultato con: verdict, supplier quote, similar customers, vs median, target, what to do now, negotiation brief, counter-proposal, signature, blocco stato a 6 campi, tabella righe a 7 colonne | Il risultato mette in una pagina verdetto, 5 blocchi di contesto, 3 azioni e la tabella completa |

**Pattern già buoni da preservare e generalizzare:** Today come coda ordinata per urgenza; la
chat di rinnovo a passi con "Steps so far"; "Standard terms stay out of the way" in Contract 360;
"Target and negotiation levers are one step further"; i tooltip `i` sui KPI; lo stato salvato di ogni Quote check.

## 2. Principi di design (vincolanti)

1. **Un solo focus per schermata.** Ogni vista ha *una* domanda a cui risponde e
   *una* azione primaria. Tutto il resto è secondario, collassato o a un click di distanza.
2. **Progressive disclosure.** Prima il riassunto (3–4 informazioni al massimo
   sopra la piega), poi il dettaglio a richiesta ("Mostra altro", drawer, tab).
   Le colonne extra, i KPI di contorno e i filtri avanzati non sono visibili di default.
3. **Tutto è un flusso guidato.** Ogni operazione dell'utente (avviare un rinnovo,
   controllare un preventivo, rivedere un contratto, chiedere qualcosa a Raffa,
   caricare documenti) è un **wizard a passi** con: barra di avanzamento,
   passo corrente evidenziato, "Indietro / Avanti / Salva e esci", riepilogo finale.
4. **L'utente non viene mai abbandonato.** In ogni punto devono esserci:
   - una breadcrumb/stepper che mostra il percorso,
   - una "prossima azione consigliata" esplicita,
   - un modo per uscire e riprendere (stato salvato, "Riprendi da dove eri").
   Nessun vicolo cieco, nessuno stato vuoto senza una CTA, nessun errore senza istruzione.
5. **Autonomia.** L'utente deve poter portare a termine ogni flusso da solo:
   testi di aiuto contestuali brevi, esempi, valori precompilati dai contratti,
   spiegazione del *perché* Raffa propone qualcosa. Niente gergo interno.
6. **Gerarchia visiva rigida.** Massimo 1 CTA primaria, 1–2 secondarie per vista.
   Colori d'allerta solo per ciò che richiede azione (scadenze, rischi). Il resto è neutro.
7. **Coerenza.** Stessi pattern (wizard, card, drawer, stati vuoti, badge) in tutte
   le aree; stessa terminologia italiana/inglese del catalogo i18n esistente.

## 3. Nuova architettura dell'informazione

### 3.1 Home = Today evoluto in "Cosa vuoi fare?"
La v3 ha già **Today** (coda di lavoro). In 4.0 diventa la porta d'ingresso in due fasce:
1. **Quattro card d'intento** (grandi, sempre visibili): **Rinnovi** ("Cosa devo rinnovare e quando?"),
   **Preventivi** ("Questa offerta è buona?"), **Portfolio** ("Cosa ho firmato e con chi?"),
   **Chiedi a Raffa** ("Ho una domanda / un'attività"). Ognuna avvia il rispettivo wizard.
2. **"Richiede la tua attenzione ora"** = la coda di Today ridotta a max 3–5 voci
   (contratto · scadenza · prossimo step · un solo pulsante).

Sotto: "Riprendi" (ultimo flusso interrotto). Se il workspace è vuoto, al posto di tutto
resta solo l'onboarding "Carica i tuoi primi contratti" (i 3 passi Upload · Process · Ask di oggi).
La **guida** non è più uno strato separato (strip + coach + drawer): è assorbita dai wizard
(stepper + testo di aiuto al passo) e da un unico "Aiuto" nel rail. La barra "Pilot demo" resta
solo in modalità demo e non conta come chrome del prodotto.
Rail 4.0: Home · Chiedi a Raffa · Rinnovi · Preventivi · Portfolio · Documenti
(Savings come "Risparmi" dentro Portfolio/Rinnovi) · Workspace & members nel footer.

### 3.2 Renewals — flusso unico a imbuto
- **Livello 1 (lista):** solo "Cosa richiede azione ora", ordinata per urgenza,
  max ~5 righe visibili con raggruppamento *Scade presto / In corso / Da avviare*.
  Il KPI strip diventa una sola riga di 3 numeri cliccabili (non 6).
- **Livello 2 (caso):** wizard a passi fissi con stepper:
  *Capire → Preparare → Negoziare → Inviare → Chiudere.* Ogni passo mostra solo
  ciò che serve a quel passo (es. "Capire": scadenza, preavviso, valore, clausole
  chiave; non tutte le 6 tab insieme). Le tab attuali (Now, Terms, Clauses,
  Timeline, Team, Mail) diventano contenuto dei passi o sezioni "Dettagli" collassate.
- Azioni di gruppo (assegna, mostra in Portfolio) spostate in un menu "Azioni" e
  visibili solo con selezione attiva.

### 3.3 Preventivi (Quote check) — wizard lineare
Passi: *1 Carica preventivo → 2 Verifica le righe lette → 3 Confronta con contratti/mercato →
4 Risultato (verdetto in una frase + 3 punti chiave) → 5 Negozia (bozza email) → Salva.*
La tabella righe completa e il dettaglio mapping stanno in "Mostra dettaglio".
Lo storico è una vista separata e secondaria.

### 3.4 Portfolio — tre livelli di lettura
1. **Riepilogo** (default): 4 numeri (contratti, spesa annua, scadenze 90 gg, rischi)
   + elenco compatto "da guardare" (max 5).
2. **Elenco** completo con colonne essenziali (fornitore, tipo, scadenza, valore, stato);
   "Altre colonne" opzionale (come oggi, ma mai attivo di default).
3. **Contract 360** per singolo contratto, organizzato a sezioni con "Cosa devo fare" in cima.
I risparmi (Savings) si presentano come una lente del Portfolio, non come dashboard
separata con 8 blocchi: prima "Quanto hai risparmiato / puoi risparmiare", poi il dettaglio.

### 3.5 Chiedi a Raffa (chat strutturata)
Oggi la chat è libera e il composer ha molti toggle. In 4.0:
- Schermata iniziale con **intenti guidati** (chip/card): "Riassumi un contratto",
  "Prepara un rinnovo", "Confronta fornitori", "Trova clausole", "Cerca risparmi",
  "Altro…". Ogni intento apre un mini-wizard che raccoglie i parametri mancanti
  (quale contratto? quale periodo?) prima di inviare la domanda.
- Testo libero sempre disponibile, ma con suggerimenti contestuali.
- Risposte con struttura fissa: **risposta breve → evidenze/citazioni (collassate) →
  azioni successive consigliate**.
- Toggle avanzati (Web search ecc.) in un menu "Opzioni", non nel composer principale.
- Quando serve un'operazione non ancora disponibile, mantenere il comportamento
  attuale (ADR-030/031) ma presentato in modo meno invasivo.

### 3.6 Documents / Review
Il caricamento è un wizard: *Carica → Elaborazione (progresso chiaro) → Rivedi i dati
incerti (uno alla volta, solo quelli a bassa confidenza) → Convalida.* La lista documenti
mostra di default solo "Richiedono attenzione".

## 4. Pattern UI trasversali da introdurre

- **Componente Wizard unico** (`WizardShell`): stepper, titolo del passo, area
  contenuto, footer con Indietro/Avanti/Salva e esci, persistenza dello stato.
- **Next Best Action bar**: striscia fissa e discreta con la prossima azione consigliata
  nel contesto corrente.
- **Riprendi**: ogni flusso interrotto si salva e ricompare in Home.
- **Dettagli on demand**: componente standard per sezioni collassabili/drawer.
- **Stati vuoti e di errore guidati**: sempre cosa è successo, perché, cosa fare.
- **Aiuto contestuale**: `InfoTip` esistente riusato in modo sistematico e breve.
- **Densità**: limiti espliciti (max N righe/KPI/colonne sopra la piega) documentati
  e verificati nei test.

## 5. Vincoli tecnici

- Riusa stack e convenzioni esistenti (`web/`, React + TS + Vite, i18n con ids inglesi
  e catalogo italiano, CSS per area). Non cambiare backend né contratti API salvo
  necessità motivata e dichiarata.
- Riusa la logica esistente (es. `renewalActions.ts`, case engine, view model)
  cambiando la presentazione, non le regole di business.
- Regole di visibilità invariate: Portfolio/Renewals/Quote check restano spenti finché
  non esiste un contratto *validated*.
- Accessibilità: navigazione da tastiera, focus visibile, contrasto AA, stepper leggibile da screen reader.
- Responsive: i wizard devono funzionare anche su viewport stretto.
- Aggiorna/aggiungi test (Vitest/RTL) per i nuovi flussi e per i limiti di densità.

## 6. Metodo di lavoro richiesto

1. **Audit** (breve): per ogni area elenca cosa mostra oggi sopra la piega, conta
   KPI/colonne/azioni e classifica ciascun elemento come *Essenziale / Utile / Rumore*.
   Output: tabella.
2. **Proposta**: wireframe testuali o HTML per Home, un caso Renewals, Quote check,
   Portfolio riepilogo, Ask iniziale. Spiega cosa è stato rimosso, spostato o collassato e perché.
3. **Piano a fasi** con PR piccole: (a) design system/WizardShell/NBA bar,
   (b) Home + rail, (c) Renewals, (d) Quote check, (e) Portfolio + Savings,
   (f) Ask strutturato, (g) Documents/Review, (h) rifinitura e test.
4. **Implementazione** fase per fase, con feature flag `ui_v4` per convivere con la 3.x
   e poter confrontare/rollback.
5. **Verifica**: test automatici + screenshot prima/dopo per ogni schermata.

## 7. Criteri di accettazione

- Ogni schermata principale ha **una** CTA primaria evidente e al massimo 4 blocchi
  informativi sopra la piega.
- Dalla Home l'utente raggiunge Rinnovi, Preventivi, Portfolio e Chat con **un click**
  e ogni percorso parte con un wizard/flusso strutturato.
- Nessun flusso termina in un vicolo cieco; ogni flusso è interrompibile e riprendibile.
- Tutte le funzioni della 3.x restano raggiungibili (nessuna regressione funzionale).
- Un nuovo utente completa da solo "carica contratto → rivedi → avvia un rinnovo"
  senza istruzioni esterne.
- Test e lint verdi; copy in italiano e inglese coerente col catalogo i18n.

## 8. Cosa NON fare

- Non aggiungere nuove funzionalità di prodotto né nuovi KPI.
- Non nascondere in modo definitivo dati critici (scadenze, preavvisi): vanno
  ridotti per priorità, non eliminati.
- Non introdurre più di un pattern di navigazione per lo stesso scopo.
- Non toccare auth, tenancy o pipeline documentale.

## 9. Domande aperte da chiarire all'inizio

1. Today diventa la Home guidata (consigliato) o resta una voce separata accanto a una nuova Home? Il repo oggi atterra su Ask, il prototipo v3 su Today: quale vale?
2. Savings resta voce di rail o diventa lente di Portfolio/Rinnovi?
3. Quali ruoli (Admin/membro) vedono quali scorciatoie in Home?
4. Serve una modalità "esperto" che riporta la densità attuale?
