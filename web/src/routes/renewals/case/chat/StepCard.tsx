import { useId, type CSSProperties } from "react";
import type { Card, CardButton, CardChip, CardField, CardInput, CardTable } from "../types";
import type { Translate } from "./chatCopy";

export interface StepCardProps {
  card: Card;
  t: Translate;
  /** Shows the card as a record (the history drawer): no buttons, no inputs, nothing to press. */
  readOnly?: boolean;
  onPress?: (button: CardButton) => void;
  onChip?: (chip: CardChip) => void;
  /** Sets a field the case keeps (`act("ui", { k, v })`). */
  onField?: (key: string, value: string | boolean) => void;
  onAct?: (act: "acceptF" | "correctF", payload: { i: number }) => void;
}

const gridOf = (table: CardTable): CSSProperties => ({
  gridTemplateColumns: table.layout.map((c) => `minmax(${c.min}px, ${c.fr}fr)`).join(" "),
});

const toneClass = (tone?: "alert" | "muted"): string => (tone ? ` is-${tone}` : "");

/**
 * One step of the wizard as a card: kicker, title, the facts, and the structured answers (chips,
 * buttons) -- or, when it is only Raffa's or the user's words, a plain chat line. Text always wraps.
 */
export default function StepCard({ card, t, readOnly = false, onPress, onChip, onField, onAct }: StepCardProps) {
  const raffa = card.by === "Raffa.ai";
  const interactive = !readOnly;

  return (
    <article className={`rc-entry${card.dim ? " is-dim" : ""}`} data-current={card.current ? "true" : undefined} data-card-id={card.id}>
      <header className="rc-entry-meta">
        <span className={`rc-entry-by${raffa ? " is-raffa" : ""}`}>{card.by}</span>
        <span className="rc-entry-at">{card.at}</span>
      </header>

      {card.plain ? (
        <>
          {card.text && <p className={`rc-say${raffa ? " is-raffa" : ""}`}>{card.text}</p>}
          {card.tag && <span className="tag tag-outline rc-tag">{card.tag}</span>}
        </>
      ) : (
        <section className={`rc-card${card.current ? " is-current" : ""}`}>
          <div>
            <div className="rc-kicker">{card.kicker}</div>
            <h3 className="rc-title">{card.title}</h3>
          </div>

          {card.banner && <div className="rc-banner">{card.banner}</div>}

          {card.lines.length > 0 && (
            <div className="rc-lines">
              {card.lines.map((line, i) => (
                <p key={i}>{line}</p>
              ))}
            </div>
          )}

          {card.rows.length > 0 && (
            <dl className="rc-rows">
              {card.rows.map((row, i) => (
                <div className="rc-row" key={i}>
                  <dt>{row.k}</dt>
                  <dd className={toneClass(row.tone).trim()} style={row.w ? { fontWeight: row.w } : undefined}>
                    {row.v}
                  </dd>
                </div>
              ))}
            </dl>
          )}

          {card.table && (
            <div className="rc-table-scroll">
              <table className="rc-table" role="table" aria-label={card.title}>
                <thead role="rowgroup">
                  <tr role="row" style={gridOf(card.table)}>
                    {card.table.cols.map((col, i) => (
                      <th role="columnheader" scope="col" key={i}>
                        {col}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody role="rowgroup">
                  {card.table.rows.map((cells, r) => (
                    <tr role="row" key={r} style={gridOf(card.table as CardTable)}>
                      {cells.map((cell, i) => (
                        <td role="cell" key={i} className={toneClass(cell.tone).trim()} style={cell.w ? { fontWeight: cell.w } : undefined}>
                          {cell.v}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {card.steps.length > 0 && (
            <ol className="rc-pills">
              {card.steps.map((step, i) => (
                <li key={i} className={`rc-pill${step.now ? " is-now" : step.on ? " is-on" : ""}`} aria-current={step.now ? "step" : undefined}>
                  {step.label}
                </li>
              ))}
            </ol>
          )}

          {card.email && (
            <div className="rc-email">
              <div className="rc-email-state">{card.email.state}</div>
              <div className="rc-email-head">
                <span>{t("to")}</span>
                <div>
                  {card.email.toMissing && interactive ? (
                    <input
                      className="input rc-input"
                      aria-label={t("recipient")}
                      placeholder={t("recipient")}
                      value={card.email.to}
                      onChange={(e) => onField?.(card.email?.toKey ?? "", e.target.value)}
                    />
                  ) : (
                    <span>{card.email.to || "—"}</span>
                  )}
                </div>
                <span>{t("subject")}</span>
                <strong>{card.title}</strong>
              </div>
              {card.email.showBody &&
                (card.email.editing && interactive ? (
                  <div className="rc-email-body-edit">
                    <textarea
                      className="input rc-input rc-email-editor"
                      aria-label={card.title}
                      value={card.email.editBody}
                      onChange={(e) => onField?.("editBody", e.target.value)}
                    />
                  </div>
                ) : (
                  <div className="rc-email-body">{card.email.body}</div>
                ))}
              {card.email.attachNote && <div className="rc-email-note">{card.email.attachNote}</div>}
              {card.email.sentNote && <div className="rc-email-note is-alert">{card.email.sentNote}</div>}
            </div>
          )}

          {card.pre && <pre className="rc-pre">{card.pre}</pre>}

          {card.fields.length > 0 && <Fields fields={card.fields} t={t} interactive={interactive} onField={onField} onAct={onAct} />}

          {card.chips.length > 0 && (
            <div className="rc-chips" role="group" aria-label={card.title}>
              {card.chips.map((chip, i) => {
                const isMulti = chip.act === "toggle";
                const shared = {
                  className: `rc-chip${chip.on ? " is-on" : ""}`,
                  disabled: !interactive || !!chip.off,
                };
                return (
                  <button
                    key={i}
                    type="button"
                    {...shared}
                    {...(isMulti ? { "aria-pressed": chip.on } : {})}
                    onClick={() => onChip?.(chip)}
                  >
                    <span className="rc-chip-mark" aria-hidden="true" />
                    <span>{chip.label}</span>
                  </button>
                );
              })}
            </div>
          )}

          {interactive && card.inputs.length > 0 && (
            <div className="rc-inputs">
              {card.inputs.map((input, i) => (
                <FieldInput key={i} input={input} onField={onField} />
              ))}
            </div>
          )}

          {interactive && card.buttons.length > 0 && <Buttons buttons={card.buttons} onPress={onPress} />}

          {card.note && <p className="rc-note">{card.note}</p>}
        </section>
      )}
    </article>
  );
}

function Buttons({ buttons, onPress }: { buttons: CardButton[]; onPress?: (button: CardButton) => void }) {
  const uid = useId();
  return (
    <div className="rc-actions">
      <div className="rc-buttons">
        {buttons.map((button, i) => (
          <button
            key={i}
            type="button"
            className={`btn ${button.primary ? "btn-primary" : "btn-secondary"}`}
            disabled={button.off}
            aria-describedby={button.off && button.why ? `${uid}-why-${i}` : undefined}
            onClick={() => onPress?.(button)}
          >
            {button.label}
          </button>
        ))}
      </div>
      {/* A button that is switched off always says why, in text, next to the buttons. */}
      {buttons.map((button, i) =>
        button.off && button.why ? (
          <p className="hint rc-why" key={i} id={`${uid}-why-${i}`}>
            {button.label}: {button.why}
          </p>
        ) : null,
      )}
    </div>
  );
}

function FieldInput({ input, onField }: { input: CardInput; onField?: StepCardProps["onField"] }) {
  const id = `rc-in-${input.ui}`;
  return (
    <div className={`rc-field${input.type === "check" ? " is-check" : ""}`}>
      <label htmlFor={id}>{input.label}</label>
      {input.type === "area" ? (
        <textarea id={id} className="input rc-input" value={String(input.value)} onChange={(e) => onField?.(input.ui, e.target.value)} />
      ) : input.type === "check" ? (
        <input id={id} type="checkbox" checked={!!input.value} onChange={(e) => onField?.(input.ui, e.target.checked)} />
      ) : (
        <input id={id} className="input rc-input" value={String(input.value)} onChange={(e) => onField?.(input.ui, e.target.value)} />
      )}
    </div>
  );
}

function Fields({ fields, t, interactive, onField, onAct }: { fields: CardField[]; t: Translate; interactive: boolean } & Pick<StepCardProps, "onField" | "onAct">) {
  return (
    <ul className="rc-fields">
      {fields.map((field) => (
        <li key={field.i}>
          <span className="rc-field-name">{field.name}</span>
          <span className="rc-field-val">{field.val}</span>
          <span className="rc-field-conf">{field.conf}</span>
          {field.open && interactive && (
            <>
              <button type="button" className="btn btn-secondary" onClick={() => onAct?.("acceptF", { i: field.i })}>
                {t("accept")}
              </button>
              <input
                className="input rc-input rc-field-correct"
                aria-label={`${t("correctedValue")} — ${field.name}`}
                placeholder={t("correctedValue")}
                value={field.inVal}
                onChange={(e) => onField?.(field.inKey, e.target.value)}
              />
              <button type="button" className="btn btn-ghost" onClick={() => onAct?.("correctF", { i: field.i })}>
                {t("correct")}
              </button>
            </>
          )}
          {field.doneLabel && <strong>{field.doneLabel}</strong>}
        </li>
      ))}
    </ul>
  );
}
