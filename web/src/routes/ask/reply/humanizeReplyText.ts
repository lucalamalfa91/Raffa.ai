import type { ReplyCitation } from "./replyTypes";

/** R-ASK-08: identifiers/GUIDs must never render in Ask reply prose. */
const GUID_PATTERN = /\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b/i;

function looksLikeGuid(value: string): boolean {
  return GUID_PATTERN.test(value);
}

/**
 * A human name to substitute for an identifier: a citation's supplier/document title when it is
 * not itself a guid, otherwise a short fallback. Never returns a guid.
 */
export function replyDisplayName(citations: readonly ReplyCitation[]): string {
  for (const citation of citations) {
    const title = citation.title.trim();
    if (title !== "" && !looksLikeGuid(title)) return title;
  }
  return "this contract";
}

/**
 * Strips formula placeholders and GUIDs from `answerMarkdown` before render (R-ASK-08). `{calc:
 * criticality[<guid>]}` and `Document:<guid>` chips become the bound supplier/document name.
 */
export function humanizeReplyText(text: string, citations: readonly ReplyCitation[] = []): string {
  const name = replyDisplayName(citations);
  return text
    .replace(/\{calc:[^}]+\}/gi, name)
    .replace(/\bDocument:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b/gi, name)
    .replace(/\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b/gi, name);
}
