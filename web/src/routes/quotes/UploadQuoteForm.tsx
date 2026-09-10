import { useRef, useState, type DragEvent } from "react";
import type { UploadQuoteFields } from "../../api/client";
import { SAMPLE_QUOTE_FIELDS, SAMPLE_QUOTE_LABEL, createSampleQuoteFile } from "./sampleQuote";

export interface UploadQuoteFormProps {
  onUpload: (file: File, fields: UploadQuoteFields) => Promise<{ ok: boolean; error?: string }>;
  submitting: boolean;
}

const ACCEPTED_TYPES = ".pdf,.docx,.xlsx";

/**
 * The V2 Quote check landing (screens-v2.md #9 "Empty: … + Upload a quote"; `contigo-v2/markup.html`
 * "QUOTE CHECK" block `noQuote`): one dashed card with the primary "Upload a quote" button and "or use
 * the sample: Databricks proposal Q-88213". A dropped or picked file uploads straight away -- there is
 * no separate submit step. The upload-time metadata the benchmark match needs (supplier · currency ·
 * geography · purchase date; all optional on `POST /api/quotes`) stays reachable under a compact
 * disclosure rather than as a form the reader has to fill first -- the prototype has no fields at all
 * because its data is already matched; the real Benchmark Service cannot match without them.
 */
export default function UploadQuoteForm({ onUpload, submitting }: UploadQuoteFormProps) {
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const [supplier, setSupplier] = useState("");
  const [currency, setCurrency] = useState("");
  const [geography, setGeography] = useState("");
  const [purchaseDate, setPurchaseDate] = useState("");
  const [dragging, setDragging] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const typedFields = (): UploadQuoteFields => ({
    supplier: supplier.trim() === "" ? undefined : supplier.trim(),
    currency: currency.trim() === "" ? undefined : currency.trim(),
    geography: geography.trim() === "" ? undefined : geography.trim(),
    purchaseDate: purchaseDate === "" ? undefined : purchaseDate,
  });

  const submit = async (candidate: File, fields: UploadQuoteFields) => {
    setError(null);
    const result = await onUpload(candidate, fields);
    if (!result.ok) {
      setError(result.error ?? "The quote could not be uploaded.");
    }
  };

  const handleDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setDragging(false);
    if (submitting) return;
    const dropped = event.dataTransfer.files?.[0];
    if (dropped) void submit(dropped, typedFields());
  };

  return (
    <div className="quote-drop">
      <div
        className={`quote-drop-card${dragging ? " is-dragging" : ""}`}
        onDragOver={(event) => {
          event.preventDefault();
          setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={handleDrop}
      >
        <label htmlFor="quote-upload-file" className="visually-hidden">
          Quote file
        </label>
        <input
          ref={fileInputRef}
          id="quote-upload-file"
          className="visually-hidden"
          type="file"
          accept={ACCEPTED_TYPES}
          disabled={submitting}
          onChange={(event) => {
            const picked = event.target.files?.[0];
            if (picked) void submit(picked, typedFields());
            event.target.value = "";
          }}
        />
        <button type="button" className="btn btn-primary" disabled={submitting} onClick={() => fileInputRef.current?.click()}>
          {submitting ? "Uploading…" : "Upload a quote"}
        </button>
        <span className="quote-drop-hint">
          or use the sample:{" "}
          <button
            type="button"
            className="btn btn-ghost quote-drop-sample"
            disabled={submitting}
            onClick={() => void submit(createSampleQuoteFile(), { ...typedFields(), ...SAMPLE_QUOTE_FIELDS })}
          >
            {SAMPLE_QUOTE_LABEL}
          </button>
        </span>
      </div>

      <details className="quote-drop-details">
        <summary>Supplier, currency, geography and purchase date (optional — needed for a market match)</summary>
        <div className="quote-drop-fields">
          <div className="field">
            <label htmlFor="quote-upload-supplier">Supplier</label>
            <input id="quote-upload-supplier" className="input" value={supplier} onChange={(event) => setSupplier(event.target.value)} />
          </div>
          <div className="field">
            <label htmlFor="quote-upload-currency">Currency</label>
            <input id="quote-upload-currency" className="input" placeholder="e.g. CHF" value={currency} onChange={(event) => setCurrency(event.target.value)} />
          </div>
          <div className="field">
            <label htmlFor="quote-upload-geography">Geography</label>
            <input id="quote-upload-geography" className="input" placeholder="e.g. CH" value={geography} onChange={(event) => setGeography(event.target.value)} />
          </div>
          <div className="field">
            <label htmlFor="quote-upload-purchase-date">Purchase date</label>
            <input id="quote-upload-purchase-date" className="input" type="date" value={purchaseDate} onChange={(event) => setPurchaseDate(event.target.value)} />
          </div>
        </div>
      </details>

      {error !== null && (
        <p className="hint" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
