import { useCallback, useEffect, useRef, useState } from "react";
import type { ApiClient } from "../../api/client";
import {
  clampWindow,
  DEFAULT_START_WINDOW_DAYS,
  MAX_RULES_LENGTH,
  MAX_START_WINDOW_DAYS,
  MIN_START_WINDOW_DAYS,
  type CompanyRulesBody,
  type PolicyProposal,
  type RuleTurn,
} from "../../api/companyRules";
import { useTenantId } from "../../routes/contracts/contracts4";
import { useI18n } from "../../i18n/I18nProvider";
import { notifyTodayChanged } from "../today/loadToday";
import "./settings4.css";

export interface SettingsRouteProps {
  apiClient: ApiClient;
}

/** A rules proposal waiting for the user to keep it (from the interview or an uploaded policy). */
interface Proposal {
  source: "interview" | "policy";
  text: string;
  startWindowDays: number | null;
  note: string;
  fileName?: string;
}

const POLICY_ACCEPT = ".pdf,.docx,.txt,.md";

/**
 * Impostazioni: the company rules Raffa follows to decide what to alert about, and when. Three ways in,
 * all ending in the same editable text -- write it, upload the policy Raffa reads, or answer Raffa's
 * interview. Nothing a proposal says is saved until the user saves; only the Head of Procurement writes.
 */
export default function SettingsRoute({ apiClient }: SettingsRouteProps) {
  const tenantId = useTenantId() ?? undefined;
  const { t, lang } = useI18n();
  const [loaded, setLoaded] = useState<CompanyRulesBody | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [text, setText] = useState("");
  const [windowDays, setWindowDays] = useState<string>(String(DEFAULT_START_WINDOW_DAYS));
  const [sourceFile, setSourceFile] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [savedNote, setSavedNote] = useState<string | null>(null);

  const [turns, setTurns] = useState<RuleTurn[]>([]);
  const [answer, setAnswer] = useState("");
  const [thinking, setThinking] = useState(false);
  const [interviewOn, setInterviewOn] = useState(false);
  const [proposal, setProposal] = useState<Proposal | null>(null);
  const [reading, setReading] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);

  const apply = useCallback((body: CompanyRulesBody) => {
    setLoaded(body);
    setText(body.rules.text);
    setWindowDays(String(body.rules.startWindowDays));
    setSourceFile(body.rules.sourceFile);
  }, []);

  useEffect(() => {
    if (!tenantId || !apiClient.getCompanyRules) return;
    let alive = true;
    void apiClient.getCompanyRules(tenantId).then((r) => {
      if (!alive) return;
      if (r.ok) apply(r.body);
      else setError(r.error);
    });
    return () => {
      alive = false;
    };
  }, [apiClient, tenantId, apply]);

  const canEdit = loaded?.canEdit ?? false;
  const parsedWindow = Number(windowDays);
  const windowValid = Number.isInteger(parsedWindow) && parsedWindow >= MIN_START_WINDOW_DAYS && parsedWindow <= MAX_START_WINDOW_DAYS;
  const dirty =
    loaded !== null &&
    (text.trim() !== loaded.rules.text.trim() ||
      (windowValid && parsedWindow !== loaded.rules.startWindowDays) ||
      sourceFile !== loaded.rules.sourceFile);
  const tooLong = text.length > MAX_RULES_LENGTH;

  const save = useCallback(async () => {
    if (!tenantId || !apiClient.saveCompanyRules || !windowValid || tooLong) return;
    setSaving(true);
    setError(null);
    const r = await apiClient.saveCompanyRules(tenantId, { text, startWindowDays: clampWindow(parsedWindow), sourceFile });
    setSaving(false);
    if (r.ok) {
      apply(r.body);
      setSavedNote(t("Saved. Raffa uses these rules from now on."));
      notifyTodayChanged();
    } else {
      setError(r.error);
    }
  }, [apiClient, tenantId, windowValid, tooLong, text, parsedWindow, sourceFile, apply, t]);

  const discard = useCallback(() => {
    if (loaded) apply(loaded);
    setSavedNote(null);
  }, [loaded, apply]);

  const keepProposal = useCallback(() => {
    if (!proposal) return;
    setText(proposal.text);
    if (proposal.startWindowDays) setWindowDays(String(proposal.startWindowDays));
    if (proposal.source === "policy" && proposal.fileName) setSourceFile(proposal.fileName);
    setProposal(null);
    setSavedNote(null);
  }, [proposal]);

  const turn = useCallback(
    async (history: RuleTurn[]) => {
      if (!tenantId || !apiClient.interviewCompanyRules) return;
      setThinking(true);
      setError(null);
      const r = await apiClient.interviewCompanyRules(tenantId, history, text, lang);
      setThinking(false);
      if (!r.ok) {
        setError(r.error);
        return;
      }
      setTurns([...history, { role: "raffa", text: r.body.message }]);
      if (r.body.done && r.body.proposal) {
        setProposal({ source: "interview", text: r.body.proposal, startWindowDays: r.body.startWindowDays, note: r.body.message });
      }
    },
    [apiClient, tenantId, text, lang],
  );

  const startInterview = useCallback(() => {
    setInterviewOn(true);
    setProposal(null);
    setTurns([]);
    void turn([]);
  }, [turn]);

  const send = useCallback(() => {
    const value = answer.trim();
    if (!value || thinking) return;
    setAnswer("");
    void turn([...turns, { role: "user", text: value }]);
  }, [answer, thinking, turns, turn]);

  const upload = useCallback(
    async (file: File) => {
      if (!tenantId || !apiClient.uploadCompanyPolicy) return;
      setReading(true);
      setError(null);
      const r = await apiClient.uploadCompanyPolicy(tenantId, file, lang);
      setReading(false);
      if (fileInput.current) fileInput.current.value = "";
      if (r.ok) {
        const p: PolicyProposal = r.body;
        setInterviewOn(false);
        setProposal({ source: "policy", text: p.text, startWindowDays: p.startWindowDays, note: p.summary, fileName: p.fileName });
      } else {
        setError(r.error);
      }
    },
    [apiClient, tenantId, lang],
  );

  if (!loaded) {
    return (
      <div className="s4-page" data-screen-label="Impostazioni">
        {error ? <div className="s4-error">{error}</div> : null}
      </div>
    );
  }

  const rules = loaded.rules;
  const status = rules.isDefault
    ? t("No rules saved yet. Raffa uses its defaults: a renewal shows on Today {n} days before its notice deadline.", { n: DEFAULT_START_WINDOW_DAYS })
    : rules.sourceFile
      ? t("From {file} · saved by {who}", { file: rules.sourceFile, who: rules.updatedBy ?? "—" })
      : t("Saved by {who}", { who: rules.updatedBy ?? "—" });

  return (
    <div className="s4-page" data-screen-label="Impostazioni">
      <h6 className="s4-role">{t("Settings")}</h6>
      <h2 className="s4-title">{t("Company rules")}</h2>
      <div className="s4-sub">
        {t("Raffa follows these rules to decide what to tell you, and when. Write them, upload your policy, or let Raffa interview you.")}
      </div>
      {!canEdit ? <div className="s4-note">{t("Only the Head of Procurement can change the company rules.")}</div> : null}

      <div className="s4-grid">
        <section className="s4-main" aria-labelledby="s4-rules-h">
          <h3 id="s4-rules-h" className="s4-h">
            {t("The rules")}
          </h3>
          <div className="s4-status">{status}</div>
          <label className="s4-label" htmlFor="s4-text">
            {t("Rules, in your own words")}
          </label>
          <textarea
            id="s4-text"
            className="input s4-text"
            value={text}
            disabled={!canEdit}
            placeholder={t("For example: Start a renewal 90 days before the notice deadline. No auto-renewal above CHF 100k. The CFO approves anything above CHF 250k.")}
            onChange={(e) => {
              setText(e.target.value);
              setSavedNote(null);
            }}
          />
          <div className={tooLong ? "s4-count is-over" : "s4-count"}>
            {text.length} / {MAX_RULES_LENGTH}
          </div>

          <div className="s4-window field">
            <label htmlFor="s4-window">{t("Lead time: days before a notice deadline a renewal shows on Today")}</label>
            <input
              id="s4-window"
              className="input s4-window-input"
              type="number"
              inputMode="numeric"
              min={MIN_START_WINDOW_DAYS}
              max={MAX_START_WINDOW_DAYS}
              value={windowDays}
              disabled={!canEdit}
              aria-invalid={!windowValid}
              onChange={(e) => {
                setWindowDays(e.target.value);
                setSavedNote(null);
              }}
            />
            <div className="s4-hint">
              {windowValid
                ? t("Earlier than this, the renewal stays in Renewals and Raffa does not bring it up.")
                : t("Between {min} and {max} days.", { min: MIN_START_WINDOW_DAYS, max: MAX_START_WINDOW_DAYS })}
            </div>
          </div>

          {canEdit ? (
            <div className="s4-actions">
              <button type="button" className="btn btn-primary" disabled={!dirty || !windowValid || tooLong || saving} onClick={() => void save()}>
                {saving ? t("Saving…") : t("Save the rules")}
              </button>
              <button type="button" className="btn btn-ghost" disabled={!dirty || saving} onClick={discard}>
                {t("Discard changes")}
              </button>
              {savedNote ? (
                <span className="s4-saved" role="status">
                  {savedNote}
                </span>
              ) : null}
            </div>
          ) : null}
          {error ? (
            <div className="s4-error" role="alert">
              {error}
            </div>
          ) : null}
        </section>

        {canEdit ? (
          <aside className="s4-side">
            <section className="s4-card" aria-labelledby="s4-policy-h">
              <h3 id="s4-policy-h" className="s4-h">
                {t("Start from your policy")}
              </h3>
              <p className="s4-p">{t("Upload the procurement policy. Raffa reads it and proposes the rules; you check them before saving.")}</p>
              <input
                ref={fileInput}
                type="file"
                accept={POLICY_ACCEPT}
                className="s4-file"
                aria-label={t("Upload a policy")}
                disabled={reading}
                onChange={(e) => {
                  const f = e.target.files?.[0];
                  if (f) void upload(f);
                }}
              />
              <button type="button" className="btn btn-secondary" disabled={reading} onClick={() => fileInput.current?.click()}>
                {reading ? t("Raffa is reading…") : t("Upload a policy")}
              </button>
              <div className="s4-hint">{t("PDF, Word or text, up to 10 MB.")}</div>
            </section>

            <section className="s4-card" aria-labelledby="s4-interview-h">
              <h3 id="s4-interview-h" className="s4-h">
                {t("Let Raffa interview you")}
              </h3>
              <p className="s4-p">
                {t("No policy? Answer a few questions and Raffa writes the rules with you. Already have rules? Raffa refines them.")}
              </p>
              {!interviewOn ? (
                <button type="button" className="btn btn-secondary" onClick={startInterview}>
                  {text.trim() ? t("Refine the rules with Raffa") : t("Start the interview")}
                </button>
              ) : (
                <div className="s4-chat">
                  <ol className="s4-turns" aria-live="polite">
                    {turns.map((m, i) => (
                      <li key={i} className={m.role === "raffa" ? "s4-turn is-raffa" : "s4-turn is-user"}>
                        <span className="s4-who">{m.role === "raffa" ? "Raffa" : t("You")}</span>
                        {m.text}
                      </li>
                    ))}
                    {thinking ? <li className="s4-turn is-raffa s4-thinking">{t("Raffa is thinking…")}</li> : null}
                  </ol>
                  {!proposal ? (
                    <form
                      className="s4-reply"
                      onSubmit={(e) => {
                        e.preventDefault();
                        send();
                      }}
                    >
                      <textarea
                        className="input s4-answer"
                        value={answer}
                        aria-label={t("Your answer")}
                        disabled={thinking}
                        onChange={(e) => setAnswer(e.target.value)}
                        onKeyDown={(e) => {
                          if (e.key === "Enter" && !e.shiftKey) {
                            e.preventDefault();
                            send();
                          }
                        }}
                      />
                      <button type="submit" className="btn btn-primary" disabled={!answer.trim() || thinking}>
                        {t("Send")}
                      </button>
                    </form>
                  ) : null}
                </div>
              )}
            </section>
          </aside>
        ) : null}
      </div>

      {proposal ? (
        <section className="s4-proposal" aria-labelledby="s4-proposal-h">
          <h3 id="s4-proposal-h" className="s4-h">
            {proposal.source === "policy" ? t("What Raffa took from {file}", { file: proposal.fileName ?? "" }) : t("The rules Raffa wrote")}
          </h3>
          <p className="s4-p">{proposal.note}</p>
          <pre className="s4-proposal-text">{proposal.text}</pre>
          {proposal.startWindowDays ? (
            <div className="s4-hint">{t("Lead time: {n} days before a notice deadline.", { n: proposal.startWindowDays })}</div>
          ) : null}
          <div className="s4-actions">
            <button type="button" className="btn btn-primary" onClick={keepProposal}>
              {t("Use these rules")}
            </button>
            <button type="button" className="btn btn-ghost" onClick={() => setProposal(null)}>
              {t("Discard")}
            </button>
          </div>
          <div className="s4-hint">{t("They go into the text above. Nothing changes until you save.")}</div>
        </section>
      ) : null}
    </div>
  );
}
