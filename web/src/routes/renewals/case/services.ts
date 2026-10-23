/**
 * The service boundary of the case engine.
 *
 * The design prototype scripted these parts for the demo. In production each is a real service -- an
 * LLM reading of the supplier's reply, a contract diff, vendor offers, the extraction pipeline -- and
 * the engine does not change: it only calls this interface. `mockServices.ts` keeps the prototype's
 * scripted scenario behind it.
 */

import type { Text } from "../../../i18n/translate";
import type { AltOffer, Case, CaseContext, DraftRow, Email, Pipe, ReplyRow, TermStatus } from "./types";

export interface ReplyAnalysis {
  rows: ReplyRow[];
  /** Term updates the analysis implies (the engine applies them to `case.terms`). */
  terms: Array<{ key: string; label: Text; state: TermStatus; val: string | null }>;
}

export interface CaseServices {
  /** Demo helper: the text "Paste example reply" puts in the box. */
  sampleReply(c: Readonly<Case>, ctx: CaseContext, round: number): string;
  /** The unit price the supplier would settle on for `term` months (round 2 position). */
  counterPrice(ctx: CaseContext, term: number): number;
  analyzeReply(c: Readonly<Case>, ctx: CaseContext, rid: number): ReplyAnalysis;
  /** Compare draft `version` of the contract with `c.agreed`, point by point. */
  verifyDraft(c: Readonly<Case>, ctx: CaseContext, version: number): DraftRow[];
  /** Vendor names offered by the "Add two examples" shortcut. */
  exampleVendors(): string[];
  vendorOffer(ctx: CaseContext, vendor: string, by: string): AltOffer;
  /** Starts extraction of the signed contract. */
  startExtraction(ctx: CaseContext): Pipe;
  /**
   * Demo shortcut for a recipient. Optional on purpose: Raffa never invents addresses, so a real
   * service leaves it out and the "Use example address" button disappears.
   */
  exampleAddress?(email: Pick<Email, "kind" | "vendor">, ctx: CaseContext): string;
}
