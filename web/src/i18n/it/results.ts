/**
 * Italian for AskRaffa 4.0 Risultati (`routes/savings`): page, goal bar, "Verso il tuo obiettivo",
 * headline numbers, KPI strip, charts, lists, and the server's goal-draft messages
 * (`Raffa.Savings.Application.Results`). Keys are the prototype's English strings (i18n-en.js); the
 * Italian is the prototype's own text.
 */

export const IT_RESULTS: Readonly<Record<string, string>> = {
  // Page
  "Results⟦page⟧": "Risultati",
  "Verifiable figures only. Raffa never estimates a saving. The goal is yours, in your own words.":
    "Solo cifre verificabili. Raffa non stima mai un risparmio. L’obiettivo lo dai tu, con parole tue.",
  "Results are not available right now.": "I risultati non sono disponibili in questo momento.",
  "Retry": "Riprova",

  // Goal bar
  "Your goal": "Il tuo obiettivo",
  "No goal set — write it in your own words and the page adapts":
    "Nessun obiettivo impostato — scrivilo con parole tue e la pagina si adatta",
  "Set a goal": "Imposta un obiettivo",
  "Change⟦goal⟧": "Cambia",
  "Close": "Chiudi",
  "write it as you would tell a colleague — Raffa turns it into a target and adapts the page":
    "scrivilo come lo diresti a un collega — Raffa lo traduce in un target e adatta la pagina",
  "E.g. “I want to save 20% on IT contracts next year”": "Es. «Voglio risparmiare il 20% sui contratti IT l’anno prossimo»",
  "Set⟦goal⟧": "Imposta",
  "Remove goal": "Rimuovi obiettivo",
  "Try:": "Prova:",
  "I want to save 20% on IT contracts next year": "Voglio risparmiare il 20% sui contratti IT l’anno prossimo",
  "CHF 300k on software contracts in 2026": "CHF 300k sui contratti software nel 2026",
  "10% on cloud": "10% sul cloud",
  "Yes, that’s it": "Sì, è questo",
  "Cancel": "Annulla",
  "The goal could not be saved.": "Non sono riuscita a salvare l’obiettivo.",
  "The renewal could not be started.": "Non sono riuscita ad avviare il rinnovo.",

  // Goal draft (server messages)
  "What I understood": "Cosa ho capito",
  "I need one more thing": "Mi serve ancora una cosa",
  "Tell me how much: a percentage (“20%”) or an amount (“CHF 300k”).":
    "Dimmi quanto: una percentuale («20%») o una cifra («CHF 300k»).",
  "Example": "Esempio",
  "“I want to save 20% on IT contracts next year”": "«Voglio risparmiare il 20% sui contratti IT l’anno prossimo»",
  "Save {target} on {scope} · {period}.": "Risparmiare {target} su {scope} · {period}.",
  "{pct}% — {amount} a year": "{pct}% — {amount} l’anno",
  "{pct}%": "{pct}%",
  "{amount} a year": "{amount} l’anno",
  "{supplier}": "{supplier}",
  "FY {year}": "AF {year}",
  "this quarter": "questo trimestre",
  "IT contracts (software and cloud)": "contratti IT (software e cloud)",
  "all validated contracts": "tutti i contratti validati",
  "cloud contracts": "contratti cloud",
  "CRM contracts": "contratti CRM",
  "productivity contracts": "contratti di produttività",
  "Measured on": "Misurato su",
  "Counts as a result": "Conta come risultato",
  "How I help": "Come ti aiuto",
  "{spend} of annual spend · 1 validated contract": "{spend} di spesa annua · 1 contratto validato",
  "{spend} of annual spend · {n} validated contracts": "{spend} di spesa annua · {n} contratti validati",
  "no validated contract in this scope — it stays a figure": "nessun contratto validato in questo ambito — resta una cifra",
  "only the signed price against the previous contract, same quantities — never an estimate":
    "solo il prezzo firmato rispetto al contratto precedente, stesse quantità — mai una stima",
  "I show the contracts in scope with their deadline and what each is worth if you hit the target; you set the per-case target in the renewal chat":
    "ti mostro i contratti dell’ambito con la loro scadenza e quanto vale ciascuno se raggiungi il target; l’obiettivo per pratica lo fissi nella chat del rinnovo",

  // Verso il tuo obiettivo
  "Towards your goal: {parts}": "Verso il tuo obiettivo: {parts}",
  "{label} · {spend} of annual spend on {n} contracts": "{label} · {spend} di spesa annua su {n} contratti",
  "Objective": "Obiettivo",
  "{pct}% of {spend} · {label}": "{pct}% di {spend} · {label}",
  "{spend} · {label}": "{spend} · {label}",
  "Signed so far": "Firmato finora",
  "{pct}% of the goal · signed price vs previous": "{pct}% dell’obiettivo · prezzo firmato vs precedente",
  "no contract signed in this scope": "nessun contratto firmato in questo ambito",
  "Objectives in the cases": "Obiettivi nelle pratiche",
  "targets you set in open renewals — not estimates": "target che hai fissato nei rinnovi aperti — non stime",
  "Still to cover": "Ancora da coprire",
  "with the renewals coming up below": "con i rinnovi in arrivo qui sotto",
  "covered, if the targets are reached": "coperto, se i target vengono raggiunti",
  "signed {amount}": "firmato {amount}",
  "objectives set in the cases {amount}": "obiettivi impostati nelle pratiche {amount}",
  "missing {amount}": "mancano {amount}",
  "goal covered by the targets": "obiettivo coperto dai target",
  "No validated contract in this scope.": "Nessun contratto validato in questo ambito.",
  "If the targets set in the cases are signed, the goal is covered. It counts only when the contract is signed.":
    "Se i target fissati nelle pratiche vengono firmati, l’obiettivo è coperto. Conta solo quando il contratto è firmato.",
  "To cover {gap} you need higher targets in the upcoming renewals: {pct}% on each contract below is worth {amount} a year. The target of each case is set at question 3 of the interview.":
    "Per coprire {gap} servono target più alti nei rinnovi in arrivo: il {pct}% su ciascun contratto qui sotto vale {amount} l’anno. Il target di ogni pratica si fissa alla domanda 3 dell’intervista.",
  "Contracts you can act on to get there · {n}": "Contratti su cui puoi agire per arrivarci · {n}",
  "Case open · objective {objective}": "Pratica aperta · obiettivo {objective}",
  "Renewal to start": "Rinnovo da avviare",
  "uplift {value}": "aumento {value}",
  "auto-renewal": "rinnovo automatico",
  "to set": "da fissare",
  "none⟦objective⟧": "nessuno",
  "custom⟦objective⟧": "personalizzato",
  "If you reach {pct}": "Se raggiungi il {pct}",
  "{n} d to notice": "{n} g alla disdetta",
  "Open the case": "Apri la pratica",
  "Start the renewal": "Avvia il rinnovo",
  "No validated contract in this scope: the goal stays a figure until you upload one.":
    "Nessun contratto validato in questo ambito: l’obiettivo resta una cifra finché non ne carichi uno.",

  // Headline numbers
  "Contractual results · FY {year}": "Risultati contrattuali · AF {year}",
  "signed price vs previous contract, same quantities · {n} contracts":
    "prezzo firmato vs contratto precedente, stesse quantità · {n} contratti",
  "Realized · from invoices": "Realizzati · da fatture",
  "a signed contract alone does not prove them": "un contratto firmato da solo non li prova",
  "Quantity reductions": "Riduzioni di quantità",
  "lower spend from cut quantities — shown apart": "spesa in meno per quantità tagliate — a parte",

  // KPI strip
  "Signed results": "Risultati firmati",
  "in the period, excluding quantity cuts": "nel periodo, esclusi i tagli di quantità",
  "Average per contract": "Media per contratto",
  "CHF / year": "CHF / anno",
  "Proven by invoices": "Provato da fatture",
  "{realized} of {contractual}": "{realized} su {contractual}",
  "Analysed spend": "Spesa analizzata",
  "{n} validated contracts": "{n} contratti validati",
  "Open objectives": "Obiettivi aperti",
  "{n} renewals · {m} without objective": "{n} rinnovi · {m} senza obiettivo",
  "Next notice deadline": "Prossima disdetta",
  "{n} d": "{n} g",

  // Charts
  "When they were signed · CHF / year": "Quando sono stati firmati · CHF / anno",
  "By supplier · signed, proven, objective": "Per fornitore · firmato, provato, obiettivo",
  "proven by invoices": "provato da fatture",
  "signed, not yet proven": "firmato, non ancora provato",
  "your objective": "obiettivo tuo",
  "By lever · signed results": "Da quale leva · risultati firmati",
  "Unit price": "Prezzo unitario",
  "Term⟦lever⟧": "Durata",
  "Clauses": "Clausole",
  "Other⟦lever⟧": "Altro",
  "1 contract": "1 contratto",
  "{n} contracts": "{n} contratti",
  "Quantity reductions are not included: they are shown apart.":
    "Le riduzioni di quantità non sono incluse: sono mostrate a parte.",
  "Proven by invoices · per signed result": "Provato da fatture · per risultato firmato",

  // Lists
  "· yours, from the renewal chat": "· tuoi, dalla chat del rinnovo",
  "No objective yet — set it in the case": "Nessun obiettivo ancora — si imposta nella pratica",
  "No target chosen": "Nessun target scelto",
  "Objective −{pct}% → CHF {amount}k / year if reached": "Obiettivo −{pct}% → CHF {amount}k / anno se raggiunto",
  "Custom objective": "Obiettivo personalizzato",
  "In negotiation": "In negoziazione",
  "To evaluate": "Da valutare",
  "· baseline · period · method · evidence": "· baseline · periodo · metodo · evidenza",
  "{amount} / year": "{amount} / anno",
  "Realized so far: {amount} · {evidence}": "Realizzato finora: {amount} · {evidence}",
  "Not yet proven by invoices": "Non ancora provato da fatture",
  "Quantity reduction — not a negotiation result": "Riduzione di quantità — non è un risultato negoziale",

  // Guida Raffa with a goal set
  "Open the first contract under “Contracts you can act on” and set the target in the case.":
    "Apri il primo contratto in «Contratti su cui puoi agire» e fissa il target nella pratica.",
  "Every target set in a case adds up here; it truly counts only at signature.":
    "Ogni target fissato in una pratica si somma qui; conta davvero solo alla firma.",
  "Open the first contract": "Apri il primo contratto",
};
