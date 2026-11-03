/**
 * Italian for Documents (route `/documents`): the list, the upload, the review of a document and the
 * viewer. Keys are the English text the screens render.
 */

export const IT_DOCUMENTS: Readonly<Record<string, string>> = {
  // Guide
  "Upload contracts, or verify the ones waiting for you.": "Carica i contratti o verifica quelli che aspettano te.",
  "Raffa reads each file and asks you only about the facts it is not sure of.": "Raffa legge ogni file e ti chiede solo i dati di cui non è sicuro.",
  "A validated contract feeds Today, Renewals and Contracts.": "Un contratto validato alimenta Oggi, Rinnovi e Contratti.",

  // List: header, delete all, validated hook, legend
  "Choose a workspace before uploading documents.": "Scegli un workspace prima di caricare documenti.",
  "Loading documents…": "Carico i documenti…",
  "Loading…": "Caricamento…",
  "Documents unavailable": "Documenti non disponibili",
  "{n} document": "{n} documento",
  "{n} documents": "{n} documenti",
  "{base} · {n} waiting for your review": "{base} · {n} in attesa della tua verifica",
  "Delete all documents": "Elimina tutti i documenti",
  "Confirm delete all": "Conferma: elimina tutto",
  "This document could not be deleted.": "Non sono riuscito a eliminare questo documento.",
  "Documents could not be deleted.": "Non sono riuscito a eliminare i documenti.",
  "{name} is now askable.": "{name} ora è consultabile.",
  "Ask: when does it expire?": "Chiedi: quando scade?",
  "When does it expire?": "Quando scade?",
  "When does {supplier} expire?": "Quando scade {supplier}?",
  "uploaded → processing → needs review → completed. Only {completed} documents feed Ask Raffa, Portfolio and Renewals.":
    "caricato → in elaborazione → da verificare → completato. Solo i documenti {completed} alimentano Chiedi a Raffa, Contratti e Rinnovi.",
  "completed⟦documents⟧": "completati",
  "← Documents": "← Documenti",
  "Document not ready for review": "Documento non pronto per la verifica",
  "This document is not yet linked to a contract, or no longer exists.": "Questo documento non è ancora collegato a un contratto, oppure non esiste più.",
  "This document no longer exists": "Questo documento non esiste più",
  "It may have been deleted, or this link is stale.": "Potrebbe essere stato eliminato, oppure questo link non è più valido.",
  "Raffa.ai's document service is temporarily unavailable. Try again in a moment.":
    "Il servizio documenti di Raffa.ai non è al momento disponibile. Riprova tra un momento.",
  "The document list could not be loaded.": "Non riesco a caricare l’elenco dei documenti.",
  "Raffa.ai could not reprocess this document.": "Raffa.ai non è riuscito a rielaborare questo documento.",

  // Filter chips
  "Filter documents": "Filtra i documenti",
  "Needs your attention · {n}": "Richiedono attenzione · {n}",
  "All documents · {n}": "Tutti i documenti · {n}",
  "Not added · {n}": "Non aggiunti · {n}",
  "Completed documents are hidden — they are already askable.": "I documenti completati sono nascosti — sono già consultabili.",
  "Everything Raffa.ai keeps, including validated documents.": "Tutto ciò che Raffa.ai conserva, compresi i documenti validati.",
  "Files Raffa.ai did not keep — never counted, never askable.": "File che Raffa.ai non ha tenuto — mai conteggiati, mai consultabili.",

  // Row grid
  "Nothing needs you right now.": "Al momento non c’è nulla per te.",
  "Every uploaded document is validated and askable. Completed documents are tucked away — switch to {all} to browse them.":
    "Ogni documento caricato è validato e consultabile. I documenti completati sono messi da parte — passa a {all} per sfogliarli.",
  "All documents": "Tutti i documenti",
  "Supplier · type": "Fornitore · tipo",
  "Next step": "Prossimo passo",
  "Document pages": "Pagine dei documenti",
  "Processing in the background": "In elaborazione in background",
  "{n} page": "{n} pagina",
  "{n} pages": "{n} pagine",
  "View document": "Vedi il documento",
  "{stage}…": "{stage}…",
  "Queued": "In coda",
  "Confirm delete": "Conferma l’eliminazione",
  "Review {n} field": "Verifica {n} campo",
  "Review {n} fields": "Verifica {n} campi",
  "Ask about it": "Fai una domanda",
  "Open Quote check": "Apri Verifica offerta",
  "Not yet linked to a contract": "Non ancora collegato a un contratto",
  "Raffa.ai could not process this file. Try again.": "Raffa.ai non è riuscito a elaborare questo file. Riprova.",
  "One extraction step failed: {detail}": "Una fase dell’estrazione non è riuscita: {detail}",
  "Nothing has changed for five minutes, so this page stopped checking for updates.":
    "Da cinque minuti non cambia nulla, quindi questa pagina ha smesso di cercare aggiornamenti.",
  "Check again": "Controlla di nuovo",

  // Document statuses (styles/semantics.ts getStatusTag)
  "Uploaded": "Caricato",
  "Completed": "Completato",
  "Needs review": "Da verificare",
  "Failed": "Non riuscito",
  "Not added": "Non aggiunto",
  "Not uploaded": "Non caricato",

  // Processing stages (the backend's DocumentProcessingStageMap names; "Uploading" is in contracts4)
  "Classifying": "Classificazione",
  "OCR / text": "OCR / testo",
  "Sections & tables": "Sezioni e tabelle",
  "Extracting facts": "Estrazione dei dati",
  "Validating schema": "Controllo dello schema",

  // Document and contract types
  "MSA": "MSA",
  "Order Form": "Modulo d’ordine",
  "SOW": "SOW",
  "Amendment": "Addendum",
  "Renewal Letter": "Lettera di rinnovo",
  "Quote": "Offerta",
  "Invoice": "Fattura",
  "Price List": "Listino prezzi",
  "NDA": "NDA",
  "DPA": "DPA",
  "Other": "Altro",

  // Onboarding and upload
  "Start here": "Inizia da qui",
  "First your contracts. Then your questions.": "Prima i tuoi contratti. Poi le tue domande.",
  "01 · Upload": "01 · Carica",
  "Drop your contracts": "Trascina qui i tuoi contratti",
  "MSA, order forms, SOWs, amendments. PDF, DOCX, XLSX, PNG, JPG.": "MSA, moduli d’ordine, SOW, addendum. PDF, DOCX, XLSX, PNG, JPG.",
  "02 · Process": "02 · Elabora",
  "Raffa.ai extracts the facts": "Raffa.ai estrae i dati",
  "Every fact has a source and a confidence. You sign off the weak ones.": "Ogni dato ha una fonte e un’affidabilità. Tu confermi quelli incerti.",
  "03 · Ask": "03 · Chiedi",
  "Answers only from validated facts, with the page that proves them.": "Risposte solo da dati validati, con la pagina che li dimostra.",
  "Choose contract files from your computer": "Scegli i file dei contratti dal tuo computer",
  "Upload contracts": "Carica contratti",
  "or drop files anywhere in this box": "oppure trascina i file in questo riquadro",
  "or drop PDF · DOCX · XLSX · PNG · JPG here — you can leave while they process":
    "oppure trascina qui PDF · DOCX · XLSX · PNG · JPG — puoi uscire mentre li elaboro",
  "Sample contracts": "Contratti di esempio",
  "Or try a sample:": "Oppure prova un esempio:",
  "Sample MSA · clean": "MSA di esempio · chiaro",
  "Sample MSA · needs review": "MSA di esempio · da verificare",
  "Northwind Traders SA — every term stated once, plainly, with the supplier role labelled.":
    "Northwind Traders SA — ogni condizione scritta una volta sola, in modo chiaro, con il ruolo del fornitore indicato.",
  "Fabrikam Software GmbH — unlabelled parties, two annual fees and a self-contradicting renewal clause.":
    "Fabrikam Software GmbH — parti non indicate, due canoni annui e una clausola di rinnovo che si contraddice.",
  "Formats": "Formati",
  "Max size": "Dimensione massima",
  "50 MB / file": "50 MB per file",
  "Up to": "Fino a",
  "20 files at once": "20 file alla volta",

  // Upload outcomes (uploadPipeline.ts)
  "This looks like a recipe, not a contract. Raffa.ai only keeps contracts, order forms, quotes and the documents around them. Drop the signed agreement or the supplier's proposal.":
    "Sembra una ricetta, non un contratto. Raffa.ai conserva solo contratti, moduli d’ordine, offerte e i documenti che li accompagnano. Carica l’accordo firmato o la proposta del fornitore.",
  "Raffa.ai could not read any contract text in this file. Try a clearer scan or the original PDF.":
    "Raffa.ai non è riuscito a leggere alcun testo contrattuale in questo file. Prova con una scansione più nitida o con il PDF originale.",
  "This file is larger than 50 MB, the most Raffa.ai accepts.": "Questo file supera i 50 MB, il massimo che Raffa.ai accetta.",
  "This file is too large for Raffa.ai to accept. Try a smaller file, or split it into parts.":
    "Questo file è troppo grande perché Raffa.ai lo accetti. Prova con un file più piccolo o dividilo in parti.",
  "Raffa.ai cannot open this file type. Try the original PDF, or a clear scan of the signed pages.":
    "Raffa.ai non può aprire questo tipo di file. Prova con il PDF originale o con una scansione nitida delle pagine firmate.",
  "This file is already in your documents, so it was not uploaded again.": "Questo file è già tra i tuoi documenti, quindi non è stato caricato di nuovo.",
  "You do not have permission to upload {file}. Sign in again or ask a workspace admin.":
    "Non hai il permesso di caricare {file}. Accedi di nuovo o chiedi a un Workspace Admin.",
  "Too many uploads at once. Wait a moment, then try again.": "Troppi caricamenti insieme. Attendi un momento, poi riprova.",
  "Raffa.ai is temporarily unavailable and could not process {file}. Try again in a few minutes.":
    "Raffa.ai non è al momento disponibile e non ha potuto elaborare {file}. Riprova tra qualche minuto.",
  "Raffa.ai could not process {file}. Try again.": "Raffa.ai non è riuscito a elaborare {file}. Riprova.",

  // Progress panel (`?progress=`)
  "Raffa.ai is giving this document priority over the rest of the queue.": "Raffa.ai sta dando a questo documento la precedenza sul resto della coda.",
  "Processing stages": "Fasi di elaborazione",
  "in progress": "in corso",
  "Queued, starting shortly": "In coda, parte a breve",
  "Ready for a quick review.": "Pronto per una verifica veloce.",
  "Review now": "Verifica ora",
  "Ready in Quote check.": "Pronto in Verifica offerta.",
  "Done -- it is now askable.": "Fatto -- ora è consultabile.",
  "Raffa.ai did not add this file.": "Raffa.ai non ha aggiunto questo file.",

  // Review (`?review=`): state, header, field list, evidence pane
  "Choose a workspace before reviewing a document.": "Scegli un workspace prima di verificare un documento.",
  "Loading review…": "Carico la verifica…",
  "Contract not found": "Contratto non trovato",
  "This document's contract does not exist, or is not in your workspace.": "Il contratto di questo documento non esiste o non è nel tuo workspace.",
  "Review unavailable": "Verifica non disponibile",
  "Correction history could not be loaded — an already-corrected field may show as pending review.":
    "Non riesco a caricare lo storico delle correzioni — un campo già corretto può risultare ancora da verificare.",
  "Extraction evidence could not be loaded — every field shows as needing review, with no source passage.":
    "Non riesco a caricare le prove dell’estrazione — ogni campo risulta da verificare, senza passaggio di origine.",
  "Raffa.ai's contract service is temporarily unavailable. Try again in a moment.":
    "Il servizio contratti di Raffa.ai non è al momento disponibile. Riprova tra un momento.",
  "The correction could not be saved.": "Non sono riuscito a salvare la correzione.",
  "The fields could not be accepted.": "Non sono riuscito ad accettare i campi.",
  "The document could not be marked as validated.": "Non sono riuscito a segnare il documento come validato.",
  "R1 · Human validation": "R1 · Validazione umana",
  "{n} facts need you — you decide⟦one⟧": "{n} dato aspetta te — decidi tu",
  "{n} facts need you — you decide": "{n} dati aspettano te — decidi tu",
  "Supplier {id}": "Fornitore {id}",
  "Validating…": "Validazione in corso…",
  "This document is already validated.": "Questo documento è già validato.",
  "This contract has no document to validate.": "Questo contratto non ha un documento da validare.",
  "{n} field still needs review before this contract can be marked validated.":
    "{n} campo va ancora verificato prima di poter segnare questo contratto come validato.",
  "{n} fields still need review before this contract can be marked validated.":
    "{n} campi vanno ancora verificati prima di poter segnare questo contratto come validato.",
  "{resolved} of {total} field resolved": "{resolved} di {total} campo risolto",
  "{resolved} of {total} fields resolved": "{resolved} di {total} campi risolti",
  "{n} needs review": "{n} da verificare",
  "{n} need review": "{n} da verificare",
  "auto": "auto",
  "auto-accepted": "accettato in automatico",
  "review⟦legend⟧": "verifica",
  "review required": "da verificare",
  "Accept all ({n})": "Accetta tutti ({n})",
  "Field": "Campo",
  "Extracted value": "Valore estratto",
  "Confidence": "Affidabilità",
  "Decision": "Decisione",
  "Accepted automatically": "Accettato in automatico",
  "Accepted by you": "Accettato da te",
  "Accepted automatically · {pct}%": "Accettato in automatico · {pct}%",
  "Review · {pct}%": "Da verificare · {pct}%",
  "Corrected → {value}": "Corretto → {value}",
  "Not found in the document": "Non trovati nel documento",
  "Raffa could not find these in the file. Type the value if you have it — it is saved as your correction.":
    "Raffa non ha trovato questi dati nel file. Scrivi il valore se lo hai — viene salvato come tua correzione.",
  "Proposed, not yet applied · {source}": "Proposto, non ancora applicato · {source}",
  "No source recorded": "Nessuna fonte registrata",
  "p. {page} · “{span}”": "p. {page} · “{span}”",
  "p. {page}": "p. {page}",
  "From the whole document": "Dall’intero documento",
  "Select…": "Seleziona…",
  "Yes": "Sì",
  "No": "No",
  "Contract type": "Tipo di contratto",
  "End date": "Data di fine",
  "Effective date": "Data di efficacia",
  "Cancellation deadline": "Scadenza di disdetta",
  "Total contract value (TCV)": "Valore totale del contratto (TCV)",
  "Renewal term (months)": "Durata del rinnovo (mesi)",
  "Select a field to see its evidence and correction form.": "Seleziona un campo per vederne la fonte e il modulo di correzione.",
  "Proposed value: {value} — not applied until you accept or correct it": "Valore proposto: {value} — non applicato finché non lo accetti o lo correggi",
  "Extracted value: {value}": "Valore estratto: {value}",
  "Correct value": "Valore corretto",
  "Reason (optional)": "Motivo (facoltativo)",
  "Save correction": "Salva la correzione",
  "Correction history": "Storico delle correzioni",
  "Not yet corrected — this is the original extracted value.": "Non ancora corretto — è il valore estratto originale.",
  "No reason given": "Nessun motivo indicato",
  "Accepted as extracted.": "Accettato come estratto.",
  "No source passage was recorded for this field — the extraction did not report it.":
    "Per questo campo non è registrato alcun passaggio di origine — l’estrazione non l’ha indicato.",
  "SOURCE DOCUMENT": "DOCUMENTO DI ORIGINE",
  "PAGE {n}": "PAGINA {n}",
  "WHOLE DOCUMENT": "INTERO DOCUMENTO",
  "Extracted by {model}": "Estratto da {model}",
  "Extracted": "Estratto",
  "confidence {pct}": "affidabilità {pct}",
  "Read from the full document text — proposed “{value}”.": "Letto dal testo completo del documento — proposto “{value}”.",
  "Read from the full document text.": "Letto dal testo completo del documento.",
  "Open in document viewer": "Apri nel visualizzatore",

  // Viewer
  "Document viewer": "Visualizzatore documenti",
  "Choose a workspace before opening a document.": "Scegli un workspace prima di aprire un documento.",
  "This document is not in this workspace.": "Questo documento non è in questo workspace.",
  "Preview unavailable": "Anteprima non disponibile",
  "Raffa.ai's document preview service": "Il servizio di anteprima documenti di Raffa.ai",
  "{service} is temporarily unavailable. Try again in a moment.": "{service} non è al momento disponibile. Riprova tra un momento.",
  "{service} could not load this page.": "{service} non è riuscito a caricare questa pagina.",
  "Page": "Pagina",
  "Previous": "Precedente",
  "Next⟦page⟧": "Successiva",
  "Page {page} of {total}": "Pagina {page} di {total}",
  "Page 1 · total unknown": "Pagina 1 · totale sconosciuto",
  "Page {page} — the wording is highlighted below": "Pagina {page} — il testo è evidenziato qui sotto",
  "Page not in this document": "Pagina non presente in questo documento",
  "This document has {n} pages.⟦one⟧": "Questo documento ha {n} pagina.",
  "This document has {n} pages.": "Questo documento ha {n} pagine.",
  "Go to page 1": "Vai a pagina 1",
  "The citation could not be restored.": "Non riesco a ripristinare la citazione.",
  "The document is still open. You can page through it.": "Il documento resta aperto. Puoi sfogliarlo.",
  "Page no longer in this document": "Pagina non più presente in questo documento",
  "This page is no longer part of this document — it was re-processed after this link was made.":
    "Questa pagina non fa più parte di questo documento — è stato rielaborato dopo la creazione di questo link.",
  "Reload the document": "Ricarica il documento",
  "No page image yet": "Ancora nessuna immagine della pagina",
  "Document page": "Pagina del documento",

  // Info tips ("i") on these screens (components/infoTipCopy.ts); "What the statuses mean" is in members.ts
  "How Documents works": "Come funziona Documenti",
  "Start here. Upload your contracts: Raffa reads each file in the background, recognises what it is and pulls out the key facts — dates, spend, notice, renewal terms — noting the page each one comes from.":
    "Inizia da qui. Carica i tuoi contratti: Raffa legge ogni file in background, riconosce di cosa si tratta ed estrae i dati chiave — date, spesa, preavviso, condizioni di rinnovo — annotando la pagina da cui viene ciascuno.",
  "Facts Raffa is not sure of wait for you. Open Review, accept or correct them, then mark the document as validated.":
    "I dati di cui Raffa non è sicuro aspettano te. Apri la verifica, accettali o correggili, poi segna il documento come validato.",
  "Validated documents feed Ask Raffa, Portfolio, Renewals and Savings.": "I documenti validati alimentano Chiedi a Raffa, Contratti, Rinnovi e Risultati.",
  "You can leave this page while files are processed.": "Puoi lasciare questa pagina mentre i file vengono elaborati.",
  "How review works": "Come funziona la verifica",
  "These are the facts Raffa extracted from the document. The ones it is sure of are accepted automatically; the others wait for your decision.":
    "Questi sono i dati che Raffa ha estratto dal documento. Quelli di cui è sicuro sono accettati in automatico; gli altri aspettano la tua decisione.",
  "Click a field to see the passage it was read from, then Accept it or Correct it.":
    "Clicca un campo per vedere il passaggio da cui è stato letto, poi scegli Accetta o Correggi.",
  "When nothing is left to decide, mark it as validated: from then on the contract feeds Ask, Portfolio, Renewals and Savings.":
    "Quando non resta nulla da decidere, segnalo come validato: da quel momento il contratto alimenta Chiedi, Contratti, Rinnovi e Risultati.",
  "What you can upload": "Cosa puoi caricare",
  "Contracts, order forms, SOWs and amendments: PDF, Word, Excel or a photo (PNG, JPG), up to 50 MB each and 20 files at a time.":
    "Contratti, moduli d’ordine, SOW e addendum: PDF, Word, Excel o una foto (PNG, JPG), fino a 50 MB ciascuno e 20 file alla volta.",
  "A file that is not a contract is not added; you will find it under Not added.": "Un file che non è un contratto non viene aggiunto; lo trovi in Non aggiunti.",
  "Not ready to use your own? Try one of the samples.": "Non vuoi ancora usare i tuoi? Prova uno degli esempi.",
  "Uploaded → Processing → Needs review → Completed.": "Caricato → Elaborazione → Da verificare → Completato.",
  "Needs review: some facts are below Raffa's confidence bar and wait for you. Completed: validated, ready to ask about.":
    "Da verificare: alcuni dati sono sotto la soglia di affidabilità di Raffa e aspettano te. Completato: validato, pronto per le tue domande.",
  "Failed: Raffa gave up after several tries; the reason shows under the file's name.":
    "Non riuscito: Raffa ha rinunciato dopo diversi tentativi; il motivo compare sotto il nome del file.",
  "An upload that stalls is retried automatically.": "Un caricamento che si blocca viene ritentato in automatico.",
  "What to do next": "Cosa fare dopo",
  "Each row shows its one next step: review the facts that need you, ask about a validated contract, or — for a supplier's quote — open Quote check.":
    "Ogni riga mostra il suo unico prossimo passo: verifica i dati che aspettano te, fai una domanda su un contratto validato o — per l’offerta di un fornitore — apri Verifica offerta.",
  "Nothing to click while a file is processing — it carries on even if you leave.":
    "Niente da cliccare mentre un file è in elaborazione — continua anche se esci.",
  "What deleting everything removes": "Cosa rimuove l’eliminazione di tutto",
  "Every document in this workspace, the contracts built from them, their renewal tracking and the Ask chats about those contracts.":
    "Tutti i documenti di questo workspace, i contratti creati da essi, il monitoraggio dei loro rinnovi e le chat di Chiedi su quei contratti.",
  "There is no undo.": "Non si può annullare.",
  "Accept or correct": "Accetta o correggi",
  "Accept makes the extracted value the contract's official one.": "Accetta rende il valore estratto quello ufficiale del contratto.",
  "Correct lets you type the right value, with a reason if you like; the change is kept in the contract's history.":
    "Correggi ti fa scrivere il valore giusto, con un motivo se vuoi; la modifica resta nello storico del contratto.",
  "What validating does": "Cosa fa la validazione",
  "The document becomes Completed and its contract starts feeding Ask Raffa, Portfolio, Renewals and Savings.":
    "Il documento diventa Completato e il suo contratto inizia ad alimentare Chiedi a Raffa, Contratti, Rinnovi e Risultati.",
  "It unlocks once every field has a decision.": "Si sblocca quando ogni campo ha una decisione.",
  "About these filters": "Informazioni su questi filtri",
  "Needs your attention: documents still being processed, waiting for your review or that failed.":
    "Richiedono attenzione: documenti ancora in elaborazione, in attesa della tua verifica o non riusciti.",
  "All documents: those plus the validated ones.": "Tutti i documenti: questi più quelli validati.",
  "Not added: files Raffa did not keep because they are not a contract or could not be read.":
    "Non aggiunti: file che Raffa non ha tenuto perché non sono un contratto o non si potevano leggere.",
  "What confidence means": "Cosa significa l’affidabilità",
  "How sure Raffa is that it read the value right.": "Quanto Raffa è sicuro di aver letto bene il valore.",
  "A value above the workspace's bar is accepted automatically; below it, you decide.":
    "Un valore sopra la soglia del workspace è accettato in automatico; sotto, decidi tu.",
  "At {pct}% or more a value is accepted automatically; below, you decide.": "Con un’affidabilità di almeno {pct}% un valore è accettato in automatico; sotto, decidi tu.",
  "A red dot beside a field means it still waits for you.": "Un punto rosso accanto a un campo indica che aspetta ancora te.",
};
