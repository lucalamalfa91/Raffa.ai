# Trattativa di rinnovo tutta in Raffa — analisi

Stato: **analisi / proposta** (13 nov 2026). Serve una decisione di prodotto (ADR) prima di implementare,
perché cambia un principio del brief AskRaffa 4.0.

## 1. Il problema

Oggi una pratica di rinnovo esce da Raffa a ogni giro di trattativa:

1. Raffa prepara la bozza → approvazione della versione esatta.
2. **Copia** → l'utente incolla nella propria casella e invia **fuori da Raffa**.
3. L'utente torna e dichiara **"Invio confermato"**.
4. Attende la risposta del fornitore **nella propria casella**.
5. **Incolla** il testo della risposta in Raffa (una volta per ogni giro: proposta, controproposta, conferma…).
6. Carica la bozza di Order Form, poi il contratto firmato, scaricandoli dalla mail.

Ogni passaggio manuale è un punto in cui la pratica si ferma, perde allegati o diventa incoerente
(testo inviato ≠ testo approvato, risposta non registrata, promemoria che scattano anche se il fornitore
ha già risposto).

Obiettivo: **invio, ricezione delle risposte, allegati e nuovo contratto gestiti dentro la pratica**,
senza che l'utente debba uscire dall'applicazione — mantenendo i controlli che oggi rendono la pratica
affidabile (approvazione della versione esatta, evidenza scritta, audit).

## 2. Com'è fatto oggi (punti del codice)

| Area | Dove | Comportamento attuale |
|---|---|---|
| Principio di prodotto | `docs/design/Raffa.ai/design_handoff_askraffa_v4/reference/AskRaffa_4.0_brief.md` §10.3 (r. 299) | "Raffa non invia email e non legge automaticamente la casella nel primo rilascio." Vieta CTA "Invia email". |
| Stati email | `backend/src/Raffa.Renewals/Domain/Cases/CaseRecords.cs` (`CaseEmailRecord`) | `draft · to_approve · approved · copied · sent · superseded`; `sent` è solo **dichiarato** (`SentBy`, `SentBody`). |
| Invio | `RenewalCaseEngine.cs` azioni `copy` / `sentOpen` / `sentOk`; endpoint `POST …/emails/{eid}/copy` e `…/sent` | Nessun trasporto: si copia il testo e si registra la dichiarazione. |
| Risposta fornitore | `RenewalCaseEngine.Reply` (≈ r. 1652); `POST /api/renewals/{id}/vendor-messages` | Solo **testo incollato**. Niente mittente, data reale, thread, allegati. Dedup per hash (`CaseVendorMessageRecord`). |
| Lettura AI | `RenewalCaseService.ReadBeforeStepAsync` → `ICaseAiReader.ReadReplyAsync / ReadDraftAsync / ReadSignedAsync` | Funziona su testo: riutilizzabile così com'è per le mail in arrivo. |
| Bozza / firmato | `POST /api/renewals/{id}/documents` (`RenewalCasesEndpointExtensions.DocumentAsync`) → azione `addDoc` | Il file viene parsato e **i byte vengono scartati**: resta il nome file e il testo. Il contratto firmato **non diventa un documento** in Documents/Portfolio; a chiusura si scrivono solo i nuovi termini (`termWriter.WriteCurrentAsync`) e il risparmio. |
| Promemoria | `ProposeReminder`, azione `ff` | Il testo dice esplicitamente "non vedo la tua casella". |
| Mail in uscita esistente | `backend/src/Raffa.Api/Infrastructure/AcsInvitationMailer.cs`, `infra/modules/communication` | Azure Communication Services Email, solo per gli inviti; dominio gestito `…azurecomm.net` (deliverability scarsa), dominio custom già previsto in Terraform (rollout in due fasi, DKIM/SPF). Nessun canale in ingresso. |
| Identità utenti | Entra (OTP via email, anche indirizzi Gmail) | Non si può assumere che ogni cliente abbia una casella Microsoft 365. |

## 3. Scelta chiave: da quale casella parte la mail?

| Opzione | Come funziona | Pro | Contro |
|---|---|---|---|
| **A. Canale Raffa** (consigliata come base) | Raffa invia dal proprio dominio verificato (`trattative@<dominio-raffa>`), nome visualizzato "Mario Rossi — ACME Srl", **Reply-To = indirizzo della pratica + indirizzo dell'utente**, CC all'utente. Le risposte arrivano a un indirizzo per pratica (`r-<token>@in.<dominio>`) letto da Raffa. | Funziona per qualunque cliente (Gmail, M365, altro). Nessun permesso sulla casella aziendale. Thread certo. Riusa ACS. | Il fornitore vede un mittente "via Raffa". Serve un provider di posta **in ingresso** (ACS non lo ha). Dominio custom e DMARC obbligatori. |
| **B. Casella collegata** (M365 via Graph, Gmail API) | OAuth delegato `Mail.Send` + lettura limitata alla conversazione della pratica. | Il fornitore vede il buyer, i thread restano nella casella aziendale. | Consenso admin per `Mail.ReadWrite`/Gmail restricted scopes (verifica Google), due integrazioni, revoche token, clienti senza M365. |
| **C. Cattura in CC/BCC** (complemento) | L'utente invia dalla propria casella mettendo in CC l'indirizzo della pratica; Raffa registra invio e risposte che lo includono. | Zero integrazioni, zero cambi di abitudini. | Dipende dalla disciplina dell'utente; il fornitore può rispondere senza "Rispondi a tutti". |

**Raccomandazione:** costruire su **A + C** (stesso indirizzo per pratica serve entrambi) e lasciare **B**
come estensione opzionale per i clienti M365 che vogliono "invia come me". A + C copre tutti i clienti con
un solo canale in ingresso; B si aggiunge dopo senza cambiare il modello dati.

## 4. Flusso target

```
Bozza ─► Da approvare ─► Approvata ─► [Invia] ─► In consegna ─► Consegnata
                                         │                         │
                                         └─► Rifiutata/Bounce ◄────┘ (evento di consegna)

Fornitore risponde ─► casella pratica ─► webhook ─► abbinamento al caso
   ─► "Risposta arrivata" (testo pulito + allegati, letto dall'AI)
   ─► [Registra come risposta]  ─► stessa logica di oggi (Reply → analisi → decisione / controproposta / accordo)
   ─► allegato classificato come bozza / firmato / offerta ─► [Usa come bozza v2] / [Usa come contratto firmato]
Firmato confermato ─► il PDF diventa un documento in Documents collegato al contratto ─► estrazione ─► validazione ─► nuovi termini
```

Principi mantenuti:

- **Si invia solo la versione approvata, byte per byte.** Il pulsante *Invia* esiste solo su `approved`; il
  server invia il corpo della versione approvata (hash registrato), mai il testo in editing.
- **Una risposta in arrivo non fa avanzare la pratica da sola.** Viene letta e proposta; un clic del
  responsabile (o delegato) la registra. Evita che un fuori-sede, uno spam o una mail di un altro
  interlocutore cambino le condizioni concordate. (Auto-registrazione per thread certi = possibile opzione
  per workspace, dopo.)
- **Il percorso manuale resta** come fallback (sotto *Altro*): Copia + "Ho inviato da un'altra casella",
  incolla risposta, carica `.eml/.msg`/PDF.
- Il contenuto delle mail del fornitore è **dato, non istruzioni** per il modello (già il pattern di `ICaseAiReader`).

## 5. Interventi per area

### 5.1 Uscita (invio)

- Nuova astrazione `ICaseMailSender` (modulo Renewals, porta) con adapter host `AcsCaseMailSender`
  (stesso schema di `AcsInvitationMailer`: adapter in `Raffa.Api/Infrastructure`, SDK solo lì).
- Nuova azione engine `sendOut` (non passiva, richiede `CanWork`): verifica stato `approved`, destinatario
  presente e valido, genera `Message-ID` proprio, mette in coda il job di invio, stato `sending`.
- Il job (worker, coda esistente in `Raffa.Messaging`) chiama ACS con `WaitUntil.Started`, salva l'operation
  id; gli **eventi di consegna ACS** (Event Grid: Delivered / Bounced / Suppressed / Failed) aggiornano lo stato.
- Allegati: oggi il brief dice "allega nella tua casella" (vedi screenshot: il PDF del contratto non viene
  copiato). Con l'invio in-app gli allegati scelti vengono presi dallo storage del documento e inviati.
- Header: `Reply-To` (pratica + utente), `CC` utente, `In-Reply-To`/`References` sui messaggi successivi,
  così follow-up e controproposte restano nello stesso thread del fornitore.
- `AfterSent` / `ProposeReminder` restano uguali, ma partono dall'evento reale di invio.

### 5.2 Ingresso (risposte del fornitore)

- **Indirizzo per pratica**: token non indovinabile, mappato a (tenant, caso). Creato all'apertura del caso,
  disattivato alla chiusura (+ periodo di grazia).
- **Provider inbound** (ACS non riceve posta). Da valutare con criteri: residenza dati UE, firma dei webhook,
  MIME grezzo disponibile, gestione allegati fino a 20–25 MB, costo. Candidati: casella condivisa Exchange
  Online + notifiche Graph (resta in Azure/Microsoft), oppure un servizio con inbound parsing/inbox via webhook
  (SendGrid Inbound Parse, Mailgun EU, Postmark, Resend). Decisione nel relativo ADR.
- Pipeline `InboundCaseMailJob` (worker):
  1. verifica firma webhook, salva MIME grezzo nello storage del tenant;
  2. dedup per `Message-ID` + hash contenuto (estende l'attuale `ContentHash`);
  3. **abbinamento**: indirizzo pratica → `In-Reply-To`/`References` su Message-ID inviati → fallback
     dominio mittente + oggetto. Non abbinata = coda **"Da smistare"** visibile all'admin, mai scartata;
  4. estrae testo (rimozione citazioni/firma), mittente, data reale, allegati;
  5. antivirus sugli allegati (Defender for Storage) e limiti di dimensione/formato già usati da Documents;
  6. pre-lettura AI (`ReadReplyAsync`, e `ReadDraftAsync`/`ReadSignedAsync` sugli allegati classificati);
  7. voce in **Oggi** e notifica "Il fornitore ha risposto", promemoria di sollecito annullati.
- Fuori-sede / bounce / mittente non riconosciuto: etichettati, non proposti come risposta.

### 5.3 Documenti e nuovo contratto

- **Persistere i file della pratica** (oggi scartati): bozze, firmato, offerte alternative, allegati in arrivo
  → storage + record `case_attachment`, scaricabili dalla pratica.
- Alla conferma del firmato (`sigOk`): il PDF viene registrato **come documento in Documents**, collegato al
  contratto (nuova versione / rinnovo), e passa dalla pipeline di estrazione esistente invece della sola
  lettura testuale. `Validate` continua a scrivere i nuovi termini; in più il contratto in Portfolio punta al
  nuovo file firmato.
- Upload manuale di `.eml`/`.msg` (drag & drop nella pratica) come fallback: stesso parser della pipeline
  inbound, così anche chi non usa il canale Raffa recupera mittente, data e allegati senza incollare.

### 5.4 Modello dati (bozza)

| Tabella | Novità |
|---|---|
| `case_email` | `sent_via` (raffa / manual / mailbox), `internet_message_id`, `provider_operation_id`, `delivery_state` (sending / delivered / bounced / failed), `approved_body_hash`, `cc`, allegati inviati |
| `case_mailbox` (nuova) | tenant, caso, token indirizzo, attivo da/a |
| `case_vendor_message` | `from`, `to`, `cc`, `subject`, `message_id`, `in_reply_to`, `raw_blob_key`, `match_state` (matched / unmatched / ignored), `kind` (reply / auto_reply / bounce), `registered_by/at` (chi l'ha confermata come risposta) |
| `case_attachment` (nuova) | messaggio/documento di origine, blob, nome, hash, classificazione (draft / signed / offer / other), scansione AV |
| `case_event` | nuovi tipi: `email_sent_system`, `email_delivered`, `email_bounced`, `inbound_received`, `inbound_registered`, `inbound_unmatched`, `attachment_promoted` |

Tutte tenant-scoped con RLS come le tabelle esistenti; log senza destinatari, corpi o oggetti (come
`AcsInvitationMailer`).

### 5.5 Interfaccia (CaseRoute)

- Card email: stati **Bozza · Da approvare · Approvata · Inviata · Consegnata** (+ *Non consegnata*);
  pulsante primario **Invia al fornitore** dopo l'approvazione; *Copia* e *Ho inviato da un'altra casella*
  sotto *Altro*. Campo destinatario obbligatorio con suggerimento dai contatti già usati nel caso.
- **Vista conversazione** della pratica: messaggi in uscita e in entrata in ordine, con allegati.
- Banner "Risposta arrivata da … il …" con anteprima della lettura AI e pulsanti *Registra come risposta* /
  *Non è una risposta*; allegati con *Usa come bozza contratto* / *Usa come contratto firmato*.
- Copy da aggiornare: frasi tipo "non vedo la tua casella" valgono solo se la pratica non usa il canale Raffa.

### 5.6 Notifiche: "il fornitore ha risposto"

Oggi non esiste un sistema di notifiche (solo la lista **Oggi** e i promemoria interni). Con la ricezione
in-app l'evento più importante della trattativa — *nuovo messaggio dal fornitore* — deve raggiungere il
responsabile dove lavora, sul canale che sceglie lui.

**Eventi notificabili** (prima versione):

| Evento | Destinatari |
|---|---|
| `supplier_message_received` — nuova mail del fornitore abbinata alla pratica | responsabile + delegati della pratica |
| `supplier_attachment_received` — allegato riconosciuto come bozza/firmato | responsabile |
| `email_bounced` — invio non consegnato | chi ha inviato + responsabile |
| `approval_requested` — una bozza attende la tua approvazione | approvatore / delegati |
| `inbound_unmatched` — mail arrivata e non abbinata | admin del workspace |
| `check_date_due` — data di controllo raggiunta senza risposta | responsabile |

**Canali** — l'utente collega quelli che preferisce e sceglie, per tipo di evento, dove riceverlo:

| Canale | Come | Note |
|---|---|---|
| **In app** (sempre attivo) | campanella + voce in Oggi + badge sulla pratica | base, nessun collegamento |
| **Push browser** | Web Push (VAPID) + service worker sul web | funziona anche a scheda chiusa; su iOS solo con PWA installata |
| **Push mobile** | Expo Push → APNs/FCM dall'app `mobile/` | quando l'app mobile esce dalla lane non-gating |
| **Email** | ACS, mail di avviso all'utente (non al fornitore) | utile come fallback |
| **Microsoft Teams** | app/bot Teams (Bot Framework) con messaggio proattivo in chat personale; in alternativa Workflows webhook su un canale | il bot permette il pulsante "Apri la pratica" e l'identità Entra coincide |
| **Slack** | app Slack con OAuth (`chat:write`), DM all'utente collegato | collegamento per utente o per workspace |
| **WhatsApp** | WhatsApp Business Cloud API (Meta) o tramite Twilio/ACS Advanced Messaging; template approvati da Meta | richiede opt-in esplicito e numero verificato; i messaggi avviati dall'azienda devono usare template |

**Principi**

- **La notifica non contiene la trattativa.** Solo "Nuovo messaggio da *Fornitore* sulla pratica *Contratto*"
  + link alla pratica. Niente prezzi, condizioni o testo della mail su WhatsApp/Slack/Teams (canali fuori dal
  perimetro di Raffa, inoltrabili, conservati da terzi). Un'opzione per workspace può abilitare un'anteprima
  per Teams/Slack aziendali.
- **Preferenze per utente**, con default per workspace decisi dall'admin; l'admin può disabilitare canali
  (es. vietare WhatsApp).
- **Ore di silenzio e raggruppamento**: più messaggi della stessa pratica in pochi minuti → una notifica.
- **Consegna affidabile**: outbox transazionale; l'evento si scrive insieme al `case_event`, un job del
  worker lo consegna con retry; se un canale fallisce resta sempre l'in-app.
- **Collegamento sicuro** di ogni canale (OAuth per Slack/Teams, codice di verifica per WhatsApp), revocabile
  dall'utente in qualunque momento; token cifrati (Key Vault).

**Architettura**

- Nuovo modulo `Raffa.Notifications`: `INotificationPublisher.PublishAsync(event)` usato dagli altri moduli;
  tabelle `notification` (in-app, letta/non letta), `notification_channel` (canali collegati per utente),
  `notification_preference` (evento × canale), `notification_delivery` (tentativi, esito).
- Un adapter per canale (`WebPushSender`, `ExpoPushSender`, `TeamsSender`, `SlackSender`,
  `WhatsAppSender`, `EmailNotificationSender`) dietro `INotificationChannel`; i provider SDK solo negli adapter.
- Web: centro notifiche (campanella) + pagina *Impostazioni → Notifiche* per collegare canali e scegliere
  gli eventi.

**Ordine consigliato**: in-app + push browser insieme a F3 (sono il minimo perché la ricezione sia utile);
poi Teams e Slack; WhatsApp per ultimo (approvazione template Meta, costi per conversazione, opt-in).

## 6. Sicurezza, conformità, rischi

- **Deliverability**: obbligatorio il dominio custom (già previsto in `infra/modules/communication`) con
  SPF/DKIM/DMARC; il dominio gestito `azurecomm.net` non è adatto a mail verso fornitori.
- **Identità percepita**: il fornitore riceve da un dominio Raffa → nome visualizzato chiaro, CC al buyer,
  Reply-To al buyer. Alcuni clienti vorranno solo l'opzione B.
- **Spoofing in ingresso**: la verifica SPF/DKIM del mittente va mostrata; una mail che dichiara condizioni
  da un dominio diverso dal fornitore va segnalata, mai registrata in automatico.
- **Prompt injection** dal corpo/allegati del fornitore: già trattati come dati dal lettore AI; mantenerlo.
- **Retention/GDPR**: MIME grezzo e allegati sono dati personali/commerciali → retention allineata ai
  documenti del tenant, cancellazione con la pratica (`RenewalCaseService.Delete`).
- **Approvazione**: l'invio usa sempre la versione approvata (hash), qualunque modifica la invalida come oggi.
- **Audit**: ogni invio/ricezione/registrazione in `case_event` + audit log.

## 7. Piano a fasi

| Fase | Contenuto | Dipendenze esterne | Valore |
|---|---|---|---|
| **F0 — Decisioni** | ADR che supera §10.3 del brief; scelta provider inbound; dominio custom e DNS (fase 1 del modulo Terraform) | Dominio, DNS | Sblocca tutto |
| **F1 — File della pratica** | Persistenza file (oggi scartati); firmato → documento in Documents collegato al contratto; upload `.eml/.msg` con allegati | Nessuna | Subito meno lavoro manuale, contratto nuovo davvero in archivio |
| **F2 — Invio in-app** | `ICaseMailSender` + ACS dominio custom, azione `sendOut`, allegati, eventi di consegna, nuova UI card | ACS dominio verificato | L'utente non esce più per inviare |
| **F3 — Ricezione in-app** | Indirizzo per pratica, webhook inbound, abbinamento, coda "Da smistare", pre-lettura AI, conferma risposta, promozione allegati, voce in Oggi | Provider inbound | Botta e risposta interamente in Raffa |
| **F3b — Notifiche** | Modulo `Raffa.Notifications`, centro notifiche in-app, push browser (con F3); poi Teams, Slack, WhatsApp; preferenze per utente | Bot Teams, app Slack, WhatsApp Business | L'utente sa subito che il fornitore ha risposto, sul canale che preferisce |
| **F4 — Casella collegata (opz.)** | M365 Graph / Gmail per "invia come me" e lettura del solo thread della pratica | Consenso admin cliente | Per i clienti che non accettano un mittente Raffa |

F1 è indipendente e si può iniziare subito; F2 e F3 sono il cuore della richiesta; F4 solo su domanda.

## 8. Decisioni da prendere

1. **Mittente**: va bene un mittente "via Raffa" con Reply-To al buyer (A), o serve da subito l'invio dalla
   casella aziendale (B)?
2. **Avanzamento automatico**: una risposta abbinata con certezza può essere registrata senza clic, o resta
   sempre una conferma umana (raccomandato almeno all'inizio)?
3. **Provider inbound**: restare nell'ecosistema Microsoft (casella Exchange + Graph) o servizio inbound dedicato?
4. **Dominio**: quale dominio/sottodominio usare per invio (`trattative@…`) e ricezione (`in.…`)?
5. **Allegati in uscita**: quali documenti Raffa può allegare automaticamente (contratto attuale, piano, RFQ)?
6. **Notifiche**: anteprima del contenuto ammessa su Teams/Slack aziendali o sempre solo "nuovo messaggio + link"?
   WhatsApp tramite Meta diretto o tramite un provider (Twilio / ACS Advanced Messaging)?
