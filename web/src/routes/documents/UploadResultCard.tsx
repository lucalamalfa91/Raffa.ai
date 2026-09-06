import { getResultCardContent, type UploadOutcome } from "./uploadPipeline";

export interface UploadResultCardProps {
  fileName: string;
  outcome: UploadOutcome;
  /** Pre-resolved message (index.tsx): usually `getResultCardContent(outcome, fileName).message`, but overridden with the API's own error text on an HTTP/network failure -- see index.tsx's own comment. */
  message: string;
  onPrimaryAction: () => void;
  onUploadAnother: () => void;
}

/**
 * AC-3: one result card per outcome (needs_review / completed / failed).
 * Tag variant/label reuse styles/semantics.ts#getStatusTag (ADR-019's locked
 * status mapping) via uploadPipeline.ts#getResultCardContent -- never
 * re-derived here. Layout (tag + filename row, message, primary CTA +
 * "Upload another" secondary) is quoted from the compiled prototype's own
 * result block (day1-demo.html: `{{ uplTag }} {{ uplLabel }}`,
 * `{{ uplFile }}`, `{{ uplMsg }}`, primary `{{ uplCta }}`, secondary "Upload
 * another").
 */
export default function UploadResultCard({
  fileName,
  outcome,
  message,
  onPrimaryAction,
  onUploadAnother,
}: UploadResultCardProps) {
  const { tag, ctaLabel } = getResultCardContent(outcome, fileName);

  return (
    <div className="card upload-result-card" role="status">
      <div className="upload-result-header">
        <span className={`tag tag-${tag.variant}`}>{tag.label}</span>
        <span className="upload-result-filename">{fileName}</span>
      </div>
      <p className="upload-result-message">{message}</p>
      <div className="upload-result-actions">
        <button type="button" className="btn btn-primary" onClick={onPrimaryAction}>
          {ctaLabel}
        </button>
        <button type="button" className="btn btn-secondary" onClick={onUploadAnother}>
          Upload another
        </button>
      </div>
    </div>
  );
}
