import { describe, expect, it } from "vitest";
import { buildArtifact, threadItems, type PlacedArtifact } from "../../../src/routes/ask/artifacts";

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

