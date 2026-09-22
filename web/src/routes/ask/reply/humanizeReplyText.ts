import type { ReplyCitation } from "./replyTypes";

const GUID = "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}";
const GUID_PATTERN = new RegExp(`\\b${GUID}\\b`, "i");

/**
 * The pack's own citation keys, when the model leaves them in its prose instead of an `[n]`
 * marker (`AskCopilotService`: `fact:{contractId}:renewal`, `fact:{clauseId}:clause`,
 * `fact:{documentId}:chunk[3]`, `fact:{contractId}:priced-line[…].unitPrice`, `fact:{id}:saving[0]`).
 * Bracketed or bare; the tail may carry one level of `[…]`.
 */
const BRACKETED_FACT_KEY_PATTERN = /\[fact:([^\]:\s]+):((?:[^[\]]|\[[^\]]*\])*)\]/gi;
// The tail runs to whitespace or closing punctuation; a dot inside it (`…].unitPrice`) is part of the key.
const BARE_FACT_KEY_PATTERN = new RegExp(`\\bfact:(${GUID}):(?:[^\\s.,;:!?()[\\]]|\\.(?=\\w)|\\[[^\\]]*\\])*`, "gi");

function looksLikeGuid(value: string): boolean {
  return GUID_PATTERN.test(value);
}

export function replyDisplayName(citations: readonly ReplyCitation[]): string {
  for (const citation of citations) {
    const title = citation.title.trim();
    if (title !== "" && !looksLikeGuid(title)) return title;
  }
  return "this contract";
}

/**
 * A leaked citation key becomes this reply's own `[n]` marker when its id is a cited contract or
 * document (the same superscript `ReplyMarkdown` already renders as a link to the card); a key
 * pointing at nothing on the card list is dropped -- a reader never sees `fact:` or an id.
 */
function factKeyToMarker(id: string, citations: readonly ReplyCitation[]): string {
  const match = citations.find((citation) => citation.contractId === id || citation.documentId === id);
  return match === undefined ? "" : `[${match.n}]`;
}

function tidyAfterRemoval(text: string): string {
  return text
    .replace(/(\[\d+\])(?:\s*\1)+/g, "$1") // the same marker twice in a row
    .replace(/([.,;:!?])(\[\d+\])/g, "$1 $2") // "automatically.[1]" -> "automatically. [1]"
    .replace(/[ \t]+([.,;:!?])/g, "$1") // no space left before punctuation
    .replace(/[ \t]{2,}/g, " ")
    .replace(/[ \t]+$/gm, "")
    .trim();
}

export function humanizeReplyText(text: string, citations: readonly ReplyCitation[] = []): string {
  const name = replyDisplayName(citations);
  const withoutKeys = text
    .replace(BRACKETED_FACT_KEY_PATTERN, (_match, id: string) => factKeyToMarker(id, citations))
    .replace(BARE_FACT_KEY_PATTERN, (_match, id: string) => factKeyToMarker(id, citations));
  return tidyAfterRemoval(
    withoutKeys
      .replace(/\{calc:[^}]+\}/gi, name)
      .replace(new RegExp(`\\bDocument:${GUID}\\b`, "gi"), name)
      .replace(new RegExp(`\\b${GUID}\\b`, "gi"), name),
  );
}
