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
