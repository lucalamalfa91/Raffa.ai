import { getStagePercent } from "./documentTable";

export interface ProcessingPipelineProps {
  /** The real stage name from `GET /api/documents` (R-DOC-09), or `null` before the first poll has
   * resolved one. Never a client-side timer -- see `uploadPipeline.ts`'s own header comment for why
   * V1's 6-item ticker list is gone. */
  stage: string | null;
}

/**
 * Inline per-row progress bar, status-cell half of the processing treatment (`raffa-v2/markup.html`
 * row template: a bare 4px bar directly under the status tag, `width:{{ d.pct }}`; the stage *label*
 * sits separately, right-aligned in the row's own "next step" cell -- see
 * `DocumentStatusTable.tsx`, which renders that half directly rather than through this component).
 * V1's `ProcessingPipeline` was a separate, standalone 6-stage list shown next to the dropzone while
 * exactly one upload was in flight; V2 shows every row (however many files are mid-flight) at once,
 * so this component is now mounted once per processing row.
 */
export default function ProcessingPipeline({ stage }: ProcessingPipelineProps) {
  const percent = getStagePercent(stage);

  return (
    <div className="document-row-progress" role="progressbar" aria-valuenow={percent} aria-valuemin={0} aria-valuemax={100}>
      <div className="document-row-progress-fill" style={{ width: `${percent}%` }} />
    </div>
  );
}
