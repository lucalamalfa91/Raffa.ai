import { useRef, useState } from "react";
import { ACCEPTED_EXTENSIONS } from "./uploadPipeline";
import { SAMPLE_DOCUMENTS, type SampleDocumentKey } from "./sampleDocument";

export interface UploadDropzoneProps {
  /** `"onboarding"` (bigger box, `raffa-v2/markup.html` `docsEmpty`) vs `"list"` (compact inline
   * bar shown above the row grid once at least one document exists, `docsList`) -- same drag/pick
   * logic, different chrome/copy density (both quoted from the V2 prototype). */
  variant: "onboarding" | "list";
  onFilesSelected: (files: File[]) => void;
  /** One of the two built-in sample MSAs (`sampleDocument.ts#SAMPLE_DOCUMENTS`): the clean one that
   * completes, or the ambiguous one that needs review. */
  onUseSampleFile: (key: SampleDocumentKey) => void;
}

/**
 * Multi-file dropzone (task E13/F09/US01/T03: "multi-file drop / pick (`accept` widened, up to 20
 * files, uploads run with <= 3 in flight)"; requirements R-DOC-01/02). Unlike V1, there is no
 * `disabled` state -- more than one upload can be in flight at once (`useDocumentsList.ts`'s own
 * concurrency-capped batch runner), so the picker/drop target stays live regardless of how many
 * rows are already processing.
 *
 * The prototype's single "sample file" action became two: a first-time user should be able to see
 * both ends of the product path -- a contract that completes and is askable at once, and one that
 * genuinely needs a review pass -- without hunting for two real PDFs. Both buttons render the same
 * labels in both variants (`SampleDocumentDefinition.label`), so a test or a user recognises them
 * wherever the dropzone sits.
 */
export default function UploadDropzone({ variant, onFilesSelected, onUseSampleFile }: UploadDropzoneProps) {
  const [dragging, setDragging] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const handleFiles = (fileList: FileList | null) => {
    if (!fileList || fileList.length === 0) return;
    onFilesSelected(Array.from(fileList));
  };

  const dropTarget = (
    <div
      className={`upload-dropzone upload-dropzone--${variant}${dragging ? " is-dragging" : ""}`}
      onDragOver={(event) => {
        event.preventDefault();
        setDragging(true);
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={(event) => {
        event.preventDefault();
        setDragging(false);
        handleFiles(event.dataTransfer.files);
      }}
    >
      <input
        ref={inputRef}
        type="file"
        multiple
        accept={ACCEPTED_EXTENSIONS}
        onChange={(event) => {
          handleFiles(event.target.files);
          // Same file(s) re-selected twice in a row must still fire onChange.
          event.target.value = "";
        }}
        className="upload-dropzone-file-input"
        aria-label="Choose contract files from your computer"
      />
      <button type="button" className="btn btn-primary" onClick={() => inputRef.current?.click()}>
        Upload contracts
      </button>
      <span className="upload-dropzone-hint">
        {variant === "onboarding" ? "or drop files anywhere in this box" : "or drop PDF · DOCX · XLSX · PNG · JPG here — you can leave while they process"}
      </span>
      <div className="upload-dropzone-samples" role="group" aria-label="Sample contracts">
        {variant === "onboarding" && <span className="micro-meta">Or try a sample:</span>}
        {SAMPLE_DOCUMENTS.map((sample) => (
          <button
            key={sample.key}
            type="button"
            className="btn btn-ghost upload-dropzone-sample"
            title={sample.description}
            onClick={() => onUseSampleFile(sample.key)}
          >
            {sample.label}
          </button>
        ))}
      </div>
    </div>
  );

  if (variant === "list") {
    return dropTarget;
  }

  return (
    <div className="upload-dropzone-column">
      {dropTarget}
      <div className="upload-strip">
        <div className="upload-strip-cell">
          <div className="upload-strip-kicker">Formats</div>
          <div className="upload-strip-value">PDF · DOCX · XLSX · PNG · JPG</div>
        </div>
        <div className="upload-strip-cell">
          <div className="upload-strip-kicker">Max size</div>
          <div className="upload-strip-value">50 MB / file</div>
        </div>
        <div className="upload-strip-cell">
          <div className="upload-strip-kicker">Up to</div>
          <div className="upload-strip-value">20 files at once</div>
        </div>
      </div>
    </div>
  );
}
