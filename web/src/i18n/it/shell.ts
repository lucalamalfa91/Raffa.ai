/**
 * Italian for the AskRaffa 4.0 shell: top nav, Guida Raffa panel and its per-screen fallback copy.
 * Keys are the prototype's English strings (reference/i18n-en.js, COPY-CATALOGUE.md); the Italian is
 * the prototype's own text, so IT output equals the prototype.
 */

export const IT_SHELL: Readonly<Record<string, string>> = {
  // Top nav
  "Today": "Oggi",
  "Renewals⟦nav⟧": "Rinnovi",
  "Contracts⟦nav⟧": "Contratti",
  "Results⟦nav⟧": "Risultati",
  "Ask⟦nav⟧": "Chiedi",
  "Primary": "Principale",
  "Raffa guide": "Guida Raffa",

  // Guide panel
  "Hide": "Nascondi",
  "Where you are": "Dove sei",
  "What to do now": "Cosa fare ora",
  "Why": "Perché",
  "Next⟦guide⟧": "Dopo",
  "Everything stays saved. You can ask a question, open a source, or leave and resume without losing your place.":
    "Tutto resta salvato. Puoi fare una domanda, aprire una fonte o uscire e riprendere senza perdere il punto.",

  // Fallback guide copy per screen (the prototype's `G`, static parts)
  "Today · start": "Oggi · inizio",
  "Upload the first contract.": "Carica il primo contratto.",
  "Raffa works only on read and verified contracts: a doubtful notice date cannot drive a decision.":
    "Raffa lavora solo su contratti letti e verificati: una data di disdetta in dubbio non può guidare una decisione.",
  "Contract validated → the notice deadline appears in Today → you run the renewal through to signature.":
    "Contratto validato → compare la scadenza di disdetta in Oggi → avvii il rinnovo fino alla firma.",
  "Upload the first contract": "Carica il primo contratto",
  "No step waits for you.": "Nessun passo aspetta te.",
  "Sorted by notice deadline. The work happens in the case, one step at a time.":
    "Ordinate per scadenza di disdetta. Il lavoro avviene nella pratica, un passo alla volta.",
  "To start: within 120 days of the notice deadline.": "Da avviare: entro 120 giorni dalla disdetta.",
  "One step at a time: everything else stays saved in the details.":
    "Un passo alla volta: il resto resta salvato nella scheda.",
  "After the questions I draft the plan; you confirm it.": "Dopo le domande preparo il piano; tu lo confermi.",
  "Validated contracts": "Contratti validati",
  "Open a contract or upload the next one.": "Apri un contratto o carica il prossimo.",
  "Only validated contracts feed Today and Renewals.": "Solo i contratti validati alimentano Oggi e Rinnovi.",
  "A new file goes reading → verification → validation.": "Un nuovo file passa da lettura → verifica → validazione.",
  "Upload a contract": "Carica un contratto",
  "Decisive dates and negotiable terms first; everything else on request.":
    "Le date decisive e le condizioni negoziabili prima; tutto il resto su richiesta.",
  "Decisions flow into the renewal plan.": "Le decisioni entrano nel piano del rinnovo.",
  "Document⟦guide⟧": "Documento",
  "I ask you only about uncertain or conflicting fields; a high score never replaces your check.":
    "Ti chiedo solo i campi incerti o in conflitto; un punteggio alto non sostituisce il tuo controllo.",
  "Once validated, the contract enters Today, Renewals and Contracts.":
    "Validato, il contratto entra in Oggi, Rinnovi e Contratti.",
  "Results⟦guide⟧": "Risultati",
  "Write your goal in your own words, then read what is signed and what is missing.":
    "Scrivi il tuo obiettivo con parole tue, poi leggi cosa è firmato e cosa manca.",
  "Raffa never estimates: a figure exists only with a signed contract or an invoice. The goal is yours and the page adapts to it.":
    "Raffa non stima mai: un numero esiste solo con un contratto firmato o una fattura. L’obiettivo è tuo e la pagina si adatta a quello.",
  "With the goal set you see how much is covered and which renewals can close it.":
    "Con l’obiettivo impostato vedi quanto è coperto e quali rinnovi lo possono chiudere.",
  "Quote⟦guide⟧": "Offerta",
  "After the decision, emails and signature follow the case.": "Dopo la decisione, email e firma seguono la pratica.",
  "Ask Raffa": "Chiedi a Raffa",
  "Write a question or pick a task.": "Scrivi una domanda o scegli un’attività.",
  "I answer only from validated contracts, with the source — or I say I cannot. Conversations stay on the left: recent, cases and archived.":
    "Rispondo solo dai contratti validati, con la fonte — o dico che non posso. Le conversazioni restano a sinistra: recenti, pratiche e archiviate.",
  "A task becomes a guided case; an archived conversation reopens when you write in it.":
    "Un’attività diventa una pratica guidata; una conversazione archiviata si riapre scrivendo.",
  "Renewals⟦guide⟧": "Rinnovi",
  "Contracts⟦guide⟧": "Contratti",
  // Table pager (components/table/TablePager.tsx)
  "Table pages": "Pagine della tabella",
  "Page {n}": "Pagina {n}",
  "0 items": "0 elementi",
  "{start}–{end} of {total}": "{start}–{end} di {total}",
};
