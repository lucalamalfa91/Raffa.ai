import type { ConversationMessage4 } from "../../api/client";

/** A plain "yes" typed in the composer, answering the proposal Raffa just made. */
const YES = /^\s*(s[iì]|ok(ay)?|va bene|certo|certamente|procedi|procediamo|conferm[oa]|confermato|avvia|yes|yep|yeah|sure|confirm|go ahead|do it|proceed)\W*$/i;
/** A plain "no". */
const NO = /^\s*(no|nope|annulla|lascia stare|non ora|cancel|stop|never ?mind|not now)\W*$/i;

export type ProposalAnswer = "yes" | "no" | null;

export function proposalAnswer(text: string): ProposalAnswer {
  if (YES.test(text)) return "yes";
  if (NO.test(text)) return "no";
  return null;
}

/**
 * The proposal still waiting for an answer: the one on the conversation's last message, when that
 * message is Raffa's (a later question of the user means it was left unanswered).
 */
export function pendingProposal(messages: ConversationMessage4[]): ConversationMessage4 | null {
  const last = messages[messages.length - 1];
  return last && last.role === "raffa" && last.payload?.actionProposal ? last : null;
}
