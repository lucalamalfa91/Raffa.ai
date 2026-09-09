/**
 * "Use the sample MSA" / "Sample MSA" (`contigo-v2/markup.html`'s own onboarding and list-view
 * dropzones both keep a sample-file action, `useSample`; task E13/F09/US01/T03's own file-scope
 * note: "keep sample only if the prototype's 'sample file' stays" -- it does, in both variants of
 * `UploadDropzone.tsx`). This repo ships no real sample contract asset (checked: no `*.pdf`
 * anywhere in the repo), so rather than silently disabling the button or faking a network
 * response, this builds a small, syntactically-minimal PDF in the browser and sends it through the
 * exact same `apiClient.uploadDocument()` call (via `useDocumentsList.ts#uploadFiles`) a real
 * drag-and-drop/file-picker upload uses. This is a real end-to-end request against the deployed
 * API, not a client-only mock -- whatever outcome classification/extraction actually returns for
 * it (admitted, rejected, or failed) is the honest answer, not a scripted one; a text-only, no
 * `MASTER SERVICES AGREEMENT` string PDF is, per the fixture gateway's own documented behaviour
 * (task-01-documents-admission.md), a plausible **rejected** ("not a contract") outcome, same as
 * any other content-free file -- this button never guarantees an admitted result.
 */
const MINIMAL_PDF_CONTENT =
  "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[]/Count 0>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF";

export const SAMPLE_DOCUMENT_FILE_NAME = "contigo-sample-contract.pdf";

export function createSampleDocumentFile(): File {
  return new File([MINIMAL_PDF_CONTENT], SAMPLE_DOCUMENT_FILE_NAME, { type: "application/pdf" });
}
