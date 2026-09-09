import UploadDropzone from "./UploadDropzone";
import type { SampleDocumentKey } from "./sampleDocument";

export interface OnboardingEmptyStateProps {
  onFilesSelected: (files: File[]) => void;
  onUseSampleFile: (key: SampleDocumentKey) => void;
}

/**
 * `docsEmpty` (`contigo-v2/markup.html` lines ~116-131; `contigo-v2/screens-v2.md` #3 "Onboarding
 * empty"): shown before this tenant has any tracked document at all (not even a rejected/in-flight
 * one this session -- see `index.tsx`'s own state machine). Headline and three-step copy are quoted
 * **verbatim from the literal prototype markup** (`01 · Upload` / `Drop your contracts`, `02 ·
 * Process` / `Contigo extracts the facts`, `03 · Ask` / `Ask Contigo`) -- not from
 * `contigo-v2/screens-v2.md`'s own shorthand summary of the same block ("02 · Review"), which
 * paraphrases the middle step's *purpose* rather than quoting its actual heading; ADR-024 names the
 * prototype itself, not a summary of it, as the pixel/copy reference.
 */
export default function OnboardingEmptyState({ onFilesSelected, onUseSampleFile }: OnboardingEmptyStateProps) {
  return (
    <div className="documents-onboarding">
      <p className="screen-kicker">Start here</p>
      <h1 className="documents-onboarding-headline">First your contracts. Then your questions.</h1>

      <div className="documents-onboarding-steps">
        <div className="documents-onboarding-step">
          <div className="documents-onboarding-step-kicker">01 · Upload</div>
          <div className="documents-onboarding-step-title">Drop your contracts</div>
          <p className="micro-meta">MSA, order forms, SOWs, amendments. PDF, DOCX, XLSX, PNG, JPG.</p>
        </div>
        <div className="documents-onboarding-step">
          <div className="documents-onboarding-step-kicker documents-onboarding-step-kicker--muted">02 · Process</div>
          <div className="documents-onboarding-step-title">Contigo extracts the facts</div>
          <p className="micro-meta">Every fact has a source and a confidence. You sign off the weak ones.</p>
        </div>
        <div className="documents-onboarding-step">
          <div className="documents-onboarding-step-kicker documents-onboarding-step-kicker--muted">03 · Ask</div>
          <div className="documents-onboarding-step-title">Ask Contigo</div>
          <p className="micro-meta">Answers only from validated facts, with the page that proves them.</p>
        </div>
      </div>

      <UploadDropzone variant="onboarding" onFilesSelected={onFilesSelected} onUseSampleFile={onUseSampleFile} />
    </div>
  );
}
