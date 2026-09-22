import { describe, expect, it } from "vitest";
import { humanizeReplyText } from "../../../../src/routes/ask/reply/humanizeReplyText";
import type { ReplyCitation } from "../../../../src/routes/ask/reply/replyTypes";

const citation: ReplyCitation = {
  n: 1,
  corpus: "tenant",
  title: "Salesforce · MSA 2024",
  subtitle: "p.1",
  snippet: "notice",
};

describe("humanizeReplyText", () => {
  it("replaces calc placeholders and GUIDs with the citation title", () => {
    const guid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    expect(humanizeReplyText(`Score is {calc:criticality[${guid}]} for Document:${guid}.`, [citation])).toBe(
      "Score is Salesforce · MSA 2024 for Salesforce · MSA 2024.",
    );
  });

  it("uses a short fallback when citations are themselves identifiers", () => {
    expect(
      humanizeReplyText("See 11111111-1111-1111-1111-111111111111", [
        { ...citation, title: "22222222-2222-2222-2222-222222222222" },
      ]),
    ).toBe("See this contract");
  });
});

describe("humanizeReplyText -- leaked citation keys", () => {
  const contractId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
  const documentId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
  const cited: ReplyCitation[] = [
    { ...citation, n: 1, contractId },
    { ...citation, n: 2, title: "Atlassian · MSA", contractId: null, documentId },
  ];

  it("turns a bracketed fact key on a cited contract into that citation's own [n] marker", () => {
    expect(humanizeReplyText(`It expires on 2029-01-06 and does not renew automatically.[fact:${contractId}:renewal]`, cited)).toBe(
      "It expires on 2029-01-06 and does not renew automatically. [1]",
    );
  });

  it("maps a document-keyed chunk citation and collapses a repeated marker", () => {
    expect(humanizeReplyText(`Notice is 90 days [fact:${documentId}:chunk[3]] [fact:${documentId}:chunk[4]].`, cited)).toBe(
      "Notice is 90 days [2].",
    );
  });

  it("drops a key that matches no citation, without leaving a gap before the full stop", () => {
    expect(humanizeReplyText("The cap is 12 months [fact:cccccccc-cccc-cccc-cccc-cccccccccccc:clause] .", cited)).toBe("The cap is 12 months.");
    expect(humanizeReplyText("Saving [fact:dddddddd-dddd-dddd-dddd-dddddddddddd:saving[0]] of CHF 80k", cited)).toBe("Saving of CHF 80k");
  });

  it("handles a bare (unbracketed) key and never prints 'fact:' or an id", () => {
    const out = humanizeReplyText(`Give notice by 18 Oct 2026 fact:${contractId}:priced-line[Sales-Cloud].unitPrice.`, cited);
    expect(out).toBe("Give notice by 18 Oct 2026 [1].");
    expect(out).not.toMatch(/fact:/);
    expect(out).not.toMatch(/[0-9a-f]{8}-/);
  });
});
