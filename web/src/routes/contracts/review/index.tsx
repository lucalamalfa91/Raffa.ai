import { useCallback, useEffect, useRef, useState } from "react";
import { Navigate, useNavigate, useParams } from "react-router-dom";
import type { ApiClient, DocumentReadingBody, ReadingFieldBody } from "../../../api/client";
import { useGuide } from "../../../components/shell/guide";
import { useFixtureSession } from "../../../fixtures/FixtureSessionContext";
import { useI18n } from "../../../i18n/I18nProvider";
import type { Translate } from "../../../i18n/translate";
import { announceContractsChanged, tx, useTenantId } from "../contracts4";
import { useContractUpload } from "../useContractUpload";
import "../contracts4.css";

export interface ReviewRouteProps {
  apiClient: ApiClient;
}

/** While the document is being read (prototype: one stage every 600 ms). */
const POLL_MS = 300;

/**
 * `/contracts/review/:documentId` — AskRaffa 4.0 Revisione. `new` starts an upload (the sample in
 * fixture mode; the Documents upload otherwise) and lands on the document's own review.
 */
export default function ReviewRoute({ apiClient }: ReviewRouteProps) {
  const { documentId = "" } = useParams();
  if (documentId === "new") return <NewUpload apiClient={apiClient} />;
  return <Review key={documentId} apiClient={apiClient} documentId={documentId} />;
}

function NewUpload({ apiClient }: ReviewRouteProps) {
  const fixture = useFixtureSession();
  const upload = useContractUpload(apiClient);
  const started = useRef(false);
  useEffect(() => {
    if (!fixture || started.current) return;
    started.current = true;
    upload.start();
  }, [fixture, upload]);
  // Outside fixture mode a file picker needs the user's own click: the Documents screen has it.
  return fixture ? null : <Navigate to="/documents" replace />;
}

function title(t: Translate, reading: DocumentReadingBody): string {
  if (reading.status === "processing") return t("Raffa is reading the contract");
  if (reading.status === "failed") return t("The reading did not finish");
  if (reading.status === "done") return t("{supplier} validated", { supplier: reading.supplier ?? reading.file });
  const n = reading.fields.length;
  if (n === 1) return t("One field to verify");
  if (n === 2) return t("Two fields to verify — one at a time");
  return t("{n} fields to verify — one at a time", { n });
}

/**
 * Revisione (AskRaffa V4.dc.html lines 297-303): while reading, an 18px spinner and the stage; then
 * the doubtful fields **one at a time** (`Campo n di N`, label, what was read and where, what to
 * check, the quote with the words highlighted, *Accetta: è corretto / Correggi*); then *Contratto
 * validato.* A high confidence never replaces the user's check: every flagged field is asked.
 * Data: `GET /api/documents/{id}/reading`, `POST …/reading/fields/{key}`.
 */
function Review({ apiClient, documentId }: ReviewRouteProps & { documentId: string }) {
  const { t } = useI18n();
  const navigate = useNavigate();
  const tenantId = useTenantId();
  const upload = useContractUpload(apiClient);
  const [reading, setReading] = useState<DocumentReadingBody | null>(null);
  const [failed, setFailed] = useState(false);
  const [correcting, setCorrecting] = useState(false);
  const [corrected, setCorrected] = useState("");
  const [busy, setBusy] = useState(false);
  const [decisionError, setDecisionError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!tenantId) return;
    const result = await apiClient.getDocumentReading(tenantId, documentId);
    if (result.ok && result.data) {
      setReading(result.data);
      setFailed(false);
    } else {
      setFailed(true);
    }
  }, [apiClient, tenantId, documentId]);

  useEffect(() => {
    void load();
  }, [load]);

  const processing = reading?.status === "processing";
  useEffect(() => {
    if (!processing) return undefined;
    const timer = window.setInterval(() => void load(), POLL_MS);
    return () => window.clearInterval(timer);
  }, [processing, load]);

  const field: ReadingFieldBody | undefined = reading?.fields.find((f) => f.decision === null);
  const index = reading && field ? reading.fields.indexOf(field) : -1;

  const decide = async (decision: "accept" | "correct") => {
    if (!tenantId || !reading || !field || busy) return;
    setBusy(true);
    setDecisionError(null);
    const value = decision === "correct" ? corrected.trim() || field.value : null;
    const result = await apiClient.decideReadingField(tenantId, reading.documentId, field.key, { decision, value });
    setBusy(false);
    if (result.ok && result.data) {
      setReading(result.data);
      setCorrecting(false);
      setCorrected("");
      if (result.data.status === "done") announceContractsChanged();
    } else {
      setDecisionError(result.error ?? "");
    }
  };

  const heading = reading ? title(t, reading) : "";
  useGuide({
    where: t("Document⟦guide⟧"),
    now: heading,
    why: t("I ask you only about uncertain or conflicting fields; a high score never replaces your check."),
    next: t("Once validated, the contract enters Today, Renewals and Contracts."),
  });

  return (
    <div className="c4-page is-review" data-screen-label="Revisione">
      <button type="button" className="btn btn-ghost c4-back" onClick={() => navigate("/contracts")}>
        ← {t("Contracts")}
      </button>
      {reading ? (
        <>
          <h6 className="c4-doc-kicker">
            {t("Document · {file}", { file: reading.file })}
          </h6>
          <h2>{heading}</h2>
        </>
      ) : null}

      {failed && !reading ? (
        <div className="c4-error" role="alert">
          <span>{t("The document could not be loaded.")}</span>
          <button type="button" className="btn btn-secondary" onClick={() => void load()}>
            {t("Try again")}
          </button>
        </div>
      ) : null}

      {reading?.status === "processing" ? (
        <>
          <div className="c4-reading" aria-live="polite">
            <span className="c4-spinner" aria-hidden="true" />
            {tx(t, reading.stageLabel)}…
          </div>
          <div className="c4-reading-note">
            {t("You can leave: the work stays saved. When the reading is done I ask you only about the fields I am not sure of.")}
          </div>
        </>
      ) : null}

      {reading?.status === "failed" ? (
        <div className="c4-error" role="alert">
          <span>{tx(t, reading.failure)}</span>
          <button type="button" className="btn btn-secondary" onClick={upload.start}>
            {t("Upload another version")}
          </button>
        </div>
      ) : null}

      {reading?.status === "review" && field ? (
        <div className="c4-field">
          <div className="c4-field-kicker">
            {t("Field {n} of {total} · to verify", { n: index + 1, total: reading.fields.length })}
          </div>
          <div className="c4-field-label">{tx(t, field.label)}</div>
          <div className="c4-field-read">
            <span className="c4-label">{t("Read")}</span> <strong>{field.value}</strong>{" "}
            {field.src ? <span className="c4-field-src">· {field.src}</span> : null}
          </div>
          <div className="c4-field-why">
            <span className="c4-label">{t("What to check")}</span> {tx(t, field.why)}
          </div>
          {field.quote ? (
            <div className="c4-quote">
              {field.quote.before}
              <mark>{field.quote.mark}</mark>
              {field.quote.after}
            </div>
          ) : null}
          {correcting ? (
            <input
              className="input c4-correct-input"
              placeholder={t("Corrected value")}
              aria-label={t("Corrected value")}
              value={corrected}
              onChange={(e) => setCorrected(e.target.value)}
              autoFocus
            />
          ) : null}
          {decisionError !== null ? (
            <div className="c4-error" role="alert">
              <span>{decisionError || t("The decision was not saved.")}</span>
            </div>
          ) : null}
          <div className="c4-field-actions">
            <button
              type="button"
              className="btn btn-primary"
              disabled={busy}
              onClick={() => void decide(correcting ? "correct" : "accept")}
            >
              {correcting ? t("Save the corrected value") : t("Accept: it is correct")}
            </button>
            <button
              type="button"
              className="btn btn-secondary"
              disabled={busy}
              onClick={() => {
                setCorrected(field.value);
                setCorrecting((c) => !c);
              }}
            >
              {correcting ? t("Cancel") : t("Correct")}
            </button>
          </div>
        </div>
      ) : null}

      {reading?.status === "done" ? (
        <div className="c4-done">
          <div className="c4-done-title">{t("Contract validated.")}</div>
          <div className="c4-done-text">
            {t("From now on it feeds Today, Renewals and Contracts. Its notice deadline is already among your priorities.")}
          </div>
          <div className="c4-done-actions">
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => reading.contractId && navigate(`/contracts/${encodeURIComponent(reading.contractId)}`)}
            >
              {t("Open the contract")}
            </button>
            <button type="button" className="btn btn-secondary" onClick={() => navigate("/today")}>
              {t("Back to Today")}
            </button>
          </div>
        </div>
      ) : null}
    </div>
  );
}
