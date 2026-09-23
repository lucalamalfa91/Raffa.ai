import { describe, expect, it } from "vitest";
import { ASK_PROMPTS, buildAskLaunch } from "../../../src/components/ask-bar/askLaunch";

describe("buildAskLaunch", () => {
  it("opens a new chat that asks the question -- bound to a contract with ?scope=, plain /ask without one", () => {
    expect(buildAskLaunch("  Where can we save?  ")).toEqual({ to: "/ask", state: { query: "Where can we save?", newChat: true } });
    expect(buildAskLaunch("Why?", "c-1")).toEqual({ to: "/ask?scope=c-1", state: { query: "Why?", newChat: true } });
    expect(buildAskLaunch("Why?", "  ")).toMatchObject({ to: "/ask" });
    expect(buildAskLaunch("Why?", "a b")).toMatchObject({ to: "/ask?scope=a%20b" });
  });
});

describe("ASK_PROMPTS", () => {
  it("phrases each question for the Ask intent it is meant for", () => {
    expect(ASK_PROMPTS.renewalApproach("Salesforce")).toBe("How should we approach the Salesforce renewal?");
    expect(ASK_PROMPTS.negotiationEmail("Salesforce")).toBe("Draft the renewal negotiation email for Salesforce");
    // "email to" would read as the *send* gap; the draft is asked for without it.
    expect(ASK_PROMPTS.negotiationEmail("Salesforce")).not.toMatch(/e-?mail\s+to/i);
    // A timing word vetoes the send gap; the send request must not carry one.
    expect(ASK_PROMPTS.sendNotice("Salesforce")).not.toMatch(/\b(when|deadline|by when)\b/i);
    expect(ASK_PROMPTS.reminder("Salesforce")).toMatch(/\bremind me\b/i);
    expect(ASK_PROMPTS.exportBrief("Salesforce")).toMatch(/\bexport/i);
    expect(ASK_PROMPTS.startFirstAmong([])).toBe("Which renewals should we start first?");
    expect(ASK_PROMPTS.startFirstAmong(["A", "B"])).toBe("Which of these renewals should we start first: A, B?");
  });
});
