/**
 * Types for the renewal case engine (`engine.ts`).
 *
 * `Case` is plain JSON: the backend (or a persisted store) owns it, the client calls
 * `act(case, type, payload, ctx)` and gets a new `Case` back. Everything the engine needs from outside
 * the case -- who is looking, the contract's facts, today's date and the scripted services -- travels
 * in `CaseContext`.
 */

import type { CaseDate } from "./dates";
import type { CaseServices } from "./services";
import type { Lang, Msg, Text } from "../../../i18n/translate";

/* ---------- responsibilities ---------- */

/**
 * Workspace roles (`admin | procurement`) say what a person may do in the app. These four say what
 * they answer for on *one* renewal, and are assigned per case: the approver (CPO) approves, closes,
 * delegates and reassigns; the owner works the case; a delegate approves (and optionally closes);
 * everybody else on the case is a collaborator (read / comment).
 */
export type Responsibility = "approver" | "owner" | "delegate" | "collab";

export interface Permissions {
  approve: boolean;
  work: boolean;
  close: boolean;
  /** Delegations and reassignment: the approver only. */
  manageTeam: boolean;
}

/* ---------- contract inputs (from the API in the real app) ---------- */

export interface Product {
  name: string;
  /** "user / month", "flat / year", "commit / year" … -- a unit containing "month" multiplies by 12. */
  unit: string;
  qty: number;
  price: number;
}

/** One clause the case can push on (`push`) or raise (`raise`), with the page it came from. */
export interface ClauseSource {
  type: string;
  norm: string;
  ask: string;
  src: string;
}

export interface ContractFacts {
  supplier: string;
  name: string;
  file: string;
  spend: number;
  start: CaseDate;
  end: CaseDate;
  cancel: CaseDate;
  cancelDays: number;
  notice: number;
  uplift: string;
  /** Renews by itself for a term if nobody gives notice. */
  autoRenews: boolean;
  /** Why this contract needs attention now -- shown while it has no renewal case yet. */
  rationale: string;
}

export interface ContractDetail {
  products: Product[];
  push: ClauseSource[];
  raise: ClauseSource[];
  terms: Array<{ label: string; value: string }>;
  /** Obligations on the supplier (exit tasks listed on a non-renewal). */
  obligationsSupplier: Array<{ text: string; when: string }>;
}

export interface CaseContext {
  lang: Lang;
  /** Who is acting / looking. Permissions are derived from this name and the case's assignments. */
  viewer: string;
  /** "Today" in case-date format. Injected so the engine never reads a clock. */
  today: CaseDate;
  /** The buyer's own company (workspace name): signs the emails Raffa drafts. */
  company: string;
  contract: ContractFacts;
  detail: ContractDetail;
  services: CaseServices;
}

/* ---------- case state ---------- */

export type Phase =
  | "interview"
  | "plan"
  | "email"
  | "waiting_rem"
  | "waiting"
  | "waiting_due"
  | "review"
  | "agree"
  | "doc_wait"
  | "doc_check"
  | "sign_wait"
  | "sign_declared"
  | "sign_confirm"
  | "pipeline"
  | "done"
  | "notice_out";

export type EmailKind = "first" | "followup" | "counter" | "docreq" | "fix" | "notice" | "rfq";
export type EmailState = "draft" | "approved" | "copied" | "sent" | "superseded";

export interface SentDeclaration {
  by: string;
  at: string;
  to: string;
  edited: boolean;
  text: string | null;
}

export interface Email {
  id: number;
  kind: EmailKind;
  ver: number;
  to: string;
  subject: string;
  body: string;
  attach: string[];
  state: EmailState;
  approvedBy: string | null;
  approvedVer?: number;
  sent: SentDeclaration | null;
  thread: string | null;
  vendor: string | null;
}

export type ReminderState = "proposed" | "set" | "due" | "cancelled";
export interface Reminder {
  rid: number;
  date: CaseDate;
  state: ReminderState;
}

export type TermStatus = "open" | "proposed" | "agreed" | "rejected" | "unclear";
export interface Term {
  label: Text;
  state: TermStatus;
  /** True when the term comes from the user's plan (as opposed to one the supplier raised). */
  plan: boolean;
  val?: string | null;
  /** Supplier reply number that is the evidence for the current state. */
  ev?: number | null;
}

export interface ReplyRow {
  label: string;
  said: string;
  plan: string;
  state: "proposed" | "unclear" | "rejected" | "conflict";
  warn: string;
}

export interface Reply {
  rid: number;
  round: number;
  text: string;
  by: string;
  at: string;
  rows: ReplyRow[];
}

export interface Limits {
  term: number;
  priceMax?: number;
  exit?: number;
}

export interface Decision {
  did: number;
  state: "open" | "agree" | "done";
  opt?: "a" | "b" | "c";
  by?: string;
  label?: Msg;
}

export interface Agreed {
  price: number;
  term: number;
  cap: number;
  liab: number;
  auth?: string;
}

export type DraftResult = "ok" | "diff" | "unread" | "manual";
export interface DraftRow {
  label: string;
  agreed: string;
  draft: string;
  res: DraftResult;
  src: string;
}
export interface DraftDoc {
  v: number;
  file: string;
  by: string;
  at: string;
  rows: DraftRow[];
  manual: boolean;
}

export interface SignedFile {
  file: string;
  by: string;
  at: string;
  confirmedBy?: string;
}

export interface WeakField {
  n: string;
  name: Msg;
  val: string;
  conf: number;
  state: "open" | "accepted" | "corrected";
}
export interface Pipe {
  /** 0 upload · 1 processing · 2 extraction · 3 review · 4 validation */
  stage: number;
  weak: WeakField[];
  facts: { total: number; aboveThreshold: number };
}

export type Outcome =
  | {
      kind: "renewed";
      start: CaseDate;
      end: CaseDate;
      cancel: CaseDate;
      cancelDays: number;
      spend: number;
      delta: number;
      /** New unit price, e.g. "CHF 123 per user / month". */
      price: string;
      /** Unit price of the contract it replaces. */
      was: number;
      file: string;
    }
  | { kind: "terminated"; end: CaseDate; when: CaseDate; replacedBy?: string };

export interface Notice {
  state: "check" | "email" | "sent" | "out";
  sentAt: CaseDate | null;
  evidence: boolean;
  evFile?: string;
  by?: string;
}

export interface AltOffer {
  price: number;
  total: number;
  term: number;
  cover: string;
  mig: string;
  gap: string;
  by: string;
}
export interface Alt {
  state: "req" | "cand" | "rfq" | "compare" | "selected";
  reqs: string[];
  vendors: string[];
  /** Keyed by vendor name; `"<vendor>_rfq": true` marks an RFQ email already drafted. */
  offers: Record<string, AltOffer | true>;
  cmp?: boolean;
  chosen?: string;
  exitDone?: boolean;
  newDone?: boolean;
  newFile?: string;
}

export interface Goals {
  prod: string | null;
  term: string | null;
  /** `undefined` = not answered yet ("no target" is the explicit answer `"none"`). */
  target?: string;
  clauses: string[] | null;
  policy: boolean;
  prodText?: string;
  termText?: string;
  targetText?: string;
  clausesText?: string;
}

export interface DecisionLogEntry {
  by: string;
  at: CaseDate;
  text: Text;
}

export type FeedKind =
  | "say"
  | "user"
  | "q"
  | "outlook"
  | "policy"
  | "plan"
  | "email"
  | "rem"
  | "reply"
  | "ana"
  | "dec"
  | "agree"
  | "doc"
  | "signwait"
  | "sig"
  | "pipe"
  | "out"
  | "ncheck"
  | "nrec"
  | "nout"
  | "areq"
  | "acand"
  | "aoff"
  | "acmp"
  | "asel"
  | "asel2";

export interface FeedItem {
  id: string;
  k: FeedKind;
  /** Stage (0–6) the case was in when the item was posted -- groups history rows in the chat. */
  st: number;
  at: string;
  by: string;
  text?: Text;
  tag?: Msg;
  i?: number;
  ver?: number;
  eid?: number;
  rid?: number;
  did?: number;
  v?: number;
  vendor?: string;
  thread?: string | null;
}

/** UI-only state that lives on the case so it survives a reload: open editors, typed-but-unsent text. */
export interface UiState {
  warn?: string;
  since?: { n: number; items: Array<{ by: string; k: FeedKind }> } | null;
  edit?: number | null;
  editBody?: string;
  sentOpen?: number | null;
  sentDate?: string;
  sentEdited?: boolean;
  sentText?: string;
  remDate?: string;
  replyOpen?: boolean;
  replyText?: string;
  auth?: string;
  noEv?: boolean;
  altName?: string;
  altReq?: string[];
  [key: string]: unknown;
}

export interface Case {
  cid: string;
  owner: string;
  approver: string;
  updated: CaseDate;
  seen: Record<string, number>;
  delegates: Record<string, { close: boolean }>;
  members: string[];
  feed: FeedItem[];
  seq: number;
  phase: Phase;
  /** Interview question currently open (0–3). */
  q: number;
  goals: Goals;
  planVer: number;
  planOk: boolean;
  emails: Email[];
  rems: Reminder[];
  reps: Reply[];
  round: number;
  terms: Record<string, Term>;
  limits: Limits | null;
  decs: DecisionLogEntry[];
  qs: Decision[];
  agreed: Agreed | null;
  docs: DraftDoc[];
  signed: SignedFile | null;
  pipe: Pipe | null;
  outcome: Outcome | null;
  notice: Notice | null;
  alt: Alt | null;
  thread: string | null;
  ui: UiState;
  history: Array<{ who: string; at: CaseDate }>;
  /** The replacement vendor's contract entered the extraction pipeline. */
  pipeNew?: boolean;
  /** Text the last "Copy" put on the clipboard -- the UI writes it out and clears it. */
  clip?: string;
  /** The extraction pipeline has another simulated tick to run. */
  tick?: boolean;
}

/* ---------- actions ---------- */

export type ActionType =
  | "ui"
  | "visit"
  | "sinceOk"
  | "ans"
  | "toggle"
  | "policy"
  | "q3done"
  | "editPlan"
  | "confirmPlan"
  | "approve"
  | "edit"
  | "cancelEdit"
  | "saveEdit"
  | "copy"
  | "sentOpen"
  | "sentCancel"
  | "sentOk"
  | "toEx"
  | "remOk"
  | "remChange"
  | "ff"
  | "followup"
  | "openReply"
  | "closeReply"
  | "sampleReply"
  | "reply"
  | "decide"
  | "agreeOk"
  | "addDoc"
  | "fixEmail"
  | "markAnnex"
  | "signDeclared"
  | "sigOk"
  | "tick"
  | "acceptF"
  | "correctF"
  | "validate"
  | "startNotice"
  | "nStart"
  | "nReceipt"
  | "nOut"
  | "startAlt"
  | "altReqs"
  | "altAdd"
  | "altEx"
  | "altRfq"
  | "thread"
  | "altOffer"
  | "altSel"
  | "altNew"
  | "delegate"
  | "delegateClose"
  | "transfer"
  | "say";

export interface ActPayload {
  /** Interview: question index (`ans`) or clause key (`toggle`) or UI field (`ui`). */
  q?: number;
  k?: string;
  val?: string;
  v?: string | number | boolean;
  eid?: number;
  rid?: number;
  did?: number;
  opt?: "a" | "b" | "c";
  i?: number;
  vendor?: string;
  who?: string;
  on?: boolean;
  text?: string;
  t?: string;
}

/* ---------- view model ---------- */

export type Tone = "alert" | "muted";

export interface CardButton {
  label: string;
  act: ActionType | "nav";
  p: ActPayload & { to?: string };
  primary: boolean;
  /** Not allowed right now; `why` says why and is shown next to the disabled button. */
  off: boolean;
  why: string;
}

export interface CardChip {
  label: string;
  act: ActionType;
  p: ActPayload;
  on: boolean;
  off?: boolean;
}

export interface CardInput {
  label: string;
  value: string | boolean;
  /** Key passed to `act("ui", { k, v })`. */
  ui: string;
  type: "text" | "check" | "area";
}

export interface CardRow {
  k: string;
  v: string;
  w?: 400 | 600 | 700;
  tone?: Tone;
}

export interface TableCell {
  v: string;
  w?: 400 | 600 | 700;
  tone?: Tone;
}

export interface CardTable {
  cols: string[];
  /** Per-column width hint: `minmax(min px, fr fr)`. */
  layout: Array<{ min: number; fr: number }>;
  rows: TableCell[][];
}

export interface CardStep {
  label: string;
  on: boolean;
  now: boolean;
}

export interface CardField {
  name: string;
  val: string;
  conf: string;
  state: WeakField["state"];
  open: boolean;
  doneLabel: string;
  /** `ui` key holding the corrected value. */
  inKey: string;
  inVal: string;
  i: number;
}

export interface CardEmail {
  eid: number;
  to: string;
  toMissing: boolean;
  /** `ui` key to edit the recipient. */
  toKey: string;
  state: string;
  body: string;
  attach: string[];
  editing: boolean;
  editBody: string;
  showBody: boolean;
  sentNote: string;
  attachNote: string;
}

export interface Card {
  id: string;
  k: FeedKind;
  st: number;
  by: string;
  at: string;
  /** A plain chat bubble (Raffa message or the user's own words) rather than a step card. */
  plain: boolean;
  text: string;
  kicker: string;
  title: string;
  lines: string[];
  rows: CardRow[];
  table: CardTable | null;
  chips: CardChip[];
  buttons: CardButton[];
  inputs: CardInput[];
  email: CardEmail | null;
  fields: CardField[];
  steps: CardStep[];
  note: string;
  pre: string;
  tag: string;
  tone: "" | "wait";
  /** The card the user acts on now. */
  current: boolean;
  /** Superseded or answered: shown muted. */
  dim: boolean;
  banner: string;
  /** Outlook card stays pinned at the top while the interview/plan is open. */
  pin?: boolean;
}

export interface ChecklistItem {
  done: boolean;
  label: string;
  who: string;
}

export type TodoKind =
  | "recipient"
  | "approve"
  | "send"
  | "plan"
  | "remind"
  | "reply"
  | "decide"
  | "policy"
  | "agree"
  | "draft"
  | "sign"
  | "sigok"
  | "fields"
  | "notice"
  | "nout"
  | "alt"
  /** Raised by the portfolio, not the engine: a notice deadline running with no case started. */
  | "unassigned";

export interface TodoItem {
  kind: TodoKind;
  who: string;
  title: string;
  why: string;
  due: CaseDate;
  dueDays: number;
  stage: number;
  stageLabel: string;
  ref: string;
  /** Days since the case last moved, when that is 7 or more; otherwise 0. */
  stalled: number;
}

export interface CasePerson {
  name: string;
  role: string;
  responsibility: Responsibility;
  isOwner: boolean;
  isDelegate: boolean;
  canClose: boolean;
  viewing: boolean;
}

export interface CaseHeader {
  title: string;
  supplier: string;
  contract: string;
  status: string;
  next: string;
  approver: string;
  noticeBy: CaseDate;
  noticeDays: number;
  end: string;
  owner: string;
  tracks: string[];
  warn: string;
  people: CasePerson[];
  youRole: string;
}

export interface TermView {
  label: string;
  val: string;
  state: string;
  status: TermStatus;
}

export interface TimelineEntry {
  at: string;
  time: string;
  by: string;
  label: string;
  text: string;
  mine: boolean;
}

export interface CaseView {
  outlook: CardTable;
  timeline: TimelineEntry[];
  upcoming: Array<{ date: CaseDate; label: string; when: string }>;
  stage: number;
  steps7: Array<{ label: string; n: number; done: boolean; now: boolean }>;
  check: ChecklistItem[];
  example: string;
  since: { n: number; lines: string[] } | null;
  emailsList: Array<{ label: string; state: string; pending: boolean }>;
  docsList: Array<{ label: string; state: string; open: boolean; final: boolean }>;
  hdr: CaseHeader;
  summary: {
    goals: Array<{ k: string; v: string }>;
    terms: TermView[];
    decisions: Array<{ by: string; at: string; text: string }>;
    refs: string;
  };
  cards: Card[];
  actions: Array<{ label: string; act: ActionType }>;
  threads: Array<{ t: string; on: boolean; offer: boolean; hasOffer: boolean }>;
  demo: Array<{ label: string; act: ActionType }>;
  reply: { open: boolean; text: string; from: string };
  shortStatus: string;
  permissions: Permissions;
  responsibility: Responsibility;
}
