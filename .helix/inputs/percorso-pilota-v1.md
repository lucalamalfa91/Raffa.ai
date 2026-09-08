# Percorso pilota 1 — demo cliente V1

**Status:** canonico per la demo cliente V1  
**Fonte:** canvas *Contigo V1 — Percorso pilota cliente* (ridisegno a due primarie)  
**Relazione con lo spec:** [`product-spec.md`](./product-spec.md) resta il **WHAT** (requisiti, stati, soglie di fiducia, Ask Contigo, Contract 360). Questo file è il **processo di demo e di IA**: cosa si mostra al cliente, in che ordine, e come la navigazione si organizza intorno a due primarie.

Questo percorso **sostituisce il tour Day-1 a 10 tappe pari** come percorso da mostrare al cliente in V1. Il tour a 10 schermate non è un processo: diluisce upload e Ask. product-spec.md non viene riscritto qui; qui si fissa come il prodotto si percorre in sala.

---

## 1. Tesi

Il pilota ha **due primarie**:

1. **Documents** — caricare contratti (upload → OCR / extract → HITL sulla bassa fiducia).
2. **Ask Contigo** — knowledge base + query structured / RAG.

Tutto il resto è **satellite**: esiste perché Ask ha bisogno di un posto dove atterrare e il cliente ha bisogno di triage. I satelliti parlano con Ask; non sono tappe obbligatorie del demo.

Prima di vedere una tabella il cliente deve capire tre cose: cos’è un workspace, perché si carica un contratto, cosa significa fiducia.

---

## 2. Processo di onboarding (first-run reale)

In sala: un **Workspace Admin** (chi carica) e, se c’è, **Procurement**. Onboarding = alimentare la knowledge base, non il roster.

| # | Passo | Cosa succede | Cosa dici |
| --- | --- | --- | --- |
| 1 | Entra → workspace | Login OIDC. Se non ha workspace: form nome / industria / paese. Se ne ha uno, entra. | Questo è il vostro tenant. I contratti non escono da qui. |
| 2 | Empty state hero | Tre passi visibili: **Carica → Elabora → Chiedi**. CTA unica: **Carica contratti**. Nessun invite in hero. | Prima alimentiamo la knowledge base. Poi potete chiedere. |
| 3 | Fiducia, in linea | Tooltip sul primo campo estratto: **>95%** accettato, **80–95%** flag, **<80%** lo rivedete voi. Sotto 80% Contigo non usa il fatto per rinnovi / Ask consequenziale. | L’IA estrae. Voi firmate i fatti deboli. Niente decisioni al buio. |
| 4 | Invite (dopo, non prima) | Dopo il primo documento `completed`, o dal footer: invita Procurement. Legal / Finance / RO esistono nel modello, non nella nav del pilota. | Potete aggiungere il team quando la base è viva. |
| 5 | Satelliti vuoti | Portfolio / Home / Renewals: «Carica un contratto» con link a Documents. Ask: «Carica almeno un contratto validato». Review: coda vuota onesta. | Queste schermate si accendono dalla knowledge base, non a priori. |

**Non è onboarding:** invite come primo click; picker di due workspace finti con gli stessi dati; Create workspace come CTA orfana; email; production Entra; mobile.

---

## 3. Loop della spina + stati documento

| | |
| --- | --- |
| Superfici primarie | 2 — Documents \| Ask Contigo |
| Stati documento | 5 — `uploaded`, `processing`, `needs_review`, `completed`, `failed` |
| Soglia HITL | **<80%** (critical più stretti: value, cancellation, termination, renewal, uplift) |
| Ask si accende | Quando la KB è ready (`completed`) |

### Loop del pilota

| Fase | Sistema | Utente | Uscita |
| --- | --- | --- | --- |
| Carica | Object storage, job async | Drag-drop PDF / DOCX / XLSX (MSA, Order Form, SOW, Amendment; Quote opzionale) | `uploaded` → `processing` |
| OCR + extract | Parser nativo o OCR; sezioni; extraction staged per campo + confidence + source | Può lasciare la pagina | `needs_review` \| `completed` \| `failed` |
| Review fiducia | Coda solo campi **<80%** (critical più stretti) | Accept / Correct con pagina evidenziata. **Mark as validated** sbloccato solo quando i deboli sono decisi | `completed` — fatti canonicali |
| Knowledge base | Structured store + embeddings tenant-scoped | Niente da fare | Ask si accende |
| Ask Contigo | Auth filter → intent (SQL vs RAG) → evidence → risposta o abstain | Domande sul proprio portafoglio | Citazione cliccabile verso il documento / Contract 360 |

### Stati

| Status | Significato | Azione |
| --- | --- | --- |
| `uploaded` | Salvato, job non partito | Attendere |
| `processing` | OCR / extract in corso (tipico 20–60s in demo) | Leave-while-running |
| `needs_review` | Almeno un campo sotto soglia | Aprire Review |
| `completed` | Fatti usabili da Ask e satelliti | Chiedere |
| `failed` | PDF password, OCR morto, errore job | Retry o nuovo file |

**IA del pilota.** Nav primaria: **Documents | Ask Contigo**. Satelliti in un gruppo secondario (Portfolio, Contract 360, Renewals, Home, Quote, Workspace). **Review non è una voce pari della rail:** è uno stato di Documents. Non otto peer nella rail.

---

## 4. Contratto Ask Contigo

Ask non è una chat generica. È il **secondo prodotto**: risponde solo sulla knowledge base **validata**, con prova o astensione.

| Percorso | Comportamento | Esempio |
| --- | --- | --- |
| **Structured** | SQL su campi validati: supplier, spend, date, count. Trust pieno. | «Quali contratti scadono nei prossimi 120 giorni?» |
| **RAG / semantic** | Retrieval su sezioni e clausole, poi LLM solo su evidence. Citation obbligatoria. | «Che responsabilità abbiamo con AWS?» |

### Regole di risposta

| Regola | Comportamento |
| --- | --- |
| Auth prima del retrieve | Documenti non autorizzati fuori dal context |
| No evidence, no claim | Chip doc · pagina · sezione → Contract 360 / viewer |
| Abstain | «Non ho dati abbastanza confidenti» se coverage o confidence insufficienti |
| Context di schermo | Su un contratto aperto, Ask è di quel contratto; ⌘K resta globale |
| Suggestion chip | Due domande contestuali per schermo, non un elenco statico |

Un solo engine (router + RAG). Interfacce diverse: barra globale, sidebar sul Contract 360, chip inline. **Non due chatbot.**

---

## 5. Schermate satellite e attacco ad Ask

Esistono perché Ask ha bisogno di un posto dove atterrare, e il cliente ha bisogno di triage. **Non sono tappe obbligatorie del demo.**

**Nav:** Documents | Ask Contigo sono primarie. Review è uno **stato di Documents**, non un item peer della rail.

| Dove sei | Ruolo | Come si attacca ad Ask | Esempio chip |
| --- | --- | --- | --- |
| **Documents + Review** | Primaria. Ingresso del pilota: dropzone, pipeline, lista stati, coda HITL. | Stato della knowledge base («quanti docs non sono ancora chiedibili?»). Sul file in coda in Review. | Quali campi mancano ancora di fiducia? |
| **Portfolio** | Satellite. Inventario per triage. Filtri spec §8.1. Riga → Contract 360. Deferred: bulk, export, trend. | Query sul set filtrato (filtri attivi + set visibile). | Quali di questi hanno liability uncapped? |
| **Contract 360** | Satellite / proof. Destinazione delle citazioni, **non** lo spine. In demo: header + Overview + tab della prova (Clauses o Documents). Nove tab extra restano disponibili, non si girano. | Quel `contract_id`. Sidebar Ask sul contratto aperto. | Quando dobbiamo dare notice? |
| **Renewals** | Satellite. Lista prioritizzata se ci sono date validate. Insight + action. Email e outreach fuori pilota. | La riga / insight aperto. | Perché questo è in cima? / Quali startare per primi? |
| **Home** | Satellite. Pochi KPI che si accendono dalla KB (contratti analyzed, upcoming renewals). Non un tour savings. | KPI e opportunity, se esistono; altrimenti empty → Documents. | Dov’è il saving più grande ancora in review? |
| **Quote check** | Opzionale, **non hero**. Se il cliente chiede «e una nuova offerta?», un giro Extract → assessment. Altrimenti si salta. | La quote aperta; confronto con contratti già in KB. | Come si confronta con il nostro contratto Snowflake? |
| **Workspace & members** | Setup. Dopo il primo `completed`, non prima. Invite Procurement. Niente email nel pilota. | Non è una superficie Ask. | — |

---

## 6. Script demo live (20–25 min)

Due atti. **Atto A:** alimentare la KB. **Atto B:** chiedere. I satelliti si toccano solo come atterraggio di una citation o se il cliente tira. **Quote non è un atto hero** — è opzionale se avanza tempo.

| Min | Atto | Click | Dici |
| --- | --- | --- | --- |
| 0–2 | Onboarding | Empty hero Carica → Elabora → Chiedi | Workspace tenant. Prima i fatti, poi le domande. Fiducia = voi sui campi deboli. |
| 2–6 | Upload | Documents: drop MSA (es. Salesforce). Status `processing` visibile. | OCR se serve, extract per campo, ogni fatto ha source e confidence. |
| 6–11 | HITL | `needs_review` → due campi <80% lato documento → Accept / Correct → Mark as validated | Sotto 80% non uso il fatto. Voi firmate. Poi la base è chiedibile. |
| 11–15 | Ask structured | ⌘K: «Quando scade Salesforce?» → chip p.2 §2.1 → Contract 360 Documents | SQL sul campo validato. Prova dal file, non dal modello. |
| 15–19 | Ask RAG + abstain | «Quali responsabilità abbiamo?» poi «Quanto ha pagato il legale?» | Retrieval + citation. Seconda domanda: abstain. Meglio non so che inventare. |
| 19–22 | Satellite breve | Dalla citation resti sul Contract 360, oppure Portfolio filtrato / Renewals se chiedono «e poi?» | Queste schermate si accendono dalla stessa KB. Ask resta a portata. |
| 22–25 | Chiusura | Ask globale da Renewals o Home se già popolati; altrimenti stop su Ask | Stesso engine ovunque. Il moat è la KB vostra, validata, citabile. |

### Se avanza tempo (opzionale)

Una quote fixture: Extract → above / in-line / below market. Un minuto. Non aprire il 10-tab, non fare il tour KPI, non mostrare Swagger, non mostrare Legal nav, non mostrare email.

### Fixture

- Un MSA da caricare **live** (`processing` visibile).
- Opzionale: 2–3 contratti già `completed` per Ask ricco dopo il primo validate.
- Tenere un PDF password per mostrare `failed` solo se chiedono i casi brutti.

---

## 7. Fuori da questo pilota

Fuori da questo pilota anche se restano nello spec V1 pieno.

| Item | Perché fuori |
| --- | --- |
| Tour a 10 schermate pari | Non è un processo; diluisce upload e Ask |
| Quote check come tappa hero | Satellite; entra se il cliente lo tira |
| Home savings come demo | KPI vuoti o parziali finché la KB è piccola |
| Contract 360 10-tab deep-dive | È il viewer della prova, non lo spine |
| Legal / Finance / RO nav | RBAC sì, chrome unificato Procurement-first |
| Invite come primo click | Onboarding = carica, non roster |
| Email, mobile, Swagger, production Entra | Non servono a mostrare il processo |
| Outreach supplier, RFQ, CLM, firma | Non-goal prodotto |

---

## Come usarlo

Questo file è il brief di **demo e di IA**. Non sostituisce [`product-spec.md`](./product-spec.md) (WHAT). Sostituisce il Day-1 a 10 stop come percorso da mostrare al cliente in V1.
