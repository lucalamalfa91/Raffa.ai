/**
 * AskRaffa 4.0 renewal case, Today feed and Renewals board -- the response shapes of
 * `backend/src/Raffa.Api/RenewalCasesEndpointExtensions.cs` (OpenAPI declares them as open objects;
 * these types mirror `Raffa.Renewals.Application.Cases.CaseView` and `...CaseWork.*`, camelCase).
 * Every engine text is a `Text` (`string | {id, vars}`), translated with `resolveText`.
 */
import type { Text } from "../i18n/translate";

/** urgent (accent) | alert (accent-700) | muted (neutral-600) | strong (text) | null (inherit). */
export type Tone = "urgent" | "alert" | "muted" | "strong" | null;

export interface CaseActionPayload {
  q?: number | null;
  val?: string | null;
  k?: string | null;
  v?: unknown;
  eid?: number | null;
  rid?: number | null;
  did?: number | null;
  opt?: string | null;
  text?: Text | null;
  resume?: Text | null;
  who?: string | null;
  on?: boolean | null;
  i?: number | null;
  n?: number | null;
  version?: number | null;
  vendor?: string | null;
  t?: string | null;
  file?: string | null;
  to?: string | null;
}

export interface CaseButton {
  label: Text;
  act: string;
  p: CaseActionPayload;
  primary: boolean;
  off: boolean;
  why?: Text | null;
}

export interface CaseChip {
  label: Text;
  act: string;
  p: CaseActionPayload;
  on: boolean;
  off: boolean;
}

export interface CaseInput {
  label: Text;
  value: unknown;
  ui: string;
  /** text | check | area */
  type: string;
}

export interface CaseField {
  name: Text;
  val: string;
  conf: string;
  state: string;
  open: boolean;
  doneLabel: Text;
  inKey: string;
  inVal: string;
  i: number;
}

export interface CaseRow {
  k: Text;
  v: Text;
  w?: number | null;
  tone?: Tone;
}

export interface CaseCell {
  v: Text;
  w?: number | null;
  tone?: Tone;
}

export interface CaseTable {
  grid: string;
  cols: Text[];
  rows: CaseCell[][];
}

export interface CaseCardStep {
  label: Text;
  on: boolean;
  now: boolean;
}

export interface CaseStat {
  k: Text;
  v: Text;
  tone?: Tone;
}

export interface CaseCardEmail {
  eid: number;
  to: string;
  toMissing: boolean;
  toKey: string;
  stateKey: string;
  state: Text;
  subject: string;
  body: string;
  attach: string[];
  editing: boolean;
  editBody: string;
  showBody: boolean;
  sentNote: Text;
  attachNote: Text;
}

export interface CaseCurrentCard {
  k: string;
  kicker: Text;
  title: Text;
  banner: Text;
  lines: Text[];
  rows: CaseRow[];
  table?: CaseTable | null;
  steps: CaseCardStep[];
  email?: CaseCardEmail | null;
  pre: string;
  fields: CaseField[];
  chips: CaseChip[];
  inputs: CaseInput[];
  buttons: CaseButton[];
  more: CaseButton[];
  note: Text;
  bullets: Text[];
  stats: CaseStat[];
}

export interface CaseDoneStep {
  kicker: Text;
  title: Text;
  note: Text;
  lines: Text[];
  rows: CaseRow[];
  steps: CaseCardStep[];
  chips: CaseChip[];
}

export interface CaseBubble {
  by: string;
  raffa: boolean;
  text: Text;
}

export interface CasePage {
  title: Text;
  phase: Text;
  next: Text;
  notice: Text;
  noticeUrgent: boolean;
  end: Text;
  warn?: Text | null;
  whoCan: Text;
  done: CaseDoneStep[];
  msgs: CaseBubble[];
  current: CaseCurrentCard;
}

export interface CaseTermView {
  label: Text;
  val: string;
  state: Text;
  status: string;
  tone?: Tone;
  w: number;
}

export interface CaseRefItem {
  label: Text;
  state: Text;
  tone?: Tone;
}

export interface CasePerson {
  name: string;
  role: Text;
  isOwner: boolean;
  isDel: boolean;
  canClose: boolean;
  viewing: boolean;
}

export interface CaseUpcoming {
  date: string;
  label: Text;
  when: Text;
}

export interface CaseView {
  hdr: {
    title: Text;
    supplier: string;
    contract: string;
    status: Text;
    next: Text;
    approver: string;
    noticeBy: string;
    noticeDays: number;
    end: Text;
    owner: string;
    warn?: Text | null;
    people: CasePerson[];
    youRole: Text;
  };
  summary: { goals: CaseRow[]; terms: CaseTermView[]; decisions: unknown[]; refs: Text };
  page: CasePage;
  upcoming: CaseUpcoming[];
  emailsList: CaseRefItem[];
  docsList: CaseRefItem[];
  reply: { open: boolean; text: string; from: string };
  stage: number;
  permissions: { canWork: boolean; canApprove: boolean; canClose: boolean; role: string };
}

export interface CaseEnvelope {
  caseId: string;
  contractId: string;
  version: number;
  view: CaseView;
  capabilities: { canWork: boolean; canApprove: boolean; canClose: boolean };
  /** Text to put on the clipboard after "copy" (copy is never send). */
  clip: string | null;
  /** Post `tick` shortly (extraction progress). */
  tick: boolean;
  closed: boolean;
  /** Fixture mode: scripted demo shortcuts exist (e.g. "Usa risposta di esempio"). */
  demo: boolean;
}

/** A text the server already wrote in both languages (the situation assessment's judgement). */
export interface LocalizedText {
  en: string;
  it: string;
}

/**
 * A contract's situation once its notice deadline has passed: the effective term (computed by the
 * server, never by a model) and the judgement on it (`origin`: "ai" when the model's answer passed
 * the guard, else "rules").
 */
export interface ContractAssessment {
  status: "active" | "rolledOver" | "ending" | "expired" | "needsReview" | "unknown";
  rolledOver: boolean;
  renewedOn: string | null;
  renewedOnPassed: boolean;
  lockedOn: string | null;
  start: string | null;
  end: string | null;
  noticeDeadline: string | null;
  noticeDays: number | null;
  endDays: number | null;
  originalEnd: string | null;
  originalNoticeDeadline: string | null;
  renewalTermMonths: number | null;
  headline: LocalizedText;
  why: LocalizedText;
  recommendedAction: "start_renewal" | "prepare_next_notice" | "negotiate_mid_term" | "plan_replacement" | "confirm_terms" | "none";
  urgency: "now" | "soon" | "later" | "none";
  source: { key: string; doc: string; page: number | null; section: string | null; quote: string | null } | null;
  origin: "ai" | "rules";
}

export interface TodayItem {
  key: string;
  kind: string;
  caseId: string | null;
  contractId: string | null;
  quoteId: string | null;
  documentId: string | null;
  supplier: string;
  contract: string;
  title: Text;
  why: Text;
  due: string;
  dueDays: number;
  dueLabel: Text | null;
  stage: number;
  stageLabel: Text;
  who: string;
  stalled: number;
  /** Set when the contract's notice deadline has passed (rolled over, ending, renewal term to confirm). */
  assessment?: ContractAssessment | null;
}

export interface TodayViewer {
  userId: string | null;
  name: string;
  title: string;
  isApprover: boolean;
  date: string;
}

export interface CaseAssignee {
  userId: string;
  name: string;
}

export interface TodayFeed {
  viewer: TodayViewer;
  emptyWorkspace: boolean;
  mine: TodayItem[];
  waiting: TodayItem[];
  snoozed: TodayItem[];
  nextDeadline: { supplier: string; date: string; days: number } | null;
  assignee: CaseAssignee | null;
  documents: { documentId: string; file: string; fieldsToCheck: number; ownerName: string | null; due: string | null }[];
  /** Informational, not an action: a contract that renewed by itself recently. */
  info?: TodayItem[];
  /** The caller's own open-case steps beyond the company's lead time (Renewals: "Yours, later"). */
  later?: TodayItem[];
  /** Open renewal cases of the workspace (the Renewals board's own count). */
  openCases?: number;
}

export interface RenewalsRow {
  contractId: string;
  caseId: string | null;
  supplier: string;
  contract: string;
  stage: number;
  stageLabel: Text;
  next: Text | null;
  notice: string;
  days: number;
  startBy: string;
  owner: string | null;
  myTurn: boolean;
  hasCase: boolean;
  done: boolean;
  waiting: boolean;
  /** active | rolledOver | ending | expired | needsReview | unknown. */
  termStatus?: string;
  /** When the term rolled over by itself: the day the current term began. */
  renewedOn?: string | null;
  /** The renewal already took effect (false: it will on `renewedOn`, the notice having passed). */
  renewedOnPassed?: boolean;
  end?: string | null;
  /** The case's next step is beyond the company's lead time: not on Today yet. */
  later?: boolean;
}

export interface RenewalsBoard {
  viewer: TodayViewer;
  groups: { key: string; label: Text; items: RenewalsRow[] }[];
  openCases: number;
  toStart: number;
  empty: boolean;
  assignee: CaseAssignee | null;
  /** The open cases, flat (read by Chiedi's "Pratiche aperte"). */
  cases: RenewalsRow[];
}

/** A case call's result: the body, or the error with the current case the server returned with it. */
export type CaseCallResult<T> =
  | { ok: true; statusCode: number; body: T; error: null }
  | {
      ok: false;
      statusCode: number | null;
      body: null;
      error: string;
      /** stale_version | duplicate_reply | forbidden | not_found | bad_request | not_closable */
      code: string | null;
      current: CaseEnvelope | null;
    };
