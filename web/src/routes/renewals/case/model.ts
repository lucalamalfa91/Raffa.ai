/**
 * Shared, pure building blocks of the renewal case engine: contract figures, clauses, permissions,
 * the 7-step stage, the interview definitions and the phase table. No state is read or written here;
 * `engine.ts`, `agenda.ts` and `view.ts` build on it.
 */

import { addDays, daysBetween } from "./dates";
import { B, chf, num, pick, tx, type Bilingual, type Lang } from "./text";
import type {
  Case,
  CaseContext,
  CardTable,
  ClauseSource,
  EmailKind,
  Permissions,
  Phase,
  Product,
  Responsibility,
} from "./types";

/* ---------- contract figures ---------- */

export interface Figures {
  product: Product;
  /** Current unit price of the lead product. */
  p0: number;
  qty: number;
  unit: string;
  /** Monthly units are billed twelve times a year. */
  mult: number;
}

export function figures(ctx: CaseContext): Figures {
  const product = ctx.detail.products[0];
  if (!product) throw new Error("A renewal case needs at least one product on the contract");
  return { product, p0: product.price, qty: product.qty, unit: product.unit, mult: /month/.test(product.unit) ? 12 : 1 };
}

/** "CHF 132 per user / month" for unit prices, "CHF 640,000 / yr" for commits. */
export function priceLabel(f: Figures, amount: number): string {
  return f.p0 < 1000 ? `CHF ${amount} per ${f.unit}` : `${chf(amount)} / yr`;
}

export function productLine(p: Product): string {
  return `${p.qty > 1 ? `${num(p.qty)} × ` : ""}${p.name}`;
}

export function productList(ctx: CaseContext): string {
  return ctx.detail.products.map(productLine).join(", ");
}

/* ---------- clauses ---------- */

export interface Clause extends ClauseSource {
  /** Stable key within the case: `p0…` for clauses to push on, `r0…` for ones to raise. */
  k: string;
  /** Policy / term key shared across contracts (`liab`, `payment`, `uplift`, `renew`, or `c_<name>`). */
  lk: string;
  stance: "push" | "raise";
}

export function clauseKey(type: string): string {
  if (/liab/i.test(type)) return "liab";
  if (/payment/i.test(type)) return "payment";
  if (/increase|uplift|protection/i.test(type)) return "uplift";
  if (/renew/i.test(type)) return "renew";
  return `c_${type.toLowerCase().replace(/\W+/g, "")}`;
}

export function clausesOf(ctx: CaseContext): Clause[] {
  return [
    ...ctx.detail.push.map((x, i): Clause => ({ ...x, k: `p${i}`, lk: clauseKey(x.type), stance: "push" })),
    ...ctx.detail.raise.map((x, i): Clause => ({ ...x, k: `r${i}`, lk: clauseKey(x.type), stance: "raise" })),
  ];
}

/** Company-policy rules (the sample "Add company policy" loads these). */
export const POLICY: Partial<Record<string, { bind: boolean; min?: number; text: Bilingual }>> = {
  liab: { bind: true, min: 24, text: B("Liability cap: at least 24 months of fees", "Limite di responsabilità: almeno 24 mesi di canone") },
  renew: { bind: true, text: B("No auto-renewal on software contracts above CHF 100k", "Niente rinnovo automatico sui contratti software oltre CHF 100k") },
  payment: { bind: false, text: B("Payment terms: quarterly or Net 60 preferred", "Pagamenti: preferibile trimestrale o Net 60") },
};

/** The supplier's liability position is below the binding company minimum (needs a recorded exception). */
export function liabilityConflict(c: Case): boolean {
  const min = POLICY.liab?.min;
  const offered = parseFloat(c.terms.liab?.val ?? "");
  return c.goals.policy && min !== undefined && Number.isFinite(offered) && offered < min;
}

/* ---------- permissions ---------- */

export const NO_APPROVE = B("Only the approver (CPO) or an explicit delegate can approve emails.", "Solo l’approvatore (CPO) o un delegato esplicito può approvare le email.");
export const NO_CLOSE = B("Only the approver (CPO), or a delegate with closing rights, can confirm this.", "Solo l’approvatore (CPO), o un delegato con diritto di chiusura, può confermarlo.");
export const NO_WORK = B("Only people working on this renewal can do this.", "Solo chi lavora a questo rinnovo può farlo.");
export const NO_DELEGATE = B("Only the approver (CPO) manages delegations.", "Solo l’approvatore (CPO) gestisce le deleghe.");
export const NO_REASSIGN = B("Only the approver (CPO) can reassign.", "Solo l’approvatore (CPO) può riassegnare.");

/** The strongest responsibility `who` holds on this case (a person can hold several). */
export function responsibilityOf(c: Case, who: string): Responsibility {
  if (who === c.approver) return "approver";
  if (who === c.owner) return "owner";
  if (c.delegates[who]) return "delegate";
  return "collab";
}

export function canApprove(c: Case, who: string): boolean {
  return who === c.approver || !!c.delegates[who];
}

export function canWork(c: Case, who: string): boolean {
  return who === c.owner || who === c.approver || !!c.delegates[who];
}

export function canClose(c: Case, who: string): boolean {
  return who === c.approver || !!c.delegates[who]?.close;
}

export function permissionsOf(c: Case, who: string): Permissions {
  return { approve: canApprove(c, who), work: canWork(c, who), close: canClose(c, who), manageTeam: who === c.approver };
}

/* ---------- steps and phases ---------- */

export const STEPS: readonly Bilingual[] = [
  B("Start", "Avvio"),
  B("Plan", "Piano"),
  B("Negotiation", "Negoziazione"),
  B("Agreement", "Accordo"),
  B("Draft", "Bozza"),
  B("Signature", "Firma"),
  B("Acquisition", "Acquisizione"),
];

/** Index (0–6) of the wizard step the case is in. */
export function stage(c: Pick<Case, "phase" | "agreed">): number {
  const p = c.phase;
  if (p === "interview") return 0;
  if (p === "plan") return 1;
  if (p === "email" || p === "waiting_rem" || p === "waiting" || p === "waiting_due" || p === "review") return c.agreed ? 4 : 2;
  if (p === "agree") return 3;
  if (p === "doc_wait" || p === "doc_check") return 4;
  if (p.startsWith("sign")) return 5;
  return 6;
}

export type NextActor = "you" | "owner" | "supplier" | "approver" | "none";

export interface PhaseInfo {
  /** Short status for lists and tags. */
  short: Bilingual;
  /** Status line in the case header. */
  status: Bilingual;
  /** What happens next. */
  next: Bilingual;
  who: NextActor;
}

export const PHASES: Record<Phase, PhaseInfo> = {
  interview: { short: B("Interview", "Intervista"), status: B("Interview", "Intervista"), next: B("Answer the questions", "Rispondi alle domande"), who: "you" },
  plan: { short: B("Plan ready", "Piano pronto"), status: B("Plan to confirm", "Piano da confermare"), next: B("Confirm or edit the plan", "Conferma o modifica il piano"), who: "owner" },
  email: { short: B("Email to approve", "Email da approvare"), status: B("Email to approve and send", "Email da approvare e inviare"), next: B("Approve, copy, send and confirm", "Approva, copia, invia e conferma"), who: "owner" },
  waiting_rem: { short: B("Email to approve", "Email da approvare"), status: B("Sent — choose a check date", "Inviata — scegli la data di controllo"), next: B("Confirm the check date", "Conferma la data di controllo"), who: "owner" },
  waiting: { short: B("Waiting for supplier", "In attesa del fornitore"), status: B("Waiting for the supplier", "In attesa del fornitore"), next: B("Supplier reply", "Risposta del fornitore"), who: "supplier" },
  waiting_due: { short: B("Reply to register", "Risposta da registrare"), status: B("Check date reached", "Data di controllo raggiunta"), next: B("Paste the reply or prepare a follow-up", "Incolla la risposta o prepara un sollecito"), who: "owner" },
  review: { short: B("Reply to review", "Risposta da rivedere"), status: B("Reply analysed", "Risposta analizzata"), next: B("Decide", "Decidi"), who: "owner" },
  agree: { short: B("Terms to confirm", "Condizioni da confermare"), status: B("Terms proposed", "Condizioni proposte"), next: B("Confirm as agreed", "Conferma come concordate"), who: "owner" },
  doc_wait: { short: B("Waiting for contract draft", "In attesa della bozza"), status: B("Terms agreed — waiting for the draft", "Condizioni concordate — attesa bozza"), next: B("Draft contract from supplier", "Bozza di contratto dal fornitore"), who: "supplier" },
  doc_check: { short: B("Checking draft", "Verifica bozza"), status: B("Draft under check", "Bozza in verifica"), next: B("Resolve differences", "Risolvi le differenze"), who: "owner" },
  sign_wait: { short: B("Waiting for signature", "In attesa di firma"), status: B("Ready to sign — signing is outside Raffa", "Pronta alla firma — la firma è fuori da Raffa"), next: B("Sign, then add the signed file", "Firma, poi aggiungi il file firmato"), who: "owner" },
  sign_declared: { short: B("Signature declared", "Firma dichiarata"), status: B("Signature declared — document to acquire", "Firma dichiarata — documento da acquisire"), next: B("Add the signed file", "Aggiungi il file firmato"), who: "owner" },
  sign_confirm: { short: B("Signed · to confirm", "Firmato · da confermare"), status: B("Signed file received", "File firmato ricevuto"), next: B("Confirm it is the final signed version", "Conferma che è la versione firmata definitiva"), who: "owner" },
  pipeline: { short: B("Signed · in review", "Firmato · in revisione"), status: B("Negotiation completed · new contract in review", "Negoziazione completata · nuovo contratto in revisione"), next: B("Resolve flagged fields", "Risolvi i campi segnalati"), who: "owner" },
  done: { short: B("Renewed", "Rinnovato"), status: B("Completed — contract signed", "Completata — contratto firmato"), next: B("Nothing open", "Niente di aperto"), who: "none" },
  notice_out: { short: B("Not renewed", "Non rinnovato"), status: B("Not renewed / notice", "Non rinnovato / disdetta"), next: B("Exit tasks", "Attività di uscita"), who: "owner" },
};

export function shortStatus(c: Case, lang: Lang = "en"): string {
  const S = pick(lang);
  if (c.outcome?.kind === "terminated") return S("Not renewed", "Non rinnovato");
  if (c.notice?.state === "sent") return S("Notice sent · outcome to verify", "Disdetta inviata · esito da verificare");
  if (c.alt?.state === "selected" && !c.outcome) return S("Replacement in progress", "Sostituzione in corso");
  return tx((PHASES[c.phase] ?? PHASES.email).short, lang);
}

export const EMAIL_KIND: Record<EmailKind, { noun: Bilingual; title: Bilingual }> = {
  first: { noun: B("first email", "prima email"), title: B("First email", "Prima email") },
  followup: { noun: B("follow-up", "sollecito"), title: B("Follow-up", "Sollecito") },
  counter: { noun: B("counter-proposal", "controproposta"), title: B("Counter-proposal", "Controproposta") },
  docreq: { noun: B("contract request", "richiesta contratto"), title: B("Contract request", "Richiesta contratto") },
  fix: { noun: B("correction email", "email correttiva"), title: B("Correction email", "Email correttiva") },
  notice: { noun: B("non-renewal notice", "disdetta"), title: B("Non-renewal notice", "Disdetta") },
  rfq: { noun: B("request for proposal", "richiesta di offerta"), title: B("Request for proposal", "Richiesta di offerta") },
};

/** What to paste or upload at each step -- shown under the step's checklist. */
export const EXAMPLES: readonly Bilingual[] = [
  B("Example: “Keep everything, but reduce CPQ to 40 seats.”", "Esempio: «Mantieni tutto, ma riduci CPQ a 40 licenze.»"),
  B("Read the plan: confirm it, or press Edit to change an answer.", "Leggi il piano: confermalo, oppure premi Modifica per cambiare una risposta."),
  B("Paste the supplier email as it is — the whole thread is fine. E.g. “Hi Luca, for a 24-month renewal we can offer…”", "Incolla l’email del fornitore così com’è — va bene anche tutta la catena. Es. «Hi Luca, for a 24-month renewal we can offer…»"),
  B("Confirm only what the supplier wrote. Silence is not acceptance.", "Conferma solo ciò che il fornitore ha scritto. Il silenzio non è accettazione."),
  B("Add the PDF the supplier sent: Order Form, amendment or redline.", "Aggiungi il PDF ricevuto dal fornitore: Order Form, emendamento o redline."),
  B("Add the signed PDF (a scan is fine). The file name alone is not proof.", "Aggiungi il PDF firmato (va bene una scansione). Il nome del file da solo non è una prova."),
  B("Decide the fields below 80% — the same review as in Documents.", "Decidi i campi sotto l’80% — la stessa revisione di Documenti."),
];

/* ---------- interview ---------- */

export interface QuestionDef {
  text: string;
  hint: string;
  /** `[value, label]` pairs. */
  opts: Array<[string, string]>;
  multi?: boolean;
}

export function questionDef(i: number, ctx: CaseContext): QuestionDef {
  const S = pick(ctx.lang);
  const f = figures(ctx);
  const C = ctx.contract;
  switch (i) {
    case 0:
      return {
        text: S("First: what do you want to do with the products and quantities?", "Prima cosa: che cosa vuoi fare con prodotti e quantità?"),
        hint: S(
          "The contract has no usage data, so I cannot say how many licences are unused. Tell me if you know — I will not guess.",
          "Il contratto non contiene dati d’uso, quindi non posso dire quante licenze siano inutilizzate. Dimmelo se lo sai — non lo indovino.",
        ),
        opts: [
          ["keep", S("Keep everything as today", "Mantieni tutto come oggi")],
          ["reduce", S("Reduce quantities", "Riduci le quantità")],
          ["add", S("Add products", "Aggiungi prodotti")],
        ],
      };
    case 1:
      return {
        text: S("Term: how long should the renewed contract run?", "Durata: quanto deve durare il contratto rinnovato?"),
        hint: S(
          `Current cycle ends ${C.end}; the new term would start on ${addDays(C.end, 1)}.`,
          `Il ciclo attuale termina il ${C.end}; il nuovo periodo partirebbe il ${addDays(C.end, 1)}.`,
        ),
        opts: [
          ["12", S("12 months", "12 mesi")],
          ["24", S("24 months", "24 mesi")],
          ["36", S("36 months", "36 mesi")],
        ],
      };
    case 2:
      return {
        text: S("Economic goal: do you have a price or budget target?", "Obiettivo economico: hai un target di prezzo o di budget?"),
        hint: S(
          `“No target for now” is a valid answer. I compare only with your current contract price (${priceLabel(f, f.p0)}) and offers you receive — I have no reliable market benchmark.`,
          `«Nessun target per ora» è una risposta valida. Confronto solo con il prezzo del tuo contratto attuale (${priceLabel(f, f.p0)}) e con le offerte che ricevi — non ho benchmark di mercato affidabili.`,
        ),
        opts: [
          ["none", S("No target for now", "Nessun target per ora")],
          ["p5", S("−5% on unit price", "−5% sul prezzo unitario")],
          ["p10", S("−10% on unit price", "−10% sul prezzo unitario")],
          ["budget", S(`Budget: ${chf(C.spend * 0.9)} / yr (−10%)`, `Budget: ${chf(C.spend * 0.9)} / anno (−10%)`)],
        ],
      };
    default:
      return {
        text: S("Last: which clauses do you want to change?", "Ultima: quali clausole vuoi cambiare?"),
        hint: S(
          "Pre-selected are the ones I found costing you money or freedom. Add or remove, or upload a company policy.",
          "Ho preselezionato quelle che ti costano denaro o libertà. Aggiungi o togli, oppure carica una policy aziendale.",
        ),
        multi: true,
        opts: clausesOf(ctx).map((x): [string, string] => [x.k, x.type]),
      };
  }
}

/** The label of what the user answered to question `i` (shown in the plan and in the history). */
export function answerLabel(c: Case, ctx: CaseContext, i: number): string {
  const S = pick(ctx.lang);
  const key = (["prod", "term", "target", "clauses"] as const)[i];
  if (i === 3) {
    const picked = c.goals.clauses ?? [];
    const names = clausesOf(ctx)
      .filter((x) => picked.includes(x.k))
      .map((x) => x.type)
      .concat(c.goals.clausesText ? [c.goals.clausesText] : []);
    return names.length ? names.join(", ") : S("none for now", "nessuna per ora");
  }
  const value = c.goals[key as "prod" | "term" | "target"];
  if (value === "custom") return (c.goals[`${key as "prod" | "term" | "target"}Text`] as string | undefined) ?? "";
  const option = questionDef(i, ctx).opts.find((o) => o[0] === String(value));
  return option ? option[1] : "";
}

/* ---------- "what to expect" table ---------- */

const OUTLOOK: Array<[RegExp, Bilingual, Bilingual]> = [
  [/price increase|uplift|price protection/i, B("Increase tied to CPI with a cap of 3–5%, or fixed for the term", "Aumento legato all’indice prezzi con tetto 3–5%, oppure bloccato per la durata"), B("Likely — suppliers usually concede a cap or a freeze in exchange for a longer term.", "Probabile — di solito il fornitore concede un tetto o il blocco in cambio di una durata più lunga.")],
  [/auto-renew/i, B("Renewal by written agreement, or at least 60–90 days notice before term end", "Rinnovo con accordo scritto, oppure preavviso di almeno 60–90 giorni prima della scadenza"), B("Likely — easy to win if you open the talks before the notice date.", "Probabile — facile da ottenere se apri la trattativa prima della data di disdetta.")],
  [/liab/i, B("Cap at 12–24 months of fees, with a separate higher cap for data-breach claims", "Tetto a 12–24 mesi di canone, con un tetto più alto separato per violazioni dei dati"), B("Possible — large vendors rarely move on standard paper; a data-breach carve-out is the realistic ask.", "Possibile — i grandi fornitori cedono di rado sul contratto standard; la richiesta realistica è l’esclusione per violazione dati.")],
  [/terminat/i, B("Exit at convenience is uncommon on committed terms; it usually comes with a fee", "Recesso libero raro sui contratti vincolati; di solito ha un costo"), B("Difficult — expect a trade: exit option against a higher unit price.", "Difficile — aspettati uno scambio: opzione di uscita contro un prezzo unitario più alto.")],
  [/payment/i, B("Annual in advance is common; quarterly or Net 45–60 is often accepted", "Pagamento annuale anticipato diffuso; trimestrale o Net 45–60 spesso accettati"), B("Possible — often granted, sometimes with a small price premium.", "Possibile — spesso concesso, a volte con un piccolo sovrapprezzo.")],
  [/credit/i, B("Credits capped at 10–25% of monthly fees; an exit for chronic failure is common in enterprise terms", "Crediti con tetto 10–25% del canone mensile; uscita per disservizi cronici comune nei contratti enterprise"), B("Possible — worth asking if uptime matters to you.", "Possibile — vale la pena chiederlo se la continuità del servizio è critica.")],
  [/commit|envelope|carry|roll|unused/i, B("Carry-forward of 10–20% of unused volume is a common concession at renewal", "Riporto del 10–20% del volume non usato: concessione comune al rinnovo"), B("Possible — vendors prefer to grant it when you renew or grow the commit.", "Possibile — il fornitore lo concede più volentieri se rinnovi o aumenti l’impegno.")],
  [/reassign/i, B("Moving licences between users within 30 days is common", "Riassegnare le licenze tra utenti entro 30 giorni è comune"), B("Likely — low cost for the vendor.", "Probabile — costo basso per il fornitore.")],
  [/support/i, B("Support priced as a percentage of spend, with lower tiers above a volume threshold", "Supporto a percentuale sulla spesa, con scaglioni più bassi oltre una soglia"), B("Possible — tiered rates come up as spend grows.", "Possibile — gli scaglioni emergono quando la spesa cresce.")],
];

const OUTLOOK_FALLBACK: [Bilingual, Bilingual] = [
  B("Usually negotiable at renewal, especially when tied to term or volume", "Di solito negoziabile al rinnovo, soprattutto se legata a durata o volume"),
  B("Depends on your leverage.", "Dipende dalla tua forza negoziale."),
];

export function outlookTable(ctx: CaseContext): CardTable {
  const lang = ctx.lang;
  const S = pick(lang);
  return {
    layout: [
      { min: 110, fr: 0.8 },
      { min: 0, fr: 1.3 },
      { min: 0, fr: 1.3 },
      { min: 0, fr: 1.5 },
    ],
    cols: S(["Clause", "In your contract", "Typical practice", "What to expect"], ["Clausola", "Nel contratto", "Prassi tipica", "Cosa aspettarti"]),
    rows: clausesOf(ctx).map((x) => {
      const hit = OUTLOOK.find((o) => o[0].test(x.type));
      const [practice, expect] = hit ? [hit[1], hit[2]] : OUTLOOK_FALLBACK;
      return [
        { v: x.type, w: 700 as const },
        { v: `${x.norm} · ${x.src}` },
        { v: tx(practice, lang) },
        { v: `${tx(expect, lang)} ${S("Your ask: ", "Tua richiesta: ")}${x.ask}`, w: 600 as const },
      ];
    }),
  };
}

/* ---------- misc ---------- */

export function daysToDate(ctx: CaseContext, date: string): number {
  return daysBetween(ctx.today, date);
}
