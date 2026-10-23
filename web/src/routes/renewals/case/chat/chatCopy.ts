/**
 * Interface copy of the renewal chat, EN/IT. Step 7 of the rollout moves these into the app's i18n
 * catalogue; until then every string lives here, in both languages, in one place.
 */

import { B, tx, type Lang } from "../text";

const COPY = {
  stepsSoFar: B("Steps so far", "Step finora"),
  viewStep: B("View →", "Apri →"),
  stepHistory: B("Step history", "Storico dello step"),
  close: B("Close", "Chiudi"),
  next: B("Next", "Poi"),
  noticeBy: B("Notice by", "Disdetta entro"),
  ends: B("Ends", "Scade"),
  owner: B("Owner", "Responsabile"),
  approver: B("Approver", "Approvatore"),
  you: B("you", "tu"),
  days: B("days", "giorni"),
  history: B("History →", "Cronologia →"),
  team: B("Team & delegations", "Team e deleghe"),
  newChat: B("+ New chat", "+ Nuova chat"),
  stepper: B("Renewal steps", "Passi del rinnovo"),
  makeDelegate: B("Make delegate", "Rendi delegato"),
  removeDelegation: B("Remove delegation", "Rimuovi delega"),
  allowClosing: B("Allow closing", "Consenti chiusura"),
  removeClosing: B("Remove closing right", "Togli diritto di chiusura"),
  reassign: B("Reassign ownership", "Riassegna responsabilità"),
  teamNote: B(
    "Delegates are people already allowed in this workspace. The chat never widens access to documents.",
    "I delegati sono persone già autorizzate nel workspace. La chat non allarga mai l’accesso ai documenti.",
  ),
  sinceTitle: B("Since your last visit", "Dalla tua ultima visita"),
  sinceOk: B("Got it", "Ok"),
  to: B("To", "A"),
  subject: B("Subject", "Oggetto"),
  recipient: B("Recipient — not in the contract", "Destinatario — non presente nel contratto"),
  accept: B("Accept", "Accetta"),
  correct: B("Correct", "Correggi"),
  correctedValue: B("Corrected value", "Valore corretto"),
  composerLabel: B("Message", "Messaggio"),
  composerPlaceholder: B("Answer, add a comment, or type approve / copy / I sent it…", "Rispondi, aggiungi un commento, o scrivi approva / copia / ho inviato…"),
  send: B("Send", "Invia"),
  pasteReply: B("Paste the supplier reply", "Incolla la risposta del fornitore"),
  pasteReplyHint: B("From: {from} · date: today — suggested from the thread.", "Da: {from} · data: oggi — suggerita dalla catena."),
  pastePlaceholder: B("Paste text or the whole thread — duplicates are detected", "Incolla il testo o l’intera catena — i duplicati vengono rilevati"),
  addReply: B("Add reply to the case", "Aggiungi la risposta al caso"),
  sampleReply: B("Use sample reply", "Usa risposta di esempio"),
  cancel: B("Cancel", "Annulla"),
  vendorThread: B("Vendor thread", "Filone fornitore"),
  addOfferFrom: B("Add offer from {vendor} (sample)", "Aggiungi offerta di {vendor} (esempio)"),
  demo: B("Demo", "Demo"),
  notFoundTitle: B("This renewal is not available", "Questo rinnovo non è disponibile"),
  notFoundBody: B("No renewal case exists for this contract.", "Non esiste un caso di rinnovo per questo contratto."),
  backToRenewals: B("Back to Renewals", "Torna a Renewals"),
  items: B("{n} items", "{n} elementi"),
  item: B("1 item", "1 elemento"),
} as const;

export type CopyKey = keyof typeof COPY;

export type Translate = (key: CopyKey, vars?: Record<string, string | number>) => string;

export function translator(lang: Lang): Translate {
  return (key, vars) => {
    const text = tx(COPY[key], lang);
    return vars ? text.replace(/\{(\w+)\}/g, (_, name: string) => String(vars[name] ?? "")) : text;
  };
}
