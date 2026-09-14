import type { DocumentListItemBody } from "../../api/client";
import { getRejectionReasonCopy } from "./uploadPipeline";
import { DOCUMENT_PROCESSING_STAGES, getOpenTarget, getRowStatus } from "./documentTable";

/**
 * View-model for `DocumentProgressPanel.tsx` (`?progress=<id>`; task E16/F03/US02/T02, wave w15,
 * ADR-020 w15 footer 11; ADR-027 w15 footer C12). Kept separate from the component the same way
 * `documentTable.ts` is kept separate from `DocumentStatusTable.tsx` -- pure functions over a
 * `DocumentListItemBody`, nothing that touches the DOM, the network, or React state.
 *
 * The panel opens only from an `"uploaded"`/`"processing"` row (`getOpenTarget`), but the document
 * can finish while the panel stays mounted -- the poll `useDocumentsList.ts` already runs keeps
 * feeding it a fresher `item`. This module therefore has a real answer for every
 * `DocumentListItemBody["processingStatus"]`, not only the two that open it.
 */

export type ProgressStageState = "done" | "current" | "todo";

export interface ProgressStageView {
  name: string;
  state: ProgressStageState;
}

/** The six real stages (R-DOC-09), each marked against `stage`'s own position in
 * `DOCUMENT_PROCESSING_STAGES` -- the same source `documentTable.ts#getStagePercent` reads for the
 * row's own inline bar, so the checklist and the bar can never disagree. A `null` stage (queued,
 * no Worker has claimed the job yet) marks every stage `"todo"`: nothing is `"current"` until a
 * real stage name arrives, the same "0%, an honest just-started reading" `getStagePercent` already
 * chose over a guess. */
export function getProgressStages(stage: string | null): readonly ProgressStageView[] {
  const index = stage === null ? -1 : DOCUMENT_PROCESSING_STAGES.indexOf(stage);
  return DOCUMENT_PROCESSING_STAGES.map((name, position) => ({
    name,
    state: index === -1 ? "todo" : position < index ? "done" : position === index ? "current" : "todo",
  }));
}

export interface ProgressLink {
  href: string;
  label: string;
}

export interface DocumentProgressView {
  /** True while the document is still `Uploaded`/`Processing` -- the panel renders the stage
   * checklist only in this state. `stages` is still populated (all `"todo"`) for the other states,
   * so a caller never has to branch on `undefined`. */
  isWaiting: boolean;
  stages: readonly ProgressStageView[];
  /** What the panel says under the document's name -- one sentence, never a guess: the same "Queued,
   * starting shortly" / real stage name the row's own next-step cell would show, or the exact
   * terminal sentence the row grid already uses (`DocumentStatusTable.tsx`), never a re-derived one. */
  headline: string;
  /** Where the terminal state's own next step goes -- `null` while `isWaiting`, for `"failed"` and
   * `"rejected"` (the row's own next-step cell has no link there either, only a sentence), and for a
   * `"completed"` document this workspace has no contract id for yet (`getOpenTarget`'s own
   * defensive case). */
  link: ProgressLink | null;
}

/** Verbatim from `DocumentStatusTable.tsx`'s own filename-cell fallback for a `"failed"` row --
 * said once there, so it is said the same way here. */
const FAILED_HEADLINE = "Not yet linked to a contract";

export function getProgressView(
  item: Pick<DocumentListItemBody, "id" | "processingStatus" | "stage" | "contractId" | "documentType" | "rejectionReason">,
): DocumentProgressView {
  const rowStatus = getRowStatus(item.processingStatus);

  if (rowStatus === "uploaded" || rowStatus === "processing") {
    return {
      isWaiting: true,
      stages: getProgressStages(item.stage),
      headline: item.stage === null ? "Queued, starting shortly" : `${item.stage}…`,
      link: null,
    };
  }

  const stages = getProgressStages(null);

  if (rowStatus === "needs_review") {
    return {
      isWaiting: false,
      stages,
      headline: "Ready for a quick review.",
      link: { href: getOpenTarget(item, rowStatus)!, label: "Review now" },
    };
  }

  if (rowStatus === "completed") {
    const href = getOpenTarget(item, rowStatus);
    const isQuote = item.documentType === "Quote";
    return {
      isWaiting: false,
      stages,
      headline: isQuote ? "Ready in Quote check." : "Done -- it is now askable.",
      link: href !== null ? { href, label: isQuote ? "Open Quote check" : "Open the contract" } : null,
    };
  }

  if (rowStatus === "failed") {
    return { isWaiting: false, stages, headline: FAILED_HEADLINE, link: null };
  }

  // rowStatus === "rejected": the same reason sentence the row's own hint shows -- silent (a plain,
  // honest fallback) when this app does not know the code, the panel's counterpart to
  // DocumentStatusTable.tsx's "tag, no hint" for the same case.
  return {
    isWaiting: false,
    stages,
    headline: getRejectionReasonCopy(item.rejectionReason) ?? "Raffa.ai did not add this file.",
    link: null,
  };
}
