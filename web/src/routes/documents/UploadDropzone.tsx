import { useRef, useState } from "react";

export interface UploadDropzoneProps {
  /** True while a document is already uploading -- disables both pick paths so only one upload runs at a time. */
  disabled: boolean;
  onFilesSelected: (files: File[]) => void;
  onUseSampleFile: () => void;
}

const ACCEPTED_EXTENSIONS = ".pdf,.docx,.xlsx";

/**
 * AC-1 (dropzone + formats/size/sources strip). Structure and copy are
 * quoted from the compiled prototype (inputs/design/prototypes/day1-demo.html):
 * heading "Drop contracts here", subtitle "Each file becomes a processing
 * job; you can leave this page while it runs.", buttons "Choose from
 * computer" / "Use sample file", and the 3-cell strip -- Formats
 * "PDF · DOCX · XLSX", Max size "50 MB / file", Sources "Local · SharePoint
 * soon" (the last cell matches product-spec.md's own integration roadmap:
 * manual upload is P1/V1, SharePoint is explicitly P2, so "soon" is
 * accurate, not decorative copy). `accept=".pdf,.docx,.xlsx"` mirrors
 * product-spec.md §4.1 ("Upload PDF, DOCX and XLSX commercial/contract
 * documents").
 *
 * Drag-and-drop is a progressive enhancement over the native, keyboard- and
 * screen-reader-operable "Choose from computer" button (ADR-019
 * accessibility baseline: "all interactive controls are native") -- a
 * keyboard user never needs the drag gesture to reach any state this screen
 * has.
 *
 * Task E11/F04/US01/T01 (gap G-DOC): the leading upload glyph was a missing
 * node against the compiled prototype -- added back verbatim (same viewBox
 * and path data) per ADR-019's icon rule ("Lucide, inline SVG, currentColor,
 * 1.5 stroke, square caps"). `aria-hidden` because "Drop contracts here"
 * already carries the meaning; the icon is decorative.
 */
export default function UploadDropzone({ disabled, onFilesSelected, onUseSampleFile }: UploadDropzoneProps) {
  const [dragging, setDragging] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const handleFiles = (fileList: FileList | null) => {
    if (!fileList || fileList.length === 0) return;
    onFilesSelected(Array.from(fileList));
  };

  return (
    <div className="upload-dropzone-column">
      <div
        className={`upload-dropzone${dragging ? " is-dragging" : ""}`}
        onDragOver={(event) => {
          event.preventDefault();
          if (!disabled) setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={(event) => {
          event.preventDefault();
          setDragging(false);
          if (!disabled) handleFiles(event.dataTransfer.files);
        }}
      >
        <svg
          className="upload-dropzone-icon"
          width="32"
          height="32"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth={1.5}
          strokeLinecap="square"
          aria-hidden="true"
        >
          <path d="M12 3v12" />
          <path d="m7 8 5-5 5 5" />
          <path d="M4 15v5h16v-5" />
        </svg>
        <p className="upload-dropzone-title">Drop contracts here</p>
        <p className="micro-meta">Each file becomes a processing job; you can leave this page while it runs.</p>
        <input
          ref={inputRef}
          type="file"
          multiple
          accept={ACCEPTED_EXTENSIONS}
          onChange={(event) => {
            handleFiles(event.target.files);
            // Same file re-selected twice in a row must still fire onChange.
            event.target.value = "";
          }}
          className="upload-dropzone-file-input"
          aria-label="Choose contract files from your computer"
        />
        <div className="upload-dropzone-actions">
          <button
            type="button"
            className="btn btn-primary"
            disabled={disabled}
            onClick={() => inputRef.current?.click()}
          >
            Choose from computer
          </button>
          <button type="button" className="btn btn-secondary" disabled={disabled} onClick={onUseSampleFile}>
            Use sample file
          </button>
        </div>
      </div>
      <div className="upload-strip">
        <div className="upload-strip-cell">
          <div className="upload-strip-kicker">Formats</div>
          <div className="upload-strip-value">PDF · DOCX · XLSX</div>
        </div>
        <div className="upload-strip-cell">
          <div className="upload-strip-kicker">Max size</div>
          <div className="upload-strip-value">50 MB / file</div>
        </div>
        <div className="upload-strip-cell">
          <div className="upload-strip-kicker">Sources</div>
          <div className="upload-strip-value">Local · SharePoint soon</div>
        </div>
      </div>
    </div>
  );
}
