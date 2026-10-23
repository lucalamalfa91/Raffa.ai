import type { Case, CaseView } from "../case/types";
import type { RenewalSummary } from "../case/portfolio";
import type { RenewalsWorkspace } from "../case/useRenewalsWorkspace";
import { nextStepLine, stepLine, stepTitle, wizardRows } from "./tabModel";

export interface TabProps {
  v: CaseView;
  c: Case;
  item: RenewalSummary;
  w: RenewalsWorkspace;
}

/**
 * "Do now": the same step guide as the chat, plus the key dates and people. Left, a numbered
 * checklist of the step with the one thing to do now expanded and one primary button; right, days to
 * the notice deadline and who answers for the case.
 */
export default function NowTab({ v, c, item, w }: TabProps) {
  const { t } = w;
  const rows = wizardRows(v.check, w.viewer, t);
  const items = w.queueAll.filter((x) => x.cid === item.cid);
  const main = items[0];
  const actions = main ? w.actionsFor(main) : null;
  const open = () => w.openChat(item.cid);
  const mineHere = items.some((x) => x.mine);

  return (
    <div className="ct-now">
      <ol className="ct-segments" aria-label={t("Renewal steps")}>
        {v.steps7.map((s) => (
          <li key={s.n} className={s.now ? "is-now" : s.done ? "is-done" : undefined} aria-current={s.now ? "step" : undefined}>
            <span className="ct-segment-n">{s.n}</span>
            <span className="visually-hidden">{s.label}</span>
          </li>
        ))}
      </ol>

      <div className="ct-now-grid">
        <section className="ct-guide" aria-label={stepTitle(v)}>
          <header className="ct-guide-head">
            <div className="ct-step-line">{stepLine(v, t)}</div>
            <h4 className="ct-step-title">{stepTitle(v)}</h4>
          </header>

          <div className="ct-todo">
            <h5 className="rn-label ct-todo-title">{t("What to do in this step")}</h5>
            {rows.length === 0 ? (
              <p className="ct-plain">{v.hdr.next}</p>
            ) : (
              <ol className="ct-rows">
                {rows.map((row, i) => (
                  <li key={i} className={`ct-row${row.now ? " is-now" : row.done ? " is-done" : ""}`} aria-current={row.now ? "step" : undefined}>
                    <span className="ct-mark" aria-hidden="true">
                      {row.mark}
                    </span>
                    <div className="ct-row-body">
                      <div className="ct-row-label">{row.label}</div>
                      <div className="rn-small">{row.who}</div>
                      {row.now && (
                        <div className="ct-expand">
                          {main && actions ? (
                            <>
                              <p className="ct-why">{main.why}</p>
                              <div className="ct-cta">
                                <button type="button" className="btn btn-primary ct-main" onClick={() => w.run(main, actions.primary)}>
                                  {actions.primary.label}
                                </button>
                                {actions.delegate && (
                                  <button type="button" className="btn btn-ghost" onClick={() => actions.delegate && w.delegate(main, actions.delegate)}>
                                    {actions.delegate.label}
                                  </button>
                                )}
                              </div>
                            </>
                          ) : (
                            <>
                              <p className="ct-why">{v.hdr.next}</p>
                              <p className="rn-small">{t("Nothing to do for you here — you will see it in Today when it comes back to you.")}</p>
                            </>
                          )}
                        </div>
                      )}
                    </div>
                  </li>
                ))}
              </ol>
            )}
          </div>

          <footer className="ct-guide-foot">
            <span className="ct-next">{nextStepLine(v, t)}</span>
            <button type="button" className="btn btn-ghost" onClick={open}>
              {mineHere ? t("Open at the current step") : t("Open the renewal chat")} →
            </button>
          </footer>
        </section>

        <aside className="ct-key" aria-label={t("Key dates")}>
          <div className="ct-key-days">
            <span className="rn-label">{t("Days to notice")}</span>
            <div className={`rn-big${v.hdr.noticeDays <= 30 ? " is-urgent" : ""}`}>{v.hdr.noticeDays}</div>
          </div>
          <dl className="ct-facts">
            <div>
              <dt className="rn-label">{t("Notice by")}</dt>
              <dd>
                <strong>{v.hdr.noticeBy}</strong>
              </dd>
            </div>
            <div>
              <dt className="rn-label">{t("Contract ends")}</dt>
              <dd>{v.hdr.end}</dd>
            </div>
            <div>
              <dt className="rn-label">{t("Owner")}</dt>
              <dd>
                <strong>{c.owner}</strong>
              </dd>
            </div>
            <div>
              <dt className="rn-label">{t("Approver")}</dt>
              <dd>{c.approver}</dd>
            </div>
          </dl>
        </aside>
      </div>

      {(!c.notice || !c.alt) && !c.outcome && (
        <div className="rn-buttons">
          {!c.notice && (
            <button type="button" className="btn btn-secondary" onClick={() => w.startOwn(item.cid, "notice")}>
              {t("Prepare non-renewal notice")}
            </button>
          )}
          {!c.alt && (
            <button type="button" className="btn btn-secondary" onClick={() => w.startOwn(item.cid, null, true)}>
              {t("Evaluate alternatives")}
            </button>
          )}
        </div>
      )}
    </div>
  );
}
