/** A drafted email found inside a reply. */
export interface EmailDraft {
  subject: string;
  body: string;
}

export interface SplitReply {
  /** The reply with the draft cut out (the draft is shown in the side panel instead). */
  text: string;
  draft: EmailDraft | null;
}

const HEADING = /^\*\*([^*]+)\*\*:?$/;
const DRAFT_HEADING = /(e-?mail|mail|bozza|draft|messaggio|message|lettera|letter)/i;
const SUBJECT = /^\s*(?:\*\*)?(subject|oggetto)(?:\*\*)?\s*:\s*(?:\*\*)?(.+?)(?:\*\*)?\s*$/i;
const SALUTATION = /^\s*(dear|hello|hi|buongiorno|buonasera|gentile|egregio|spett\.?le|ciao|salve)\b/i;

/**
 * Finds the email Raffa drafted in a reply: the section under a heading that names it ("**Draft email**",
 * "**Bozza email**", …), or a block that starts with a `Subject:` / `Oggetto:` line, or — failing both — a
 * paragraph run that opens with a salutation and ends with a sign-off. The email is returned apart from the
 * rest of the reply, which is what stays in the chat (as Claude shows an artifact beside the conversation).
 */
export function splitEmailDraft(markdown: string): SplitReply {
  const lines = markdown.replace(/\r\n/g, "\n").split("\n");
  const headingAt = (i: number) => HEADING.exec(lines[i]!.trim());

  // 1. A section whose heading names an email / draft.
  for (let i = 0; i < lines.length; i += 1) {
    const h = headingAt(i);
    if (!h || !DRAFT_HEADING.test(h[1]!)) continue;
    let end = i + 1;
    while (end < lines.length && !headingAt(end)) end += 1;
    const section = lines.slice(i + 1, end);
    if (section.join("").trim().length < 40) continue;
    return cut(lines, i, end, h[1]!.trim(), section);
  }

  // 2. A Subject:/Oggetto: line opens the email; it runs to the next heading.
  for (let i = 0; i < lines.length; i += 1) {
    if (!SUBJECT.test(lines[i]!)) continue;
    let end = i + 1;
    while (end < lines.length && !headingAt(end)) end += 1;
    return cut(lines, i, end, "", lines.slice(i, end));
  }

  // 3. A salutation … sign-off paragraph run.
  for (let i = 0; i < lines.length; i += 1) {
    if (!SALUTATION.test(lines[i]!)) continue;
    let end = i + 1;
    while (end < lines.length && !headingAt(end)) end += 1;
    const run = lines.slice(i, end).join("\n");
    if (/(best regards|kind regards|sincerely|cordiali saluti|distinti saluti|saluti|cordialmente)/i.test(run)) {
      return cut(lines, i, end, "", lines.slice(i, end));
    }
  }

  return { text: markdown, draft: null };
}

function cut(lines: string[], from: number, to: number, headingText: string, section: string[]): SplitReply {
  let subject = "";
  const body: string[] = [];
  for (const line of section) {
    const s = SUBJECT.exec(line);
    if (s && subject === "") subject = s[2]!.trim();
    else body.push(line);
  }
  const text = [...lines.slice(0, from), ...lines.slice(to)].join("\n").replace(/\n{3,}/g, "\n\n").trim();
  return {
    text,
    draft: { subject: subject || headingText, body: body.join("\n").replace(/^\s*\n+|\n+\s*$/g, "").replace(/\*\*/g, "") },
  };
}
