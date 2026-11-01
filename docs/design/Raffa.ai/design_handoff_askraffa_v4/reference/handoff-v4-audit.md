# AskRaffa 4.0 — Handoff (design → development)

> **Aggiornamento:** il prototipo di riferimento è ora **`AskRaffa V4.dc.html`**, ricostruito da zero attorno a un flusso guidato (una cosa alla volta, massimo tre scelte per schermata, Guida Raffa sempre presente). `Raffa.ai V4.dc.html` resta come variante "a sezioni complete" dello stesso brief; `Raffa.ai V3.dc.html` è la versione precedente.

## 0. Principi del flusso guidato (AskRaffa V4)
- **Oggi** mostra una sola carta alla volta («1 di N»): fornitore, cosa fare, perché, scadenza etichettata. Tre scelte al massimo: *Fai ora* · un'alternativa contestuale (Delega a Marta / Me ne occupo io) · *Più tardi*.
- **Pratica**: in alto il riepilogo (fase · prossima azione e chi · disdetta/fine). Al centro **solo il passo corrente** con ≤3 pulsanti; tutto il resto sotto «Altro ▾». I passi completati sono una riga richiudibile; la scheda (condizioni, email, team, date) si apre su richiesta. Il composer accetta domande: Raffa risponde con la fonte e ripropone il passo in sospeso.
- **Contratti → contratto**: date decisive, «Cosa richiede una decisione» (≤3 punti, tre scelte ciascuno), una prossima azione; «Tutto il contratto» su richiesta.
- **Risultati** (ex Savings): contrattuali · realizzati da fatture · riduzioni di quantità a parte · obiettivi dell'utente. Nessuna stima.
- **Chiedi**: un campo e tre attività (Prepara un rinnovo · Riassumi un contratto · Confronta un'offerta → flusso Verifica · Confronta · Decidi in tre passi).
- **Guida Raffa** (pannello destro, richiudibile): Dove sei · Cosa fare ora · Perché · Dopo · un'unica azione consigliata — contestuale alla schermata e al passo.
- Navigazione: cinque voci (Oggi · Pratiche · Contratti · Risultati · Chiedi). Documenti è dentro Contratti («Carica un contratto» → lettura → verifica un campo alla volta → validato).
- Lingua: UI italiana; il motore della pratica è bilingue (IT/EN). La variante EN della UI è da aggiungere nel catalogo i18n in implementazione.


**File di riferimento:** `Raffa.ai V4.dc.html` (prototipo navigabile) · `renewal-case-v4.js` (motore della pratica) · `savings-quote-v4.js` (preventivi e risparmi) · `i18n-it.js` (catalogo IT).
**Brief applicato:** `uploads/AskRaffa_4.0_Prompt_Claude_Design_Definitivo-e6d57120.md`. Le decisioni della sezione 2 non sono state riaperte.
**Orologio di scenario:** 08 Sep 2026, condiviso da Home, Rinnovi, Portfolio, promemoria e timeline. Tutti i dati sono dimostrativi (barra "Demo").

## 1. Audit essenziale (V3 → V4)

| Area | Domanda principale | Essenziale ora | Utile su richiesta | Ridondante nella presentazione (rimosso, dato conservato) |
|---|---|---|---|---|
| Home (ex Today) | Cosa devo fare oggi? | ≤5 priorità (fornitore, motivo, scadenza etichettata, 1 CTA, chi è atteso) · Riprendi · 4 accessi | Vedi tutti in Rinnovi · Posticipati | Strip guida, coach, tour; fasce 30/120/oltre; stats; terza lista «Senza responsabile» (ora dentro le priorità del CPO) |
| AskRaffa | Ho una domanda o un'attività | 6 intenti + testo libero · raccolta del parametro solo se manca | Conversazioni precedenti | Griglia di «spunti» per categoria |
| Pratica (chat) | Qual è il passo attivo? | CaseSummary (fase, prossima azione · chi, scadenza disdetta, fine contratto, owner/approver) · un passo attivo | Dettagli: condizioni, piano, email/documenti, team, timeline | Stepper sempre visibile, checklist «Per avanzare», strip di mercato |
| Rinnovi | Cosa richiede intervento? | Gruppi Richiede azione · In corso · In attesa · Da avviare · Chiusi; preavviso e fine contratto distinti; sintesi condizioni (aumento, rinnovo, SLA); stato · owner · prossima azione · 1 CTA | Dettagli per riga (Termini, Cronologia, Email e documenti, Cosa aspettarti, Team) · Pipeline · ricerca, 3 filtri, 2 scorciatoie · azioni di gruppo dopo selezione | KPI strip (5), tab «Da fare ora» con wizard parallelo, vista «Coda azioni» (le voci vivono in Home / Richiede azione) |
| Contract 360 | Cosa richiede una decisione? | Identità/versione/stato · 4 date decisive · DecisionCard (dice · perché conta · fonte · proposta · decisione) · prossima azione | 6 sezioni a fisarmonica: leve, prodotti e prezzi, clausole, obblighi, rischi/priorità, termini chiave + documenti | Fascia «cosa è in gioco / quando muoverti / cosa fare» con risparmio in evidenza |
| Portfolio | Quali contratti abbiamo? | Sintesi (4 aggregati verificabili, contatori spiegati) · Richiede attenzione · elenco essenziale | Colonne aggiuntive, ricerca, vista Risparmi | Savings come sezione autonoma del rail |
| Risparmi | Cosa è verificabile? | Risultati contrattuali (firmati) · realizzati (fatture) · riduzioni di quantità a parte · obiettivi dell'utente | Per fornitore · ogni risultato con baseline/periodo/metodo/evidenza · filtri | Stime «in corso / identificati», intervalli, proiezioni, benchmark |
| Documenti / Review | Cosa richiede attenzione? | «Richiedono attenzione» · un campo alla volta con «cosa controllare e perché» | Tutti i documenti · tutti i campi | Legenda delle percentuali di confidenza |
| Preventivi | Quali condizioni devo valutare? | Carica → Collega → Verifica → Confronta → Decidi → Agisci; verdetto = una frase + ≤3 punti con evidenza | Tabella completa, mapping, calcolo · storico | Fasce P25–P75, mediana, «sopra/sotto mercato», target da percentili |

## 2. Mappa delle sezioni e del contesto mantenuto

Rail: **Home · AskRaffa · Rinnovi · Preventivi · Portfolio · Documenti**; secondario: Aiuto (unico accesso generale), Workspace e membri, lingua. Barra demo: orologio di scenario, persona (Luca CPO / Marta Buyer), lingua, reset.

Deep link conservati: `today → home`, `savings → portfolio?view=savings`, `quote` → Preventivi. Ogni ingresso a una pratica (`openCase(cid)`) riusa la stessa pratica e scorre al passo corrente; «Riprendi» in Home riporta all'ultima pratica aperta se non è già tra le priorità.

| Da | A | Contesto mantenuto |
|---|---|---|
| Home priorità | Chat al passo esatto / Revisione campo / Preventivo | cid, passo, ruolo |
| Rinnovi riga → «Riprendi nella chat» | Chat | cid; «Dettagli» espande la riga senza cambiare schermata |
| Contract 360 → prossima azione / «Apri chat del rinnovo» | Chat | cid, stessa pratica |
| Chat → «Confronta l'offerta» | Preventivi (check collegato) | cid, obiettivo del piano, altre offerte, policy |
| Preventivi → «Continua nella chat» | Chat | cid, stato del check |
| Portfolio aggregati | Rinnovi filtrati / Documenti attenzione | filtro |
| Chat «Apri in Rinnovi» | Rinnovi, riga espansa, tab Cronologia | cid |

## 3. Componenti riutilizzabili (varianti di stato nel prototipo)

- **PriorityCard** (Home): azione tua · in attesa di collega · in attesa del fornitore · scadenza senza responsabile; posticipato.
- **CaseSummary** (chat + riga Rinnovi): fase, prossima azione e persona attesa, scadenza, fine, ruoli; avviso permessi con «chi può procedere».
- **GuidedFlow / step card** (chat): corrente, completato (storico espandibile), bloccato per permesso, con input/chip/pulsanti.
- **EmailDraft**: Bozza · Da approvare · Approvata vN da X · Copiata · Invio confermato · Superata; modifica dopo approvazione (torna in bozza); modifica concorrente (banner + recupero testo); testo inviato diverso da quello approvato.
- **DecisionCard** (Contract 360 e passi della chat): condizione da discutere · rischio · SLA rilevante · da verificare; decisioni «Lo negoziamo / Lo accettiamo / Va chiarito» registrate con autore e data.
- **SourceLink / DetailsPanel**: «apri la clausola» evidenzia la fonte nella sezione Clausole senza lasciare la pagina; dettagli pratica a scomparsa.
- **ReviewField**: valore, origine, «perché è segnalato», cosa controllare, accetta/correggi.
- **EmptyState / ErrorState / Resume**: workspace vuoto (Carica → Elabora → Verifica → Usa), primo documento in elaborazione, nessuna urgenza, file duplicato, scansione illeggibile, permesso insufficiente, riprendi.

## 4. Stati progettati (sezione 16 del brief) e dove vederli

Nessun contratto → Home vuota (fixture `none`) · Nessuna urgenza → Home (Marta, dopo aver chiuso le voci) · Elaborazione → Documenti/Home dopo «Usa l'MSA di esempio» · Illeggibile → Documenti «Demo: unreadable scan» · Dati in conflitto → Contract 360 Salesforce «Da verificare» → Revisione · Duplicato → ricarica lo stesso file · Attesa fornitore → gruppo «In attesa» e Home «In attesa del fornitore» · Permesso insufficiente → Marta prova ad approvare (avviso + chi può procedere) · Bozza modificata dopo approvazione → Modifica → torna in bozza · Aggiornamento concorrente → chat, «Simula una modifica del collega» · Contratto diverso dall'accordo → bozza v1 con differenze · Firma registrata, acquisizione aperta → card «Negoziazione completata · nuovo contratto in revisione» · Operazione non supportata → nessun invio email, nessuna firma elettronica, nessun benchmark: limite dichiarato e via manuale.

## 5. Decisioni di design proposte (dettagli non definiti dal brief)

1. **Email «Da approvare» è uno stato esplicito**: il responsabile manda la bozza in approvazione (serve il destinatario); l'approvatore può comunque approvare direttamente una bozza completa. Qualsiasi modifica riporta in bozza.
2. **Fasi**: Avvio · Piano · Negoziazione · Accordo · Contratto e firma · Acquisizione (6). Bozza e firma condividono la fase perché le versioni si alternano.
3. **Domanda libera in chat**: risposta immediata con fonte, poi «Torniamo al passo in sospeso — …». Il commento interno è una modalità esplicita del composer («Nota per il team»).
4. **Home**: priorità = le mie azioni, poi ciò che aspetta altri entro 30 giorni, poi scadenze senza responsabile (solo CPO); massimo 5; «Vedi tutti» porta a Rinnovi.
5. **Decisioni in Contract 360** sono salvate per contratto e dichiarate «portate nel piano di rinnovo» — il collegamento effettivo alla preselezione delle clausole nell'intervista è un requisito da implementare (vedi §6).
6. **Risparmi**: nessuna stima. Gli «obiettivi» sono l'importo derivato dal target dell'utente (es. −5% × spesa contrattuale) e sono etichettati come tali; le riduzioni di quantità sono mostrate a parte.
7. **Preventivi senza confronto possibile** (Databricks): verdetto onesto «nessun confronto di prezzo disponibile», elenco di cosa si può valutare e cosa manca; CTA «Chiedi al fornitore i dati mancanti».

## 6. Requisiti da implementare (non ancora disponibili / simulati nel prototipo)

- Stato email `to_approve` nel case engine e nella API (oggi: draft/approved/copied/sent); versioning con `approvedVer`; registrazione del testo effettivamente inviato.
- Rilevamento di modifica concorrente della bozza (versione corrente, recupero del testo non salvato).
- Risposta di Raffa a domanda libera dentro la pratica, con ripresa del passo (oggi simulata dal motore di risposte del prototipo).
- Decisioni per punto in Contract 360 persistite e usate per preselezionare le clausole dell'intervista.
- Deduplica file al caricamento (hash) con «usa esistente / nuova versione»; stato «illeggibile» dalla pipeline.
- Obiettivi di risparmio per pratica derivati dal target dell'intervista; riconciliazione «realizzato» con le fatture.
- Preventivi: estrazione righe, collegamento a contratto/pratica, confronto con prezzo contrattuale per riga, altre offerte, policy. Nessun benchmark di mercato nel primo rilascio.
- Promemoria (data di controllo) persistiti e aggiornati all'arrivo della risposta.
- Permessi: delega esplicita per pratica (approvazione, chiusura) separata dal ruolo di workspace.

## 7. Prove previste dal brief (percorso nel prototipo)

1. Home → DocuSign «Apri a questo passo» → chat; «Dettagli» → piano; domanda libera nel composer → risposta → «Torniamo al passo in sospeso».
2. Microsoft (Marta): finisci l'intervista → Conferma piano → bozza → aggiungi destinatario (demo) → Invia in approvazione → come Luca «Approva questa versione» → Copia (resta «Copiata», non inviata).
3. «Ho inviato questa versione» → Conferma invio → data di controllo → (Demo) salta alla data → Incolla risposta → analisi → decisione → controproposta.
4. AWS: conferma condizioni concordate (deroga policy) → richiesta contratto → Aggiungi contratto (bozza v1 con 2 differenze) → email correttiva → v2 → firma fuori da Raffa → aggiungi firmato → conferma → pipeline → valida campi → esito.
5. Portfolio → riga DocuSign → Contract 360 → «Apri chat del rinnovo» → stessa pratica, stesso passo.
6. Preventivi → apri QC-07 (Databricks) → Collega «Nuova esigenza» → Verifica → Confronta (nessun confronto) → Decidi → Agisci. Dalla chat DocuSign «Confronta l'offerta» → check collegato con prezzo attuale e obiettivo.
7. Dalla chat: «Prepara disdetta» e «Valuta alternative» — il contratto corrente resta aperto finché l'esito non è registrato.
8. Documenti: «Demo: unreadable scan» → errore recuperabile; ricarica «Salesforce_MSA_2024.pdf» → duplicato; Home «Riprendi» dopo aver lasciato una pratica.
