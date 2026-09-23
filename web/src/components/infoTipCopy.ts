/**
 * Every InfoTip's words, in one place, so the whole app explains itself in the same voice: plain
 * words, second person, what a thing means and what to do next -- never a figure the screen itself
 * does not show. Two kinds:
 *
 * - **Screen guides** (`SCREEN_GUIDES`, rendered by `ScreenTitle.tsx`): the "i" beside a screen's
 *   title -- what the screen is for, how to use it, where it leads. The onboarding for someone who
 *   opens the screen for the first time.
 * - **Field tips** (`TIPS`): the "i" beside a column header or a label -- what one term means.
 *
 * No React, no I/O: the functions below only pick words from data the caller already has.
 * Tip text never repeats a sentence the screen already prints, word for word (the bubble is always
 * in the DOM as the trigger's description).
 */

export interface TipCopy {
  /** The trigger's accessible name: the question the bubble answers ("What the statuses mean"). */
  label: string;
  /** One paragraph each. */
  lines: readonly string[];
  /** Small print under a rule: a source, a limit, a shortcut. */
  meta?: string;
}

export type ScreenGuideKey =
  | "documents"
  | "review"
  | "portfolio"
  | "contract360"
  | "renewals"
  | "savings"
  | "quotes"
  | "members";

export const SCREEN_GUIDES = {
  documents: {
    label: "How Documents works",
    lines: [
      "Start here. Upload your contracts: Raffa reads each file in the background, recognises what it is and pulls out the key facts — dates, spend, notice, renewal terms — noting the page each one comes from.",
      "Facts Raffa is not sure of wait for you. Open Review, accept or correct them, then mark the document as validated.",
      "Validated documents feed Ask Raffa, Portfolio, Renewals and Savings.",
    ],
    meta: "You can leave this page while files are processed.",
  },
  review: {
    label: "How review works",
    lines: [
      "These are the facts Raffa extracted from the document. The ones it is sure of are accepted automatically; the others wait for your decision.",
      "Click a field to see the passage it was read from, then Accept it or Correct it.",
      "When nothing is left to decide, mark it as validated: from then on the contract feeds Ask, Portfolio, Renewals and Savings.",
    ],
  },
  portfolio: {
    label: "How Portfolio works",
    lines: [
      "Every validated contract in one list, the one whose notice deadline comes first at the top. A highlighted row needs notice within 45 days.",
      "Click a row to open the contract: its terms, what you pay against the market, the clauses that matter and what to do next.",
      "More columns adds the start date, auto-renewal and risk.",
    ],
  },
  contract360: {
    label: "How to read this contract",
    lines: [
      "The three answers on top say where you can save, when you must act and what Raffa recommends. The numbered sections below are the proof: terms, leverage, pricing, clauses, obligations and risks.",
      "Ask about it starts a chat bound to this contract. Review extraction opens the facts Raffa read from its document.",
    ],
  },
  renewals: {
    label: "How Renewals works",
    lines: [
      "Your validated contracts that renew, highest priority first.",
      "The figures on top are filters: click one to narrow the list, click it again to clear it.",
      "Select a row to see why it is here, what Raffa recommends and everything you can start from it. Tick several rows to act on them together.",
    ],
  },
  savings: {
    label: "How Savings works",
    lines: [
      "What you have saved and what you could still save, from your validated contracts and the quotes you checked.",
      "A saving moves from identified (Raffa found it) to in progress (someone is working on it) to verified (the outcome was recorded).",
      "Click a stage or a supplier in the charts to filter the opportunities at the bottom.",
    ],
  },
  quotes: {
    label: "How Quote check works",
    lines: [
      "For a new proposal, before you sign. Upload the supplier's quote: Raffa matches each line to a product and tells you whether the price is above, in line with or below the market.",
      "Lines it cannot match wait for you to map them to a product. Then you can set a target price and record how the negotiation ended.",
      "A quote never changes your portfolio.",
    ],
  },
  members: {
    label: "How members work",
    lines: [
      "Invite your colleagues with their work email. Everyone in the workspace works on the same documents and contracts; chats stay personal.",
      "The table shows who has joined and who is still invited. From it you can revoke an invitation, issue a new one, or remove someone.",
    ],
  },
} as const satisfies Record<ScreenGuideKey, TipCopy>;

export const TIPS = {
  // ---- Rail -----------------------------------------------------------------------------------
  railWorkspace: {
    label: "About your workspace",
    lines: [
      "Everything you see belongs to this workspace: its documents, the contracts built from them and what Raffa knows about them. Your chats are yours alone.",
    ],
  },

  /** The one word every screen leans on. */
  validated: {
    label: "What a validated contract is",
    lines: [
      "A contract whose document you uploaded in Documents and whose key facts are confirmed: by Raffa when it is sure, by you in Review when it is not.",
      "Once validated, the contract feeds Ask Raffa, Portfolio, Renewals and Savings.",
    ],
  },

  // ---- Ask ------------------------------------------------------------------------------------
  askScope: {
    label: "What Raffa answers from",
    lines: [
      "Raffa answers only from your validated contracts and cites the page each fact comes from.",
      "When your contracts do not say, it tells you and estimates from market data, labelled as such.",
      "A new upload counts once it is validated in Documents.",
    ],
  },
  askBound: {
    label: "About a chat on one contract",
    lines: ["This chat is about one contract: every answer cites its pages. Click the contract to open it."],
  },
  askComposer: {
    label: "Tips for asking",
    lines: [
      "Ask in your own words: dates, spend, notice, clauses, market prices, what to negotiate.",
      "You can leave while Raffa answers: the chat keeps going, and it is marked in the list on the left once the reply is in.",
      "Asked for something Raffa cannot do yet? It says so, offers the nearest alternative and lets you propose the feature.",
    ],
    meta: "⌘K / Ctrl+K from any screen jumps straight to a question. Double-click a chat's name to rename it.",
  },

  // ---- Documents ------------------------------------------------------------------------------
  documentsUpload: {
    label: "What you can upload",
    lines: [
      "Contracts, order forms, SOWs and amendments: PDF, Word, Excel or a photo (PNG, JPG), up to 50 MB each and 20 files at a time.",
      "A file that is not a contract is not added; you will find it under Not added.",
    ],
    meta: "Not ready to use your own? Try one of the samples.",
  },
  documentsStatus: {
    label: "What the statuses mean",
    lines: [
      "Uploaded → Processing → Needs review → Completed.",
      "Needs review: some facts are below Raffa's confidence bar and wait for you. Completed: validated, ready to ask about.",
      "Failed: Raffa gave up after several tries; the reason shows under the file's name.",
    ],
    meta: "An upload that stalls is retried automatically.",
  },
  documentsNextStep: {
    label: "What to do next",
    lines: [
      "Each row shows its one next step: review the facts that need you, ask about a validated contract, or — for a supplier's quote — open Quote check.",
      "Nothing to click while a file is processing — it carries on even if you leave.",
    ],
  },
  documentsDeleteAll: {
    label: "What deleting everything removes",
    lines: [
      "Every document in this workspace, the contracts built from them, their renewal tracking and the Ask chats about those contracts.",
      "There is no undo.",
    ],
  },

  // ---- Review ---------------------------------------------------------------------------------
  reviewDecision: {
    label: "Accept or correct",
    lines: [
      "Accept makes the extracted value the contract's official one.",
      "Correct lets you type the right value, with a reason if you like; the change is kept in the contract's history.",
    ],
  },
  reviewValidate: {
    label: "What validating does",
    lines: [
      "The document becomes Completed and its contract starts feeding Ask Raffa, Portfolio, Renewals and Savings.",
      "It unlocks once every field has a decision.",
    ],
  },

  // ---- Portfolio ------------------------------------------------------------------------------
  portfolioNotice: {
    label: "What give notice by means",
    lines: [
      "The last day to tell the supplier you want to cancel or renegotiate. After it, a contract that renews automatically renews on its current terms.",
      "The days count down to it; from 45 days the row is highlighted.",
    ],
  },
  portfolioAuto: {
    label: "What auto-renewal means",
    lines: ["Yes: the contract renews by itself unless you give notice in time."],
  },
  portfolioRisk: {
    label: "Where the risk comes from",
    lines: ["The contract's overall risk level. Open the contract to see what drives it, under Risk factors."],
  },
  portfolioStatus: {
    label: "What the status means",
    lines: ["Active or expired, worked out from the contract's validated start and end dates."],
  },

  // ---- Contract 360 ---------------------------------------------------------------------------
  contractMove: {
    label: "About the notice deadline",
    lines: [
      "The last day to give the supplier notice. After it, a contract that renews automatically renews on its current terms.",
      "In colour when it is within 45 days.",
    ],
  },
  contractAct: {
    label: "About the recommendation",
    lines: [
      "Raffa's next step for this contract, from its dates, its spend and market prices.",
      "Start it to follow the steps here and in Renewals, or assign it to yourself to take ownership.",
    ],
  },

  // ---- Renewals -------------------------------------------------------------------------------
  renewalsScore: {
    label: "How the priority score works",
    lines: [
      "From 0 to 100, the sum of five parts worth up to 20 each: spend, time to the notice deadline, room against the market, price-increase risk and contract risk.",
      "From 80 the score is shown in colour. Select a row to see its parts.",
    ],
  },
  renewalsDays: {
    label: "Renews in and notice in",
    lines: [
      "Renews in: days until the contract renews.",
      "Notice in: days left to tell the supplier you want to cancel or renegotiate. This is the one to beat.",
    ],
  },
  renewalsStatus: {
    label: "What the status means",
    lines: [
      "Open until someone takes it on. Then it shows what is under way — assigned, in negotiation — and closes when the contract is renewed or notice is sent.",
    ],
  },
  renewalsLauncher: {
    label: "About these actions",
    lines: [
      "Each one opens with this contract already in context.",
      "An action Raffa cannot do yet opens Ask, which says so and lets you request it.",
    ],
  },
  renewalsTodos: {
    label: "About negotiation TODOs",
    lines: [
      "The points worth negotiating at this renewal, most important first, each with where you are today and the target.",
      "Ask Raffa what to negotiate before this renewal to fill or refresh the list.",
    ],
  },

  // ---- Savings --------------------------------------------------------------------------------
  savingsVerified: {
    label: "What verified means",
    lines: ["Money actually saved: recorded when a negotiation outcome is captured in Quote check or a renewal closes."],
  },
  savingsIdentified: {
    label: "What identified means",
    lines: ["Savings Raffa found by comparing what you pay with the market, not yet worked on. An estimate, shown as a range."],
  },
  savingsInProgress: {
    label: "What in progress means",
    lines: ["Savings someone is already working on — negotiating, consolidating or switching supplier. Still an estimate, shown as a range."],
  },
  savingsPotential: {
    label: "What potential means",
    lines: ["What the open estimates — identified and in progress — are worth as a share of the annual spend Raffa has analysed."],
  },

  // ---- Quote check ----------------------------------------------------------------------------
  quotesP50: {
    label: "What P50 means",
    lines: ["The market median for the same line: half of comparable customers pay less, half pay more."],
  },
  quotesPosition: {
    label: "What the position means",
    lines: [
      "Where the quoted price sits against the market: above, in line or below.",
      "Needs mapping: Raffa could not match the line to a product yet — map it below to get its price checked.",
    ],
  },
  quotesTarget: {
    label: "Target and walk-away",
    lines: [
      "Your target is the price you aim for; the walk-away is the most you would accept before looking elsewhere.",
      "Both start from Raffa's recommendation and are yours to change.",
    ],
  },

  // ---- Workspace & members --------------------------------------------------------------------
  membersRole: {
    label: "What the roles can do",
    lines: [
      "Procurement: asks Raffa, uploads and reviews documents, handles renewals.",
      "Workspace Admin: all of that, plus deleting documents and managing members.",
    ],
  },
  membersStatus: {
    label: "What the statuses mean",
    lines: [
      "Invited: the invitation is out, not accepted yet. Active: the person has joined. Expired: the invitation ran out before it was used.",
    ],
  },
  membersInvite: {
    label: "How invitations work",
    lines: [
      "The person gets a link by email to join this workspace. If the email cannot go out, copy the link and share it yourself.",
      "The link works once and expires. Until it is used, the person shows as Invited, and you can issue a new invitation or revoke it.",
    ],
  },
} as const satisfies Record<string, TipCopy>;

/** Rail: "From your contracts" -- what the second tier is, and why it may still be dimmed. */
export function buildRailContractsTip(kbReady: boolean): TipCopy {
  return {
    label: "About these screens",
    lines: [
      "These screens work from your validated contracts.",
      kbReady
        ? "The number beside a screen is how many validated contracts it draws on."
        : "Portfolio, Renewals and Quote check stay dimmed until your first contract is validated in Documents. Savings is open from the start.",
    ],
  };
}

/** Documents: the filter chips -- "Not added" is explained only while that chip is on screen. */
export function buildDocumentsFilterTip(showsNotAdded: boolean): TipCopy {
  const lines = [
    "Needs your attention: documents still being processed, waiting for your review or that failed.",
    "All documents: those plus the validated ones.",
  ];
  if (showsNotAdded) lines.push("Not added: files Raffa did not keep because they are not a contract or could not be read.");
  return { label: "About these filters", lines };
}

/** Review: "Confidence" -- the auto-accept bar is the server's (`autoAcceptThreshold`), never a guessed constant. */
export function buildReviewConfidenceTip(thresholdPct: number | null): TipCopy {
  return {
    label: "What confidence means",
    lines: [
      "How sure Raffa is that it read the value right.",
      thresholdPct === null
        ? "A value above the workspace's bar is accepted automatically; below it, you decide."
        : `At ${thresholdPct}% or more a value is accepted automatically; below, you decide.`,
    ],
    meta: "A red dot beside a field means it still waits for you.",
  };
}
