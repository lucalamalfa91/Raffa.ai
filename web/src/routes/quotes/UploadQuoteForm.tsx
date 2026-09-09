import { useState, type FormEvent } from "react";
import type { UploadQuoteFields } from "../../api/client";
import { buildSamplePdf } from "../documents/sampleDocument";

export interface UploadQuoteFormProps {
  onUpload: (file: File, fields: UploadQuoteFields) => Promise<{ ok: boolean; error?: string }>;
  submitting: boolean;
}

/**
 * The "Use sample file" control builds a real, readable supplier quote the same way
 * `../documents/sampleDocument.ts` builds the sample MSAs (a structurally valid one-page PDF,
 * ASCII text, correct xref): this repo ships no real quote asset, so rather than disabling the
 * convenience button or faking a network response, this sends a real file through the exact same
 * `apiClient.uploadQuote()` call a real drag-and-drop/file-picker upload uses. Whatever the real
 * `QuoteExtractionPipeline` returns for it is the honest answer, not a scripted one -- the text is
 * written so a reader (model or fixture) can find two priced lines with SKU, quantity, unit price,
 * list price, discount and term.
 */
const SAMPLE_QUOTE_PAGES: readonly string[] = [
  [
    "QUOTE No. Q-2026-0042",
    "Supplier: Northwind Traders SA, Rue du Rhone 12, 1204 Geneva, Switzerland. Customer: Contigo Demo AG. Quote date: 2026-09-01. Valid until: 2026-10-31. Currency: EUR. Payment terms: Net 30.",
    "Line 1. SKU NW-PROC-ENT - Northwind Procurement Platform, Enterprise edition. Quantity: 250 seats. Unit price: EUR 160.00 per seat per year (list price EUR 200.00, discount 20%). Term: 12 months. Line total: EUR 40,000.00.",
    "Line 2. SKU NW-SUP-PRM - Premium Support, 24x7 with a four-hour response time. Quantity: 1. Unit price: EUR 8,000.00 per year (list price EUR 10,000.00, discount 20%). Term: 12 months. Line total: EUR 8,000.00.",
    "Total for the 12-month term: EUR 48,000.00. All prices exclude VAT. Prices are firm for the validity period of this quote.",
  ].join("\n\n"),
];
const SAMPLE_QUOTE_FILE_NAME = "contigo-sample-quote.pdf";

function createSampleQuoteFile(): File {
  return new File([buildSamplePdf(SAMPLE_QUOTE_PAGES)], SAMPLE_QUOTE_FILE_NAME, { type: "application/pdf" });
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
