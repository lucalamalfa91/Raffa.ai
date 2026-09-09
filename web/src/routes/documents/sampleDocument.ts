/**
 * "Use the sample MSA" / "Sample MSA" (`contigo-v2/markup.html`'s own onboarding and list-view
 * dropzones both keep a sample-file action, `useSample`; task E13/F09/US01/T03's own file-scope
 * note: "keep sample only if the prototype's 'sample file' stays" -- it does, in both variants of
 * `UploadDropzone.tsx`). This repo ships no real sample contract asset (checked: no `*.pdf`
 * anywhere in the repo), so rather than silently disabling the button or faking a network
 * response, this builds a small PDF in the browser and sends it through the exact same
 * `apiClient.uploadDocument()` call (via `useDocumentsList.ts#uploadFiles`) a real
 * drag-and-drop/file-picker upload uses. It is a real end-to-end request against the deployed
 * API, not a client-only mock.
 *
 * **It has to be a readable contract, not a placeholder.** The first version of this file was a
 * 160-byte PDF with a catalog, no page objects and no text at all. Against the V2 admission gate
 * (task E13/F04/US01/T01) that document is correctly rejected -- 422 `no_readable_text` -- so the
 * one button a first-time user is invited to press answered "Not added". The content below is a
 * one-page, syntactically real PDF whose text stream carries a short but genuine master services
 * agreement: enough readable characters to clear `Documents:MinReadableChars` (200), and the words
 * `MASTER SERVICES AGREEMENT` the classifier keys on, so the sample lands as an admitted `Msa` and
 * the user sees the actual product path -- upload, classify, extract, ask.
 *
 * The outcome is still whatever the deployed pipeline really returns (`needs_review` is the honest
 * result while extraction runs on the fixture gateway); this file guarantees a readable contract,
 * never a scripted verdict.
 *
 * The PDF is hand-built rather than generated: one `/Type /Page` object paired with one
 * text-bearing content stream is exactly the shape `NativeDocumentTextExtractor` reads natively
 * (it counts page objects and pairs them 1:1 with `BT ... Tj ... ET` streams), so the sample takes
 * the born-digital path and never spends an OCR call.
 */
const SAMPLE_CONTRACT_TEXT = [
  "MASTER SERVICES AGREEMENT between Contigo Demo AG and Northwind Traders SA, effective 2026-01-01.",
  "This Agreement governs every Order Form the parties execute under it. Annual fees are EUR 48,000,",
  "invoiced yearly in advance and payable within thirty days. The initial term is thirty-six months and",
  "renews automatically for successive twelve-month terms unless either party gives ninety days written",
  "notice before the end of the then-current term. Price increases at renewal are capped at four percent.",
  "Either party may terminate for material breach not cured within thirty days of written notice.",
].join(" ");

/**
 * A minimal but syntactically real single-page PDF. Written as Latin-1 text (the encoding
 * `NativeDocumentTextExtractor` decodes an uncompressed content stream with), so the sample text
 * deliberately stays within that character set.
 */
const SAMPLE_PDF_CONTENT = [
  "%PDF-1.4",
  "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj",
  "2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj",
  "3 0 obj << /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> >> /MediaBox [0 0 612 792] /Contents 5 0 R >> endobj",
  "4 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj",
  "5 0 obj << /Length " + String(SAMPLE_CONTRACT_TEXT.length + 32) + " >>",
  "stream",
  "BT /F1 11 Tf 54 720 Td (" + SAMPLE_CONTRACT_TEXT + ") Tj ET",
  "endstream",
  "endobj",
  "trailer << /Root 1 0 R >>",
  "%%EOF",
].join("\n");

export const SAMPLE_DOCUMENT_FILE_NAME = "contigo-sample-contract.pdf";

export function createSampleDocumentFile(): File {
  return new File([SAMPLE_PDF_CONTENT], SAMPLE_DOCUMENT_FILE_NAME, { type: "application/pdf" });
}
