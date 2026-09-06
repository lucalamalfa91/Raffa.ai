/**
 * "Use sample file" (screens.md #3 dropzone: "... 'Choose from computer'
 * file picker, sample file"). This repo ships no real sample contract asset
 * (checked: no `*.pdf` anywhere in the repo), so rather than silently
 * disabling the button or faking a network response, this builds a small,
 * syntactically-minimal PDF in the browser and sends it through the exact
 * same `apiClient.uploadDocument()` call a real drag-and-drop/file-picker
 * upload uses (see ./index.tsx). This is a real end-to-end request against
 * the deployed API, not a client-only mock -- whatever `processingStatus`
 * classification/extraction actually returns for it is the honest answer,
 * not a scripted one.
 */
const MINIMAL_PDF_CONTENT =
  "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[]/Count 0>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF";

export const SAMPLE_DOCUMENT_FILE_NAME = "contigo-sample-contract.pdf";

export function createSampleDocumentFile(): File {
  return new File([MINIMAL_PDF_CONTENT], SAMPLE_DOCUMENT_FILE_NAME, { type: "application/pdf" });
}
