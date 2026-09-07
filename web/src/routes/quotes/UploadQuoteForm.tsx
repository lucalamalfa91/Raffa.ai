import { useState, type FormEvent } from "react";
import type { UploadQuoteFields } from "../../api/client";

export interface UploadQuoteFormProps {
  onUpload: (file: File, fields: UploadQuoteFields) => Promise<{ ok: boolean; error?: string }>;
  submitting: boolean;
}

/**
 * "Enterprise Support Tier — Custom Bundle. Map it manually..." — Kept a small,
 * syntactically-minimal PDF the same way `../documents/sampleDocument.ts` does for the Documents
 * screen's own "Use sample file" control: this repo ships no real sample quote asset (checked: no
 * `*.pdf` anywhere in the repo), so rather than disabling the convenience button or faking a network
 * response, this sends a real file through the exact same `apiClient.uploadQuote()` call a real
 * drag-and-drop/file-picker upload uses. Whatever the real `QuoteExtractionPipeline` returns for it
 * (very possibly zero line items, since this content-free stub has no extractable text -- see
 * `QuoteExtractionPipeline.ProcessAsync`'s own "at least one page of document text" guard) is the
 * honest answer, not a scripted one; a future task that adds a real fixture quote can replace this
 * function without touching anything else in this file.
 */
const SAMPLE_QUOTE_PDF_CONTENT =
  "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[]/Count 0>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF";
const SAMPLE_QUOTE_FILE_NAME = "contigo-sample-quote.pdf";

function createSampleQuoteFile(): File {
  return new File([SAMPLE_QUOTE_PDF_CONTENT], SAMPLE_QUOTE_FILE_NAME, { type: "application/pdf" });
}

/**
 * Quote check's own upload entry point (screens.md #10 names no upload screen at all -- the cited
 * prototype starts directly at "Extracted line items", treating upload as already having happened.
 * ADR-018's route map only ever names the *detail* route `/quotes/:id`, the same "no landing screen
 * of its own" shape `../../components/shell/navItems.ts`'s header comment already documents for
 * this exact nav item). This form is this task's own minimal, honest way to reach a real `quoteId`
 * at all: it is deliberately smaller than `../documents/`'s own dropzone + 6-stage pipeline
 * animation (that UI answers a different, explicitly-designed screen; this one answers "how does a
 * person ever get here"), not a scaled-down clone of it.
 */
export default function UploadQuoteForm({ onUpload, submitting }: UploadQuoteFormProps) {
  const [file, setFile] = useState<File | null>(null);
  const [supplier, setSupplier] = useState("");
  const [currency, setCurrency] = useState("");
  const [geography, setGeography] = useState("");
  const [purchaseDate, setPurchaseDate] = useState("");
  const [error, setError] = useState<string | null>(null);

  const submit = async (candidate: File) => {
    setError(null);
    const fields: UploadQuoteFields = {
      supplier: supplier.trim() === "" ? undefined : supplier.trim(),
      currency: currency.trim() === "" ? undefined : currency.trim(),
      geography: geography.trim() === "" ? undefined : geography.trim(),
      purchaseDate: purchaseDate === "" ? undefined : purchaseDate,
    };
    const result = await onUpload(candidate, fields);
    if (!result.ok) {
      setError(result.error ?? "The quote could not be uploaded.");
    }
  };

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (file) void submit(file);
  };

  return (
    <div className="quote-upload">
      <p className="screen-kicker">R4</p>
      <h2 className="screen-title">Quote check</h2>
      <p className="micro-meta quote-upload-intro">
        Upload a supplier quote to extract its line items, match them to market benchmarks, and build
        a negotiation strategy. Supplier, currency, geography and purchase date are optional, but a
        quote uploaded without them cannot be matched against the Benchmark Service yet.
      </p>

      <form onSubmit={handleSubmit} className="quote-upload-form">
        <div className="field">
          <label htmlFor="quote-upload-file">Quote file</label>
          <input
            id="quote-upload-file"
            className="input"
            type="file"
            accept=".pdf,.docx,.xlsx"
            onChange={(event) => setFile(event.target.files?.[0] ?? null)}
          />
        </div>
        <div className="quote-upload-fields-grid">
          <div className="field">
            <label htmlFor="quote-upload-supplier">Supplier</label>
            <input id="quote-upload-supplier" className="input" value={supplier} onChange={(event) => setSupplier(event.target.value)} />
          </div>
          <div className="field">
            <label htmlFor="quote-upload-currency">Currency</label>
            <input
              id="quote-upload-currency"
              className="input"
              placeholder="e.g. CHF"
              value={currency}
              onChange={(event) => setCurrency(event.target.value)}
            />
          </div>
          <div className="field">
            <label htmlFor="quote-upload-geography">Geography</label>
            <input
              id="quote-upload-geography"
              className="input"
              placeholder="e.g. CH"
              value={geography}
              onChange={(event) => setGeography(event.target.value)}
            />
          </div>
          <div className="field">
            <label htmlFor="quote-upload-purchase-date">Purchase date</label>
            <input
              id="quote-upload-purchase-date"
              className="input"
              type="date"
              value={purchaseDate}
              onChange={(event) => setPurchaseDate(event.target.value)}
            />
          </div>
        </div>

        {error !== null && (
          <p className="hint" role="alert">
            {error}
          </p>
        )}

        <div className="quote-upload-actions">
          <button type="submit" className="btn btn-primary" disabled={!file || submitting}>
            {submitting ? "Uploading…" : "Upload & extract"}
          </button>
          <button
            type="button"
            className="btn btn-secondary"
            disabled={submitting}
            onClick={() => void submit(createSampleQuoteFile())}
          >
            Use sample file
          </button>
        </div>
      </form>
    </div>
  );
}
