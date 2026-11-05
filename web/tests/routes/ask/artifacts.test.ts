import { describe, expect, it } from "vitest";
import { buildArtifact, storedArtifacts, threadItems, type PlacedArtifact } from "../../../src/routes/ask/artifacts";

const t = (id: string) => id;
const placed = (id: string, conversationId: string | null, afterMessageId: string | null): PlacedArtifact => ({
  artifact: { ...buildArtifact("kpi", [], t, "2026-11-04"), id },
  conversationId,
  afterMessageId,
  request: null,
});
const order = (items: ReturnType<typeof threadItems<{ id: string }>>) => items.map((i) => i.message?.id ?? i.content?.artifact.id);

describe("threadItems", () => {
  it("puts each content right after the message it followed, in the order things happened", () => {
    const messages = [{ id: "q1" }, { id: "a1" }, { id: "q2" }, { id: "a2" }];
    const contents = [placed("first", "A", null), placed("dash", "A", "a1"), placed("kpi", "A", "a1"), placed("last", "A", "a2")];
    expect(order(threadItems(messages, contents, "A"))).toEqual(["first", "q1", "a1", "dash", "kpi", "q2", "a2", "last"]);
  });

  it("leaves out another conversation's contents, and a new conversation shows only its own", () => {
    const contents = [placed("a", "A", "a1"), placed("b", "B", null), placed("draft", null, null)];
    expect(order(threadItems([{ id: "a1" }], contents, "A"))).toEqual(["a1", "a"]);
    expect(order(threadItems([], contents, null))).toEqual(["draft"]);
  });
});


describe("storedArtifacts", () => {
  const t = (id: string) => id;
  const artifact = (kind: "kpi" | "dashboard") => ({
    kind, title: kind, subtitle: "", focusSupplier: "GitLab", usedPortfolio: false, usedMarket: true,
    tiles: [{ label: "A", value: "1", hint: "", tone: "" }], charts: [], tables: [], markdown: "",
  });

  it("reads the agents' content back from the stored replies, each after its own reply, numbering versions per kind", () => {
    const messages = [
      { id: "m1", payload: null },
      { id: "m2", payload: { artifact: artifact("kpi") } },
      { id: "m3", payload: { artifact: artifact("dashboard") } },
      { id: "m4", payload: { artifact: artifact("kpi") } },
    ];
    const out = storedArtifacts(messages, "c1", t);
    expect(out.map((c) => [c.afterMessageId, c.artifact.id, c.artifact.version])).toEqual([
      ["m2", "artifact-m2", 1],
      ["m3", "artifact-m3", 1],
      ["m4", "artifact-m4", 2],
    ]);
    expect(out[0]!.artifact.subtitle).toContain("GitLab");
    expect(out[0]!.conversationId).toBe("c1");
  });

  it("keeps the server id, version, contract and missing-data state across a conversation reload", () => {
    const messages = [{
      id: "m1",
      payload: {
        artifact: {
          ...artifact("kpi"),
          id: "stable-artifact-id",
          version: 3,
          status: "draft" as const,
          contractId: "11111111-1111-1111-1111-111111111111",
          missingInputs: ["renewal_quote", "actual_usage"],
        },
      },
    }];

    const [placed] = storedArtifacts(messages, "c1", t);

    expect(placed?.artifact).toMatchObject({
      id: "stable-artifact-id",
      version: 3,
      status: "draft",
      contractId: "11111111-1111-1111-1111-111111111111",
      missingInputs: ["renewal_quote", "actual_usage"],
    });
  });
});
