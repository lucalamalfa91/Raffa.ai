import { useId, useState, type ReactNode } from "react";
import { useI18n } from "../../i18n/I18nProvider";

/** One source of a reply: what the conversation stores of a citation (title, page, section). */
export interface ReplySource {
  title?: string;
  page?: number | null;
  section?: string | null;
  /** The passage the answer rests on, when the engine sent it (shown in the chip's preview). */
  snippet?: string | null;
}

const INLINE_TOKEN = /\*\*(.+?)\*\*|\[(\d+)\]/g;
const BULLET = /^\s*[-*•]\s+/;
const NUMBERED = /^\s*\d+[.)]\s+/;

type Block =
  | { type: "p"; text: string }
  | { type: "h"; text: string }
  | { type: "ul"; items: string[] }
  | { type: "ol"; items: string[] };

/**
 * Splits a reply into paragraph / heading / list blocks, line by line: a line that is only `**…**` is a
 * heading, consecutive `-` / `*` lines are a bullet list, `1.` / `1)` lines a numbered one, the rest
 * prose (adjacent lines of one paragraph stay together). A blank line closes a block. Safe: text only,
 * never HTML (React escapes every string child).
 */
export function splitBlocks(source: string): Block[] {
  const blocks: Block[] = [];
  let para: string[] = [];
  const flush = () => {
    if (para.length) blocks.push({ type: "p", text: para.join("\n") });
    para = [];
  };
  for (const raw of source.replace(/\r\n/g, "\n").split("\n")) {
    const line = raw.trim();
    if (line === "") {
      flush();
      continue;
    }
    const heading = /^\*\*([^*]+)\*\*:?$/.exec(line);
    if (heading) {
      flush();
      blocks.push({ type: "h", text: heading[1]!.trim() });
    } else if (BULLET.test(raw)) {
      flush();
      const item = raw.replace(BULLET, "").trim();
      const last = blocks[blocks.length - 1];
      if (last?.type === "ul") last.items.push(item);
      else blocks.push({ type: "ul", items: [item] });
    } else if (NUMBERED.test(raw)) {
      flush();
      const item = raw.replace(NUMBERED, "").trim();
      const last = blocks[blocks.length - 1];
      if (last?.type === "ol") last.items.push(item);
      else blocks.push({ type: "ol", items: [item] });
    } else {
      para.push(line);
    }
  }
  flush();
  return blocks;
}

function sourceLine(s: ReplySource): string {
  return [s.title, s.page ? `p. ${s.page}` : null, s.section].filter((x): x is string => !!x && x.trim() !== "").join(" · ");
}

/** An inline `[n]` marker: a small chip with the source's number; hovering or focusing it previews the source. */
function Chip({ n, source }: { n: number; source: ReplySource }) {
  const [open, setOpen] = useState(false);
  const id = useId();
  return (
    <span className="reply-citation" onMouseEnter={() => setOpen(true)} onMouseLeave={() => setOpen(false)}>
      <button
        type="button"
        className="reply-citation-ref"
        aria-describedby={open ? id : undefined}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
        onKeyDown={(e) => {
          if (e.key === "Escape") setOpen(false);
        }}
      >
        {n}
      </button>
      {open ? (
        <span className="reply-citation-preview" role="tooltip" id={id}>
          <span className="reply-citation-preview-title">{source.title}</span>
          <span className="reply-citation-preview-subtitle">{[source.page ? `p. ${source.page}` : null, source.section].filter(Boolean).join(" · ")}</span>
          {source.snippet ? <span className="reply-citation-preview-snippet">{`“${source.snippet.trim()}”`}</span> : null}
        </span>
      ) : null}
    </span>
  );
}

function inline(text: string, sources: readonly ReplySource[], key: string): ReactNode[] {
  const nodes: ReactNode[] = [];
  let last = 0;
  let i = 0;
  INLINE_TOKEN.lastIndex = 0;
  let m: RegExpExecArray | null;
  while ((m = INLINE_TOKEN.exec(text)) !== null) {
    if (m.index > last) nodes.push(text.slice(last, m.index));
    if (m[1] !== undefined) nodes.push(<strong key={`${key}-b${i}`}>{m[1]}</strong>);
    else {
      const n = Number(m[2]);
      const source = sources[n - 1];
      nodes.push(source ? <Chip key={`${key}-c${i}`} n={n} source={source} /> : <sup key={`${key}-c${i}`}>[{n}]</sup>);
    }
    last = INLINE_TOKEN.lastIndex;
    i += 1;
  }
  if (last < text.length) nodes.push(text.slice(last));
  return nodes;
}

/**
 * A reply, the way the chat always showed it: paragraphs, bold headings, bullet and numbered lists,
 * inline source chips, and one collapsed "Sources" row under it (what the answer rests on).
 */
export default function ReplyBody({ text, sources }: { text: string; sources: readonly ReplySource[] }) {
  const { t } = useI18n();
  const blocks = splitBlocks(text);
  const listed = sources.filter((s) => sourceLine(s) !== "");
  return (
    <div className="reply-body">
      <div className="reply-markdown">
        {blocks.map((b, i) => {
          if (b.type === "h") return <h4 key={i} className="reply-heading">{b.text}</h4>;
          if (b.type === "ul")
            return (
              <ul key={i}>
                {b.items.map((it, j) => (
                  <li key={j}>{inline(it, sources, `b${i}-${j}`)}</li>
                ))}
              </ul>
            );
          if (b.type === "ol")
            return (
              <ol key={i}>
                {b.items.map((it, j) => (
                  <li key={j}>{inline(it, sources, `b${i}-${j}`)}</li>
                ))}
              </ol>
            );
          return <p key={i}>{inline(b.text, sources, `b${i}`)}</p>;
        })}
      </div>
      {listed.length > 0 ? (
        <details className="reply-sources">
          <summary>{listed.length === 1 ? t("1 source") : t("{n} sources", { n: listed.length })}</summary>
          <ol>
            {sources.map((s, i) =>
              sourceLine(s) ? (
                <li key={i} value={i + 1}>
                  {sourceLine(s)}
                </li>
              ) : null,
            )}
          </ol>
        </details>
      ) : null}
    </div>
  );
}
