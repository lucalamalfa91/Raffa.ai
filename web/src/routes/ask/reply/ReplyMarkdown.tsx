import type { ReactNode } from "react";
import type { ReplyCitation } from "./replyTypes";

/**
 * Safe markdown -> React elements for a reply's `answerMarkdown` (task text; R-WEB-04;
 * requirements.md §6; task E13/F09/US01/T02). A small, dependency-free renderer: the task text
 * allows a markdown library only "if it is <=30kB gzipped and sanitizes by default", a claim this
 * harness cannot install or verify (`npm`/`node` are not on the Helix Bash PATH by name -- see
 * this task's own turn for how the implementer reached them at all to run `npm test`/`npm run
 * build`), so a hand-written renderer sidesteps an unverifiable size/sanitization claim entirely.
 *
 * Supports exactly the subset R-ASK-05's persona prompt is allowed to produce (`answerMarkdown` is
 * narrated prose, not arbitrary Markdown): paragraphs (blank-line separated), `**bold**`, short
 * lists (every non-empty line of a block starts with `-`/`*`), and inline `[n]` citation markers.
 *
 * **No raw HTML pass-through.** Every character of the source text is emitted as a React text
 * child, never via `dangerouslySetInnerHTML` -- React itself escapes text children on render, so a
 * literal `<script>` in the model's own narration renders as inert visible text. This is what
 * makes the DoD's "`<script>` in markdown is escaped" true structurally, not by a special case.
 *
 * **`[n]` is a callback, not a DOM anchor.** "Rendered as superscript links to the matching card"
 * (task text) is satisfied by calling the exact same `onOpenCitation` callback `CitationCard`'s own
 * click does, not an `href="#some-id"` fragment jump: an anchor id would have to be unique
 * page-wide, but `n` is only unique *within one reply* (this file's own `ReplyCitation.n` doc
 * comment) -- a real conversation renders many replies at once, each restarting citation numbering
 * from 1, so a page-global id would collide the moment more than one reply is on screen. Calling
 * back into the same handler the card uses sidesteps that entirely and is the more meaningful
 * proof anyway: clicking `[2]` does exactly what clicking card 2 does.
 */

export interface ReplyMarkdownProps {
  text: string;
  /** Valid `[n]` targets for this reply -- an `[n]` with no matching citation renders as plain
   * superscript text, never a dangling link (R-ASK-06 "every [n] must exist in the pack" is a
   * backend guard; this component's own fallback is what an honest renderer does if that guard is
   * ever wrong, rather than fabricating a link to nothing). */
  citations: readonly ReplyCitation[];
  /** Shared with every `CitationCard` for this same reply -- see this file's own header comment. */
  onOpenCitation: (citation: ReplyCitation) => void;
}

export type MarkdownBlock = { type: "paragraph"; text: string } | { type: "list"; items: string[] };

const LIST_MARKER_PATTERN = /^[-*]\s+/;
const INLINE_TOKEN_PATTERN = /\*\*(.+?)\*\*|\[(\d+)\]/g;

/**
 * Splits `answerMarkdown` into paragraph/list blocks on one-or-more blank lines (task text:
 * "paragraphs ... short lists"). A block is a list only when *every* non-empty line starts with
 * `-`/`*` -- a stray bullet inside otherwise-prose text stays prose, since the model is not
 * expected to author mixed blocks. Exported for direct unit coverage alongside the render-based
 * tests in `ReplyMarkdown.test.tsx`.
 */
export function splitMarkdownBlocks(source: string): MarkdownBlock[] {
  const normalized = source.replace(/\r\n/g, "\n").trim();
  if (normalized === "") return [];

  return normalized
    .split(/\n{2,}/)
    .map((rawBlock): MarkdownBlock => {
      const lines = rawBlock
        .split("\n")
        .map((line) => line.trim())
        .filter((line) => line.length > 0);

      if (lines.length > 0 && lines.every((line) => LIST_MARKER_PATTERN.test(line))) {
        return { type: "list", items: lines.map((line) => line.replace(LIST_MARKER_PATTERN, "")) };
      }

      return { type: "paragraph", text: lines.join("\n") };
    })
    .filter((block) => (block.type === "list" ? block.items.length > 0 : block.text.length > 0));
}

/**
 * Inline pass over one block's text (or one list item): `**bold**` -> `<strong>`, `[n]` -> a
 * superscript link when `n` has a real citation, else plain superscript text. Everything else is
 * emitted as a plain string child -- see this file's own header comment for why that alone is what
 * keeps raw HTML from ever executing. Not recursive: a citation marker nested inside `**bold**` is
 * not re-scanned, matching the persona prompt's own narration style (requirements.md §6:
 * `"**15 January 2027** [1]"` -- the two never nest).
 */
export function renderInline(text: string, citations: readonly ReplyCitation[], onOpenCitation: ReplyMarkdownProps["onOpenCitation"], keyPrefix: string): ReactNode[] {
  const citationsByN = new Map(citations.map((citation) => [citation.n, citation] as const));
  const nodes: ReactNode[] = [];
  let lastIndex = 0;
  let tokenIndex = 0;
  INLINE_TOKEN_PATTERN.lastIndex = 0;
  let match: RegExpExecArray | null;

  while ((match = INLINE_TOKEN_PATTERN.exec(text)) !== null) {
    if (match.index > lastIndex) {
      nodes.push(text.slice(lastIndex, match.index));
    }

    const [, bold, citationRef] = match;
    if (bold !== undefined) {
      nodes.push(<strong key={`${keyPrefix}-b-${tokenIndex}`}>{bold}</strong>);
    } else if (citationRef !== undefined) {
      const n = Number(citationRef);
      const citation = citationsByN.get(n);
      nodes.push(
        citation ? (
          <sup key={`${keyPrefix}-c-${tokenIndex}`}>
            <a
              href="#"
              className="reply-citation-ref"
              onClick={(event) => {
                event.preventDefault();
                onOpenCitation(citation);
              }}
            >
              [{n}]
            </a>
          </sup>
        ) : (
          <sup key={`${keyPrefix}-c-${tokenIndex}`}>[{n}]</sup>
        ),
      );
    }

    lastIndex = INLINE_TOKEN_PATTERN.lastIndex;
    tokenIndex += 1;
  }

  if (lastIndex < text.length) {
    nodes.push(text.slice(lastIndex));
  }

  return nodes;
}

export default function ReplyMarkdown({ text, citations, onOpenCitation }: ReplyMarkdownProps) {
  const blocks = splitMarkdownBlocks(text);

  return (
    <div className="reply-markdown">
      {blocks.map((block, index) =>
        block.type === "list" ? (
          <ul key={index}>
            {block.items.map((item, itemIndex) => (
              <li key={itemIndex}>{renderInline(item, citations, onOpenCitation, `b${index}-i${itemIndex}`)}</li>
            ))}
          </ul>
        ) : (
          <p key={index}>{renderInline(block.text, citations, onOpenCitation, `b${index}`)}</p>
        ),
      )}
    </div>
  );
}
