import { useCallback, useEffect, useRef, useState } from "react";
import { Navigate, useNavigate, useParams } from "react-router-dom";
import type { ApiClient, DocumentReadingBody, ReadingFieldBody } from "../../../api/client";
import { useGuide } from "../../../components/shell/guide";
import { useFixtureSession } from "../../../fixtures/FixtureSessionContext";
import { useI18n } from "../../../i18n/I18nProvider";
import type { Translate } from "../../../i18n/translate";
import { announceContractsChanged, tx, useTenantId } from "../contracts4";
import { reviewPath, useContractUpload } from "../useContractUpload";
import ReviewPagePane, { type PageHighlight } from "./ReviewPagePane";
import "../contracts4.css";

export interface ReviewRouteProps {
  apiClient: ApiClient;
}

/** While the document is being read (prototype: one stage every 600 ms). */
const POLL_MS = 300;
/** Once a contract is validated, how long before the next one to verify opens by itself. */
export const ADVANCE_SECONDS = 4;

type ReadingBasisBody = NonNullable<ReadingFieldBody["basis"]>;

/** What is still waiting after this document: the next one to open and how many are left. */
interface Queue {
  /** Documents still to validate (waiting for review or being read), this one included while it is open. */
  remaining: number;
  /** The next document to open, other than this one. */
  next: { documentId: string; file: string } | null;
}

const BASIS_KIND: Record<ReadingBasisBody["kind"], string> = {
  stated: "Written in the contract",
  derived: "Worked out from other passages",
  notStated: "Not written in the contract",
};

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

/** Failure ids the API sends (`DocumentReadingModels.FailureFor`): the admission gate's two rejections. */
const NOT_A_CONTRACT = "This file does not look like a contract. Upload another file.";
const NO_READABLE_TEXT = "This file has no readable text (a scan without text?). Upload another version.";

interface FailureCopy {
  title: string;
  text: string;
  action: string;
}

/** Why the reading stopped, in words the user can act on: a title, what happened, what to do. */
function failureCopy(t: Translate, reading: DocumentReadingBody): FailureCopy {
  switch (reading.failure?.id) {
    case NOT_A_CONTRACT:
      return {
        title: t("This file is not a contract"),
        text: t("Raffa reads contracts only: agreements, order forms, amendments, renewal letters. This document looks like something else (a receipt, an invoice, a letter…), so no contract was created from it."),
        action: t("Upload a contract"),
      };
    case NO_READABLE_TEXT:
      return {
        title: t("This file has no readable text"),
        text: t("It looks like a scan or a photo without a text layer, so Raffa cannot read it. Upload a PDF with selectable text or a sharper copy."),
        action: t("Upload another version"),
      };
    default:
      return {
        title: t("The reading did not finish"),
        text: reading.failure ? tx(t, reading.failure) : t("The reading did not finish. Upload another version or try again."),
        action: t("Upload another version"),
      };
  }
}

function title(t: Translate, reading: DocumentReadingBody): string {
  if (reading.status === "processing") return t("Raffa is reading the contract");
  if (reading.status === "failed") return failureCopy(t, reading).title;
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
  const [queue, setQueue] = useState<Queue | null>(null);
  const [page, setPage] = useState(1);
  const [countdown, setCountdown] = useState<number | null>(null);

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
  const fieldKey = field?.key;
  const fieldPage = field?.page ?? null;

  // Each field opens on the page it was read on.
  useEffect(() => {
    if (fieldKey !== undefined) setPage(fieldPage ?? 1);
  }, [fieldKey, fieldPage]);

  /** The Contratti banner's counts: what is still to validate and which document comes next. */
  const loadQueue = useCallback(async () => {
    if (!tenantId) return;
    const result = await apiClient.listContractsV4(tenantId);
    const doc = result.ok ? result.data?.document : null;
    if (!doc) {
      setQueue({ remaining: 0, next: null });
      return;
    }
    const remaining = Math.max((doc.toReview ?? 0) + (doc.reading ?? 0), 1);
    setQueue({ remaining, next: doc.documentId === documentId ? null : { documentId: doc.documentId, file: doc.file } });
  }, [apiClient, tenantId, documentId]);

  const status = reading?.status;
  useEffect(() => {
    if (status === "review" || status === "done") void loadQueue();
  }, [status, loadQueue]);

  // Validated with more to do: the next contract opens by itself after a short pause.
  const nextId = status === "done" ? queue?.next?.documentId ?? null : null;
  useEffect(() => {
    if (nextId === null) {
      setCountdown(null);
      return undefined;
    }
    setCountdown(ADVANCE_SECONDS);
    const timer = window.setInterval(() => {
      setCountdown((c) => (c === null ? null : c - 1));
    }, 1000);
    return () => window.clearInterval(timer);
  }, [nextId]);
  useEffect(() => {
    if (countdown === 0 && nextId !== null) navigate(reviewPath(nextId));
  }, [countdown, nextId, navigate]);

  const highlights: PageHighlight[] = [];
  if (field?.box && field.page) highlights.push({ key: field.key, page: field.page, box: field.box, tone: "field" });
  for (const source of field?.basis?.sources ?? []) {
    if (source.box && source.page) highlights.push({ key: `src-${source.key}`, page: source.page, box: source.box, tone: "source" });
  }

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
      if (result.data.status === "done") {
        // The queue read during the review counted this document: read it again once validated.
        setQueue(null);
        announceContractsChanged();
      }
    } else {
      // A server failure saved nothing (the reading stays in review): say so plainly, not the raw response.
      setDecisionError(result.statusCode !== null && result.statusCode >= 500 ? "" : (result.error ?? ""));
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
          {queue && queue.remaining > 1 && reading.status !== "done" ? (
            <div className="c4-queue" data-testid="review-queue">
              {t("{n} contracts to validate, this one included", { n: queue.remaining })}
            </div>
          ) : null}
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
        <div className="c4-failed" role="alert">
          <div className="c4-done-text">{failureCopy(t, reading).text}</div>
          <div className="c4-done-actions">
            <button type="button" className="btn btn-primary" disabled={upload.busy} onClick={upload.start}>
              {failureCopy(t, reading).action}
            </button>
            <button type="button" className="btn btn-secondary" onClick={() => navigate("/contracts")}>
              {t("Back to contracts")}
            </button>
          </div>
          {upload.error ? <div className="c4-error">{tx(t, { id: upload.error })}</div> : null}
        </div>
      ) : null}

      {reading?.status === "review" && field ? (
        <div className="c4-review-grid">
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
            <div className="c4-passage">
              <div className="c4-passage-head">
                <span className="c4-label">{t("The passage I read")}</span>
                {field.page ? (
                  <button type="button" className="btn btn-ghost c4-page-link" onClick={() => setPage(field.page ?? 1)}>
                    {t("see page {page}", { page: field.page })}
                  </button>
                ) : null}
              </div>
              <div className="c4-quote">
                {field.quote.before}
                <mark>{field.quote.mark}</mark>
                {field.quote.after}
              </div>
            </div>
          ) : null}
          {field.basis ? <Basis t={t} basis={field.basis} onPage={setPage} /> : null}
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
        {tenantId ? (
          <ReviewPagePane
            apiClient={apiClient}
            tenantId={tenantId}
            documentId={reading.documentId}
            page={page}
            onPage={setPage}
            highlights={highlights}
          />
        ) : null}
        </div>
      ) : null}

      {reading?.status === "done" ? (
        <div className="c4-done">
          <div className="c4-done-title">{t("Contract validated.")}</div>
          <div className="c4-done-text">
            {t("From now on it feeds Today, Renewals and Contracts. Its notice deadline is already among your priorities.")}
          </div>
          {queue?.next ? (
            <div className="c4-queue-next" aria-live="polite" data-testid="review-next">
              <strong>
                {queue.remaining === 1
                  ? t("1 contract still to validate")
                  : t("{n} contracts still to validate", { n: queue.remaining })}
              </strong>
              {" · "}
              {t("next: {file}", { file: queue.next.file })}
              {countdown !== null && countdown > 0 ? (
                <> · {t("it opens in {s} s", { s: countdown })}</>
              ) : null}
            </div>
          ) : queue ? (
            <div className="c4-queue-next" data-testid="review-all-done">
              <strong>{t("All your contracts are validated.")}</strong>
            </div>
          ) : null}
          <div className="c4-done-actions">
            {queue?.next ? (
              <button type="button" className="btn btn-primary" onClick={() => navigate(reviewPath(queue.next!.documentId))}>
                {t("Next contract now")}
              </button>
            ) : null}
            <button
              type="button"
              className={queue?.next ? "btn btn-secondary" : "btn btn-primary"}
              onClick={() => reading.contractId && navigate(`/contracts/${encodeURIComponent(reading.contractId)}`)}
            >
              {t("Open the contract")}
            </button>
            {queue && !queue.next ? (
              <button type="button" className="btn btn-secondary" onClick={() => navigate("/contracts")}>
                {t("Back to contracts")}
              </button>
            ) : null}
          </div>
        </div>
      ) : null}
    </div>
  );
}

/**
 * Raffa's reasoning behind the value: written in the passage, worked out from other passages (each
 * one quoted, with a link to its page), or not written anywhere. It tells the user what to compare.
 */
function Basis({ t, basis, onPage }: { t: Translate; basis: ReadingBasisBody; onPage: (page: number) => void }) {
  return (
    <div className={`c4-basis is-${basis.kind}`} data-testid="review-basis">
      <div className="c4-basis-head">
        <span className="c4-label">{t("How I read it")}</span>
        <span className="c4-basis-kind">{t(BASIS_KIND[basis.kind])}</span>
      </div>
      <p className="c4-basis-text">{tx(t, basis.explanation)}</p>
      {basis.sources.length > 0 ? (
        <ul className="c4-sources">
          {basis.sources.map((source) => (
            <li key={source.key} className="c4-source">
              <div className="c4-source-head">
                <span>
                  {tx(t, source.label)}: <strong>{source.value}</strong>
                </span>
                {source.page ? (
                  <button type="button" className="btn btn-ghost c4-page-link" onClick={() => onPage(source.page ?? 1)}>
                    {t("see page {page}", { page: source.page })}
                  </button>
                ) : null}
              </div>
              {source.quote ? (
                <div className="c4-quote is-source">
                  {source.quote.before}
                  <mark>{source.quote.mark}</mark>
                  {source.quote.after}
                </div>
              ) : null}
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
