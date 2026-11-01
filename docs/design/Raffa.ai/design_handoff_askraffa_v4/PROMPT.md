# Prompt per Claude Code — AskRaffa 4.0

Incolla questo prompt in Claude Code, dalla root della repo `lucalamalfa91/Raffa.ai`, dopo aver decompresso `design_handoff_askraffa_v4/` nella root.

---

Devi implementare nella nostra app (frontend in `web/src`) il redesign "AskRaffa 4.0", un flusso guidato per i rinnovi contrattuali.

**Regola zero: il risultato deve essere identico al prototipo.** Il prototipo `reference/AskRaffa V4.dc.html` è la fonte di verità; se testo e prototipo divergono, vale il prototipo. La parità si dimostra con i test in `parity/` (screenshot golden a 1440×900, stesso percorso di click, asserzioni sugli stili). Una schermata non è finita finché i test di parità non passano.

**Cosa leggere prima, in ordine**
1. `design_handoff_askraffa_v4/README.md` — specifica completa, con l'ordine di lettura degli altri file: `PARITY.md` (+ `parity/`), `DESIGN-GAPS.md` (cosa il design non definisce e la regola da applicare), `BACKEND-SPEC.md` (modello dati, stati, API, conflitti col backend esistente), `COPY-CATALOGUE.md`, `STYLE-INVENTORY.md`, `screenshots/`.
2. `design_handoff_askraffa_v4/reference/AskRaffa_4.0_brief.md` — il brief di prodotto. Le decisioni della sezione 2 sono definitive: non riaprirle.
3. `design_handoff_askraffa_v4/reference/AskRaffa V4.dc.html` — prototipo HTML navigabile. Aprilo in un browser, scegli *Scenario → Da zero* nella barra demo e percorri tutto il flusso fino alla firma. Per i dati precompilati usa *Con dati*.
4. `reference/renewal-case-v4.js` e `reference/savings-quote-v4.js` — sono la specifica eseguibile delle regole di dominio (fasi, stati email, permessi, confronto offerte, risultati). Leggili, non copiarli nel frontend.

**Come lavorare**
- Il prototipo è un **riferimento di design in HTML**, non codice da copiare. Ricrea l'interfaccia con i pattern, i componenti, il router, l'i18n e i token già presenti nella repo (`styles/tokens.css`, `components/shell`, `InfoTip`, ecc.).
- Prima di scrivere codice, esplora la repo e proponimi una mappa schermata → rotta/file esistente. Riusa `routes/renewals`, `routes/savings`, `routes/quotes` e la shell attuale; non creare rotte parallele.
- Fedeltà alta: stile Modernist (angoli a 0, regole da 1px e 2px, Archivo, accento rosso `#ec3013` solo per azione primaria e urgenza). I token sono in `reference/modernist-styles.css` e vanno mappati su quelli della repo.
- Lavora per fette verticali, e dopo ciascuna fermati per una mia verifica:
  1. shell: barra superiore con le 5 voci (Oggi · Rinnovi · Contratti · Risultati · Chiedi) e pannello **Guida Raffa** (Dove sei · Cosa fare ora · Perché · Dopo);
  2. **Oggi** (una sola carta alla volta, coda "Poi, per te", "In attesa di altri", stato vuoto "Si parte da qui");
  3. **Rinnovi** (elenco a gruppi) e **pratica guidata** (6 fasi, step corrente, email Bozza → Da approvare → Approvata → Copiata → Invio confermato, composer con domanda libera che ritorna al passo);
  4. **Contratti** e **Contratto** (date decisive, "Cosa richiede una decisione", dettagli a richiesta) e **Revisione** (un campo alla volta);
  5. **Risultati** (obiettivo a testo libero, KPI, grafici);
  6. **Chiedi** (conversazioni recenti/archiviate/pratiche) e **Offerta** (Verifica → Confronta → Decidi).
- Le regole di business non negoziabili sono nel README ("Hard product rules"): niente benchmark di mercato e nessun risparmio stimato; un risparmio conta solo con contratto firmato (contrattuale) o fatture (realizzato); le riduzioni di quantità sono mostrate a parte; i destinatari delle email non si inventano; copiare un'email non è inviarla; solo l'approvatore o un delegato esplicito approva; ogni risposta cita la fonte o dichiara di non poterla dare.
- **Regola UX centrale**: massimo 2–3 scelte per schermata, un'azione primaria evidente, tutto il resto dietro un controllo secondario ("Altro ▾", "Dettagli ▾"). Non aggiungere pulsanti o sezioni che nel prototipo non ci sono.
- La barra demo (scenario, "Vedi come", Reimposta) e i dati fittizi (Helvetia Foods, Salesforce, Microsoft, AWS, DocuSign) **non vanno in produzione**. Dove il prototipo simula qualcosa (risposte del fornitore, lettura dei documenti, parser dell'obiettivo, indirizzi di esempio), usa le API reali o indicami cosa manca e proponi il contratto API.
- Lingua: interfaccia IT/EN con cataloghi di messaggi proper. Il dizionario `i18n-en.js` del prototipo serve solo come lista di testi, non come meccanismo.

**Cosa voglio da te adesso**
1. Leggi i file sopra ed esplora `web/src`.
2. Rispondimi con: (a) la mappa schermata → file della repo, (b) le lacune backend rispetto a `BACKEND-SPEC.md` (verifica i punti marcati VERIFY leggendo `documents-contracts.sql` e l'OpenAPI), (c) le lacune di design di `DESIGN-GAPS.md` che ritieni insufficienti, (d) il piano in fette con l'ordine proposto e come farai passare i test di parità, (e) eventuali conflitti tra il design e la codebase.
3. Non scrivere codice finché non confermo il piano.
