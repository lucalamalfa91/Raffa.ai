import { useCallback, useEffect, useRef, useState, type CSSProperties } from "react";
import { useNavigate, useParams } from "react-router-dom";
import AskShortcuts from "../../../components/ask/AskShortcuts";
import type { ApiClient } from "../../../api/client";
import type { CaseActionPayload, CaseButton, CaseCardStep, CaseEnvelope, CaseInput, CaseRow, Tone } from "../../../api/renewalCases";
import { useGuide, useGuideState } from "../../../components/shell/guide";
import ArtifactPanel from "../../ask/ArtifactPanel";
import ReplyBody from "../../ask/ReplyBody";
import { MailIcon } from "../../ask/DraftPanel";
import "../../ask/chat.css";
import { useTenantId } from "../../../routes/contracts/contracts4";
import { useI18n } from "../../../i18n/I18nProvider";
import { resolveText, type Text } from "../../../i18n/translate";
import { notifyTodayChanged } from "../../today/loadToday";
import "./case4.css";

export interface CaseRouteProps {
  apiClient: ApiClient;
}

/** Engine tones → the prototype's colours. */
export function toneColor(tone: Tone | undefined): string {
  switch (tone) {
    case "urgent":
      return "var(--color-accent)";
    case "alert":
      return "var(--color-accent-700)";
    case "muted":
      return "var(--color-neutral-600)";
    case "strong":
      return "var(--color-text)";
    default:
      return "inherit";
  }
}

const stepStyle = (s: CaseCardStep): CSSProperties => ({
  background: s.now ? "var(--color-text)" : s.on ? "var(--color-neutral-200)" : "transparent",
  color: s.now ? "var(--color-bg)" : s.on ? "var(--color-text)" : "var(--color-neutral-600)",
});

const rowStyle = (r: { w?: number | null; tone?: Tone }): CSSProperties => ({ fontWeight: r.w ?? 400, color: toneColor(r.tone) });

/** A scripted demo shortcut ("Demo: …", "… (demo)"): shown only in fixture mode. */
const demoLabel = (b: CaseButton): boolean => {
  const id = typeof b.label === "string" ? b.label : b.label.id;
  return /^Demo: |\(demo\)$/.test(id);
};

/** A button that adds a document to the case (the contract draft, the signed contract): in a real workspace it takes a file. */
const isDocButton = (b: CaseButton): boolean => b.act === "addDoc";

const isEmpty = (x: Text | null | undefined): boolean => x == null || (typeof x === "string" && x.length === 0);

/** The source line the server appends to a composer answer: "(Source: …)" / "(Fonte: …)". */
const SOURCE_LINE = /\n\((?:Source|Fonte): ([^\n]+)\)\s*$/;

/**
 * A Raffa bubble in the case chat: the answer's markdown rendered as in Ask (paragraphs, bold, lists,
 * `[n]` markers) and its source on its own muted line, never the raw `**…**` text.
 */
export function RaffaBubble({ text }: { text: string }) {
  const { t } = useI18n();
  const m = SOURCE_LINE.exec(text);
  const body = m ? text.slice(0, m.index) : text;
  return (
    <div className="p4-msg-text p4-msg-reply">
      <ReplyBody text={body} sources={[]} />
      {m ? <div className="p4-msg-source">{`${t("Source:")} ${m[1]!.trim()}`}</div> : null}
    </div>
  );
}

/**
 * Pratica (AskRaffa V4.dc.html lines 119-175): the guided renewal case. The server's engine composes
 * the page (`view.page`): case summary, completed steps, the last chat bubbles and the one current step
 * card -- at most three visible buttons, the rest under "Altro ▾". Every action goes to
 * `POST /api/renewals/{caseId}/actions` with the version it was taken on; a 409/403 answer carries the
 * current case, which is rendered as is (typed text the user has not sent stays in the inputs).
 */
function parityRun(): boolean {
  try {
    return window.sessionStorage.getItem("raffa4.parity") === "1";
  } catch {
    return false;
  }
}

export default function CaseRoute({ apiClient }: CaseRouteProps) {
  const { caseId = "" } = useParams();
  const tenantId = useTenantId() ?? undefined;
  const { t, lang } = useI18n();
  const tr = useCallback((x: Text | null | undefined) => resolveText(x, lang), [lang]);
  const navigate = useNavigate();
  const [env, setEnv] = useState<CaseEnvelope | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [scheda, setScheda] = useState(false);
  const [hist, setHist] = useState(false);
  const [doneOpen, setDoneOpen] = useState<Record<number, boolean>>({});
  const [more, setMore] = useState(false);
  const [rq, setRq] = useState("");
  /** The composer question while Raffa answers it (shown at once, before the answer arrives). */
  const [asking, setAsking] = useState<string | null>(null);
  /** Why the last question got no answer; the question goes back into the composer to retry. */
  const [askError, setAskError] = useState<string | null>(null);
  /** The question and Raffa's answer sit above the step: bring them into view when they arrive. */
  const askView = useRef<HTMLDivElement | null>(null);
  const showAnswer = useRef(false);
  const [localWarn, setLocalWarn] = useState<Text | null>(null);
  const [drafts, setDrafts] = useState<Record<string, unknown>>({});
  const pending = useRef<Record<string, unknown>>({});
  const envRef = useRef<CaseEnvelope | null>(null);
  envRef.current = env;
  const busy = useRef(false);

  useEffect(() => {
    if (!tenantId || !apiClient.getRenewalCase || !caseId) return;
    let alive = true;
    void apiClient.getRenewalCase(tenantId, caseId, true).then((r) => {
      if (!alive) return;
      if (r.ok) setEnv(r.body);
      else setError(r.error);
    });
    return () => {
      alive = false;
    };
  }, [apiClient, tenantId, caseId]);

  const post = useCallback(
    async (type: string, p: CaseActionPayload | null): Promise<CaseEnvelope | null> => {
      const cur = envRef.current;
      if (!tenantId || !cur || !apiClient.postRenewalCaseAction) return null;
      const r = await apiClient.postRenewalCaseAction(tenantId, caseId, type, p, cur.version);
      const next = r.ok ? r.body : r.current;
      if (next) {
        envRef.current = next;
        setEnv(next);
      }
      setError(r.ok ? null : r.code === "stale_version" ? t("The case changed since you loaded it. Your text is kept; review the current version.") : null);
      if (r.ok && r.body.clip) {
        try {
          void navigator.clipboard?.writeText(r.body.clip);
        } catch {
          // Clipboard refused: the email text stays on screen to copy by hand.
        }
      }
      if (r.ok && r.body.tick) window.setTimeout(() => void post("tick", null), 900);
      return next;
    },
    [apiClient, tenantId, caseId, t],
  );

  /** Sends the inputs typed so far (recipient, edited body, answers) before an action that needs them. */
  const flush = useCallback(async () => {
    const entries = Object.entries(pending.current);
    pending.current = {};
    for (const [k, v] of entries) await post("ui", { k, v });
  }, [post]);

  const act = useCallback(
    async (type: string, p: CaseActionPayload | null) => {
      if (busy.current) return;
      busy.current = true;
      try {
        await flush();
        setLocalWarn(null);
        await post(type, p);
        if (type !== "ui") {
          setMore(false);
          setDrafts({});
          notifyTodayChanged();
        }
      } finally {
        busy.current = false;
      }
    },
    [flush, post],
  );

  const setDraft = (k: string, v: unknown) => {
    pending.current[k] = v;
    setDrafts((d) => ({ ...d, [k]: v }));
  };
  const draft = <T,>(k: string, fallback: T): T => (k in drafts ? (drafts[k] as T) : fallback);

  const press = (b: CaseButton) => {
    if (b.off) {
      setLocalWarn(b.why ?? null);
      return;
    }
    if (b.act === "nav") {
      navigate(b.p.to === "c360" && env ? `/contracts/${env.contractId}` : "/renewals");
      return;
    }
    if (isDocButton(b) && env && !env.demo && apiClient.uploadRenewalCaseDocument) {
      docInput.current?.click();
      return;
    }
    void act(b.act, b.p);
  };

  const docInput = useRef<HTMLInputElement | null>(null);
  const [uploading, setUploading] = useState(false);
  const uploadDoc = async (file: File) => {
    const cur = envRef.current;
    if (!tenantId || !cur || !apiClient.uploadRenewalCaseDocument) return;
    setUploading(true);
    setLocalWarn(null);
    const r = await apiClient.uploadRenewalCaseDocument(tenantId, caseId, { type: "addDoc", file, version: cur.version, lang });
    setUploading(false);
    const next = r.ok ? r.body : r.current;
    if (next) {
      envRef.current = next;
      setEnv(next);
      notifyTodayChanged();
    }
    if (!r.ok) setLocalWarn(t("Raffa could not add this file. Try a PDF or a text file."));
  };

  /**
   * The composer question: shown at once with a processing line, then the answer (the server adds both
   * bubbles to the case) or a recoverable error with the question back in the composer. Serialised with
   * the step actions and sent with the version current after the pending inputs were flushed, so a
   * draft saved just before cannot turn the question into a silent conflict.
   */
  const ask = async () => {
    const text = rq.trim();
    if (!text || !tenantId || !envRef.current || !apiClient.askRenewalCase || busy.current) return;
    busy.current = true;
    setRq("");
    setAskError(null);
    setAsking(text);
    try {
      await flush();
      const cur = envRef.current;
      if (!cur) return;
      const r = await apiClient.askRenewalCase(tenantId, caseId, text, cur.version, lang);
      const next = r.ok ? r.body : r.current;
      if (next) {
        envRef.current = next;
        showAnswer.current = r.ok;
        setEnv(next);
      }
      if (!r.ok) {
        setRq((typed) => typed || text);
        setAskError(
          r.code === "stale_version"
            ? t("The case changed while Raffa was answering. Your question is back in the box: ask it again.")
            : t("Raffa could not answer right now. Your question is back in the box: try again."),
        );
      }
    } catch {
      setRq((typed) => typed || text);
      setAskError(t("Raffa could not answer right now. Your question is back in the box: try again."));
    } finally {
      busy.current = false;
      setAsking(null);
    }
  };

  useEffect(() => {
    if (!asking && !showAnswer.current) return;
    showAnswer.current = false;
    // The exchange is read from its start: the question on top, the (possibly long) answer below it.
    // "nearest" aligned the bottom of the last bubble and cut the answer's opening off the window.
    askView.current?.scrollIntoView?.({ block: "start" });
  }, [asking, env]);

  // A4: after a step change, focus moves to the new step's heading.
  const headRef = useRef<HTMLDivElement | null>(null);
  const stepKey = env ? `${tr(env.view.page.current.kicker)}|${tr(env.view.page.current.title)}` : "";
  const firstStep = useRef(true);
  useEffect(() => {
    if (!stepKey) return;
    if (firstStep.current) {
      firstStep.current = false;
      return;
    }
    headRef.current?.focus({ preventScroll: false });
  }, [stepKey]);

  const page = env?.view.page;
  const cd = page?.current;
  const why = cd ? (!isEmpty(cd.note) ? tr(cd.note) : cd.lines[0] ? tr(cd.lines[0]) : t("One step at a time: everything else stays saved in the details.")) : "";
  useGuide(
    page && env
      ? {
          where: `${tr(page.title)} · ${tr(page.phase)}`,
          now: tr(env.view.hdr.next),
          why,
          next: env.closed
            ? t("The case is closed. The next cycle starts from the new notice deadline.")
            : page.done.length
              ? t("The next step appears below as soon as this one is closed.")
              : t("After the questions I draft the plan; you confirm it."),
          cta: null,
        }
      : null,
  );

  // The drafted email lives in the right-hand action panel (as in Ask), not in the middle of the step: it
  // opens by itself when a draft appears and Guida Raffa steps aside while it is open.
  const hasEmail = !!cd?.email;
  const [emailPanel, setEmailPanel] = useState(false);
  const hadEmail = useRef(false);
  const guide = useGuideState();
  const guideYielded = useRef(false);
  useEffect(() => {
    // (The parity run compares the page with the prototype, which shows the draft inline: no panel then.)
    if (hasEmail && !hadEmail.current && !parityRun()) setEmailPanel(true);
    if (!hasEmail) setEmailPanel(false);
    hadEmail.current = hasEmail;
  }, [hasEmail]); // eslint-disable-line react-hooks/exhaustive-deps
  // A new version of the draft (Raffa revised it from the composer) reopens the panel on it.
  const emailBody = cd?.email?.body ?? null;
  const lastBody = useRef<string | null>(null);
  useEffect(() => {
    if (emailBody && lastBody.current && emailBody !== lastBody.current && !parityRun()) setEmailPanel(true);
    lastBody.current = emailBody;
  }, [emailBody]);
  useEffect(() => {
    if (emailPanel && guide.open) {
      guideYielded.current = true;
      guide.toggle();
    } else if (!emailPanel && guideYielded.current) {
      guideYielded.current = false;
      if (!guide.open) guide.toggle();
    }
  }, [emailPanel]); // eslint-disable-line react-hooks/exhaustive-deps
  const [copiedMail, setCopiedMail] = useState(false);
  useEffect(() => {
    if (!copiedMail) return undefined;
    const timer = window.setTimeout(() => setCopiedMail(false), 2000);
    return () => window.clearTimeout(timer);
  }, [copiedMail]);

  if (!env || !page || !cd) {
    return (
      <div className="p4-page" data-screen-label="Pratica">
        {error ? <div className="p4-error">{error}</div> : null}
      </div>
    );
  }

  const v = env.view;
  const warn = localWarn ?? v.hdr.warn ?? page.warn ?? null;
  const refs = [
    ...v.emailsList.map((e) => ({ label: tr(e.label), state: tr(e.state), tone: e.tone })),
    ...v.docsList.map((e) => ({ label: tr(e.label), state: tr(e.state), tone: e.tone })),
    ...v.hdr.people.map((p) => ({ label: p.name, state: tr(p.role), tone: "muted" as Tone })),
  ];
  const em = cd.email;
  // The last message the person wrote: where a composer exchange starts.
  const lastPersonMsg = page.msgs.reduce((last, m, i) => (m.raffa ? last : i), page.msgs.length - 1);

  const input = (i: CaseInput) => {
    const value = draft<unknown>(i.ui, i.value);
    const commit = () => {
      if (i.ui in pending.current) void act("ui", { k: i.ui, v: pending.current[i.ui] });
    };
    return (
      <label key={i.ui} className="p4-input">
        {tr(i.label)}
        {i.type === "text" ? (
          <input className="input" value={String(value ?? "")} onChange={(e) => setDraft(i.ui, e.target.value)} onBlur={commit} />
        ) : null}
        {i.type === "area" ? (
          <textarea className="input" value={String(value ?? "")} onChange={(e) => setDraft(i.ui, e.target.value)} onBlur={commit} />
        ) : null}
        {i.type === "check" ? (
          <input type="checkbox" checked={!!value} onChange={(e) => void act("ui", { k: i.ui, v: e.target.checked })} />
        ) : null}
      </label>
    );
  };

  const emailBlock = em ? (
        <div className="p4-email">
          <div className="p4-email-state">{tr(em.state)}</div>
          <div className="p4-email-meta">
            <span className="p4-muted">{t("To")}</span>
            <div>
              {em.toMissing ? (
                <input
                  className="input p4-email-to"
                  placeholder={t("Recipient — not in the contract")}
                  value={draft<string>(em.toKey, em.to)}
                  onChange={(e) => setDraft(em.toKey, e.target.value)}
                  onBlur={() => {
                    if (em.toKey in pending.current) void act("ui", { k: em.toKey, v: pending.current[em.toKey] });
                  }}
                />
              ) : (
                <span>{em.to}</span>
              )}
            </div>
            <span className="p4-muted">{t("Subject")}</span>
            <span className="p4-strong">{tr(cd.title)}</span>
          </div>
          {em.editing ? (
            <div className="p4-email-edit">
              <textarea className="input" value={draft<string>("editBody", em.editBody)} onChange={(e) => setDraft("editBody", e.target.value)} />
            </div>
          ) : (
            <div className="p4-email-body">{em.body}</div>
          )}
          {!isEmpty(em.attachNote) || !isEmpty(em.sentNote) ? (
            <div className="p4-email-note">
              {tr(em.attachNote)} {tr(em.sentNote)}
            </div>
          ) : null}
        </div>
  ) : null;

  const rows = (list: CaseRow[], cls: string) =>
    list.map((r, i) => (
      <div key={i} className={cls}>
        <span className="p4-k">{tr(r.k)}</span>
        <span style={rowStyle(r)} className="p4-v">
          {tr(r.v)}
        </span>
      </div>
    ));

  const subject = tr(cd.title);
  const mailto = em ? `mailto:${em.toMissing ? "" : em.to}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(em.body)}` : "";

  return (
    <div className="p4-split">
    <div className="p4-page" data-screen-label="Pratica">
      <div className="p4-head">
        <div className="p4-top">
          <button type="button" className="btn btn-ghost p4-back" onClick={() => navigate("/renewals")}>
            ← {t("Renewals")}
          </button>
          <span className="p4-title">{tr(page.title)}</span>
          <button type="button" className="btn btn-ghost p4-scheda-toggle" onClick={() => setScheda((s) => !s)}>
            {scheda ? t("Hide details ▴") : t("Details: terms, emails, team, dates ▾")}
          </button>
        </div>
        <AskShortcuts
          scopeContractId={env.contractId}
          items={[
            { label: t("Renewal strategy"), question: t("How should we approach this renewal? Give me the strategy and the negotiation points.") },
            { label: t("Negotiation email"), question: t("Draft the negotiation email to the supplier for this renewal.") },
            { label: t("Risks and terms to watch"), question: t("Which clauses and risks should we watch in this contract before renewing?") },
          ]}
        />
        <div className="p4-cells">
          <div className="p4-cell">
            <div className="p4-label">{t("Phase")}</div>
            <div className="p4-val">{tr(page.phase)}</div>
          </div>
          <div className="p4-cell">
            <div className="p4-label">{t("Next action · who")}</div>
            <div className="p4-val">{tr(page.next)}</div>
          </div>
          <div className="p4-cell">
            <div className="p4-label">{t("Notice by · contract end")}</div>
            <div className="p4-val p4-val-plain">
              <strong className={page.noticeUrgent ? "is-urgent" : undefined}>{tr(page.notice)}</strong> · {tr(page.end)}
            </div>
          </div>
        </div>
        {!isEmpty(warn) ? (
          <div className="p4-warn">
            <strong>{tr(warn)}</strong> {tr(page.whoCan)}
          </div>
        ) : null}
        {error ? <div className="p4-warn">{error}</div> : null}
        {scheda ? (
          <div className="p4-scheda" data-screen-label="Scheda pratica">
            <div>
              <div className="p4-col-head">{t("Terms")}</div>
              {v.summary.terms.map((x, i) => (
                <div key={i} className="p4-line">
                  <span>{tr(x.label)}</span>
                  <span className="p4-right p4-strong" style={{ color: toneColor(x.tone) }}>
                    {x.val} · {tr(x.state)}
                  </span>
                </div>
              ))}
              {v.summary.terms.length === 0 ? <div className="p4-none">{t("They appear once the plan is confirmed.")}</div> : null}
            </div>
            <div>
              <div className="p4-col-head">{t("Plan · objectives")}</div>
              {v.summary.goals.map((g, i) => (
                <div key={i} className="p4-line">
                  <span className="p4-muted">{tr(g.k)}</span>
                  <span className="p4-right p4-ralign">{tr(g.v)}</span>
                </div>
              ))}
            </div>
            <div>
              <div className="p4-col-head">{t("Emails · documents · team")}</div>
              {refs.map((e, i) => (
                <div key={i} className="p4-line">
                  <span className="p4-wrap">{e.label}</span>
                  <span className="p4-right p4-strong p4-nowrap" style={{ color: e.tone === "muted" ? "var(--color-neutral-600)" : toneColor(e.tone) }}>
                    {e.state}
                  </span>
                </div>
              ))}
            </div>
            <div>
              <div className="p4-col-head">{t("Upcoming dates")}</div>
              {v.upcoming.map((u, i) => (
                <div key={i} className="p4-line">
                  <span className="p4-muted">{u.date}</span>
                  <span>{tr(u.label)}</span>
                  <span className="p4-right p4-muted">{tr(u.when)}</span>
                </div>
              ))}
            </div>
          </div>
        ) : null}
      </div>

      <div className="p4-feed">
        <div className="p4-feed-in">
          {page.done.length > 0 ? (
            <div>
              <div className="p4-hist-bar">
                <button
                  type="button"
                  className="btn btn-secondary p4-review"
                  onClick={() => {
                    setHist(true);
                    setDoneOpen({ 0: true });
                  }}
                >
                  <span>↺</span>
                  <span>{t("Review the case overview")}</span>
                </button>
                <button type="button" className="btn btn-ghost p4-hist-toggle" onClick={() => setHist((h) => !h)}>
                  {hist ? t("Hide completed steps ▴") : t("Completed steps · {n} ▾", { n: page.done.length })}
                </button>
              </div>
              {hist ? (
                <div className="p4-done">
                  {page.done.map((d, i) => {
                    const op = !!doneOpen[i];
                    return (
                      <div key={i} className="p4-done-item">
                        <button
                          type="button"
                          className={op ? "p4-done-row is-open" : "p4-done-row"}
                          aria-expanded={op}
                          onClick={() => setDoneOpen((m) => ({ ...m, [i]: !op }))}
                        >
                          <span className="p4-done-mark" />
                          <span className="p4-done-kicker">{tr(d.kicker)}</span>
                          <strong>{tr(d.title)}</strong>
                          <span className="p4-done-note">
                            {tr(d.note)} {op ? "▴" : "▾"}
                          </span>
                        </button>
                        {op ? (
                          <div className="p4-done-body">
                            {d.lines.map((l, j) => (
                              <div key={j} className="p4-done-line">
                                {tr(l)}
                              </div>
                            ))}
                            {rows(d.rows, "p4-done-r")}
                            {d.steps.length > 0 ? (
                              <div className="p4-steps">
                                {d.steps.map((s, j) => (
                                  <span key={j} style={stepStyle(s)}>
                                    {tr(s.label)}
                                  </span>
                                ))}
                              </div>
                            ) : null}
                          </div>
                        ) : null}
                      </div>
                    );
                  })}
                </div>
              ) : null}
            </div>
          ) : null}

          {page.msgs.map((m, i) => (
            <div key={i} className="p4-msg" ref={!asking && i === lastPersonMsg ? askView : undefined}>
              <span className={m.raffa ? "p4-msg-by is-raffa" : "p4-msg-by"} title={m.by}>
                {m.by}
              </span>
              {m.raffa ? <RaffaBubble text={tr(m.text)} /> : <div className="p4-msg-text is-person">{tr(m.text)}</div>}
            </div>
          ))}
          {asking ? (
            <div className="p4-asking" data-screen-label="Domanda in corso" aria-live="polite" aria-busy="true" ref={askView}>
              <div className="p4-msg">
                <span className="p4-msg-by">{t("You")}</span>
                <div className="p4-msg-text is-person">{asking}</div>
              </div>
              <div className="p4-msg">
                <span className="p4-msg-by is-raffa">Raffa</span>
                <div className="p4-msg-text p4-msg-pending">{t("Reading the contract to answer…")}</div>
              </div>
            </div>
          ) : null}

          <div className="p4-step" data-screen-label="Passo corrente">
            <div ref={headRef} tabIndex={-1} className="p4-step-head">
              <div className="p4-kicker">{tr(cd.kicker)}</div>
              <div className="p4-step-title">{tr(cd.title)}</div>
            </div>
            {!isEmpty(cd.banner) ? <div className="p4-banner">{tr(cd.banner)}</div> : null}
            {cd.lines.map((l, i) => (
              <div key={i} className="p4-line-text">
                {tr(l)}
              </div>
            ))}
            {cd.rows.length > 0 ? <div>{rows(cd.rows, "p4-row")}</div> : null}
            {cd.table ? (
              <div className="p4-table-wrap">
                <div className="p4-table">
                  <div className="p4-table-head" style={{ gridTemplateColumns: cd.table.grid }}>
                    {cd.table.cols.map((c, i) => (
                      <span key={i}>{tr(c)}</span>
                    ))}
                  </div>
                  {cd.table.rows.map((r, i) => (
                    <div key={i} className="p4-table-row" style={{ gridTemplateColumns: cd.table!.grid }}>
                      {r.map((c, j) => (
                        <span key={j} style={rowStyle(c)}>
                          {tr(c.v)}
                        </span>
                      ))}
                    </div>
                  ))}
                </div>
              </div>
            ) : null}
            {cd.steps.length > 0 ? (
              <div className="p4-steps">
                {cd.steps.map((s, i) => (
                  <span key={i} style={stepStyle(s)}>
                    {tr(s.label)}
                  </span>
                ))}
              </div>
            ) : null}
            {em ? (
              <>
                {/* The prototype shows the draft inline: the parity run shows this copy; everyone else gets the card and the panel. */}
                <div className="p4-email-inline">{emailBlock}</div>
                <button type="button" className="artifact-card p4-email-card" aria-pressed={emailPanel} onClick={() => setEmailPanel((o) => !o)}>
                  <span className="artifact-card-icon" aria-hidden="true">
                    <MailIcon />
                  </span>
                  <span className="artifact-card-text">
                    <span className="artifact-card-title">{tr(cd.title)}</span>
                    <span className="artifact-card-meta">
                      {tr(em.state)}
                      <span aria-hidden="true"> · </span>
                      {emailPanel ? t("Open⟦panel⟧") : t("Click to open")}
                    </span>
                  </span>
                </button>
              </>
            ) : null}
            {cd.pre ? <div className="p4-pre">{cd.pre}</div> : null}
            {cd.fields.length > 0 ? (
              <div>
                {cd.fields.map((x) => (
                  <div key={x.i} className="p4-field">
                    <span className="p4-field-name">{tr(x.name)}</span>
                    <span className="p4-field-val">{x.val}</span>
                    <span className="p4-field-conf">{x.conf}</span>
                    {!isEmpty(x.src) ? <span className="p4-field-src">{tr(x.src!)}</span> : null}
                    {x.open ? (
                      <>
                        <button type="button" className="btn btn-secondary p4-field-accept" onClick={() => void act("acceptF", { i: x.i })}>
                          {t("Accept")}
                        </button>
                        <input className="input p4-field-in" placeholder={t("Corrected value")} value={draft<string>(x.inKey, x.inVal)} onChange={(e) => setDraft(x.inKey, e.target.value)} />
                        <button type="button" className="btn btn-ghost p4-field-correct" onClick={() => void act("correctF", { i: x.i })}>
                          {t("Correct")}
                        </button>
                      </>
                    ) : null}
                    {!isEmpty(x.doneLabel) ? <span className="p4-strong">{tr(x.doneLabel)}</span> : null}
                  </div>
                ))}
              </div>
            ) : null}
            {cd.chips.length > 0 ? (
              <div className="p4-chips">
                {cd.chips.map((ch, i) => (
                  <button
                    key={i}
                    type="button"
                    className={ch.on ? "p4-chip is-on" : "p4-chip"}
                    style={{ opacity: ch.off ? 0.5 : 1 }}
                    onClick={() => void act(ch.act, ch.p)}
                  >
                    <span className="p4-chip-mark" />
                    {tr(ch.label)}
                  </button>
                ))}
              </div>
            ) : null}
            {cd.inputs.length > 0 ? <div className="p4-inputs">{cd.inputs.map(input)}</div> : null}
            <div className="p4-buttons">
              {cd.buttons.filter((b) => env.demo || !demoLabel(b) || isDocButton(b)).map((b, i) => (
                <button key={i} type="button" className={`btn ${b.primary ? "btn-primary" : "btn-secondary"} p4-btn`} style={{ opacity: b.off ? 0.5 : 1 }} onClick={() => press(b)}>
                  {isDocButton(b) && !env.demo ? (uploading ? t("Raffa is reading…") : t("Add the file")) : tr(b.label)}
                </button>
              ))}
              <input
                ref={docInput}
                type="file"
                hidden
                accept=".pdf,.docx,.txt,.png,.jpg,.jpeg"
                onChange={(e) => {
                  const f = e.target.files?.[0];
                  e.target.value = "";
                  if (f) void uploadDoc(f);
                }}
              />
              {cd.more.length > 0 ? (
                <button type="button" className="btn btn-ghost p4-more-toggle" onClick={() => setMore((m) => !m)} aria-expanded={more}>
                  {more ? t("Less ▴") : t("More ▾")}
                </button>
              ) : null}
            </div>
            {more ? (
              <div className="p4-more">
                {cd.more
                  .filter((b) => env.demo || !demoLabel(b) || isDocButton(b))
                  .map((b, i) => (
                    <button key={i} type="button" className="btn btn-ghost" style={{ opacity: b.off ? 0.5 : 1 }} onClick={() => press(b)}>
                      {tr(b.label)}
                    </button>
                  ))}
              </div>
            ) : null}
            {!isEmpty(cd.note) ? <div className="p4-note">{tr(cd.note)}</div> : null}
          </div>

          {v.reply.open ? (
            <div className="p4-reply">
              <div className="p4-reply-head">{t("Paste the supplier reply")}</div>
              <textarea className="input" value={draft<string>("replyText", v.reply.text)} onChange={(e) => setDraft("replyText", e.target.value)} />
              <div className="p4-reply-actions">
                <button type="button" className="btn btn-primary" onClick={() => void act("reply", {})}>
                  {t("Add to the case")}
                </button>
                {env.demo ? (
                  <button
                    type="button"
                    className="btn btn-ghost"
                    onClick={() => {
                      delete pending.current.replyText;
                      setDrafts((d) => {
                        const { replyText: _drop, ...rest } = d;
                        return rest;
                      });
                      void act("sampleReply", null);
                    }}
                  >
                    {t("Use sample reply")}
                  </button>
                ) : null}
                <button type="button" className="btn btn-ghost" onClick={() => void act("closeReply", null)}>
                  {t("Cancel")}
                </button>
              </div>
            </div>
          ) : null}
        </div>
      </div>

      <div className="p4-composer">
        {askError ? (
          <div className="p4-ask-error" role="alert">
            {askError}
          </div>
        ) : null}
        <div className="p4-composer-in">
          <input
            className="input"
            placeholder={t("A doubt? Ask Raffa about this contract — then we return to the step")}
            value={rq}
            disabled={asking !== null}
            onChange={(e) => setRq(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault();
                void ask();
              }
            }}
          />
          <button type="button" className="btn btn-primary" onClick={() => void ask()} disabled={asking !== null}>
            {asking !== null ? t("Raffa is answering…") : t("Ask")}
          </button>
        </div>
      </div>
    </div>
    {em && emailPanel ? (
      <ArtifactPanel
        label={t("Email draft")}
        kicker={tr(em.state)}
        title={subject}
        icon={<MailIcon />}
        size="wide"
        onClose={() => setEmailPanel(false)}
        actions={
          <>
            <a className="artifact-panel-action" href={mailto}>
              {t("Open in mail")}
            </a>
            <button
              type="button"
              className="artifact-panel-action"
              data-primary="true"
              onClick={() => {
                const clipboard = typeof navigator === "undefined" ? undefined : navigator.clipboard;
                if (clipboard) void clipboard.writeText(`${subject}\n\n${em.body}`).then(() => setCopiedMail(true));
              }}
              aria-live="polite"
            >
              {copiedMail ? t("Copied") : t("Copy email")}
            </button>
          </>
        }
      >
        <div className="p4-panel-email">{emailBlock}</div>
      </ArtifactPanel>
    ) : null}
    </div>
  );
}
