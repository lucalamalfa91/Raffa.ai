import { describe, expect, it } from "vitest";
import { artifactRequest, buildArtifact, threadItems, type PlacedArtifact } from "../../../src/routes/ask/artifacts";

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

describe("artifactRequest", () => {
  it("still reads a request to build something", () => {
    expect(artifactRequest("Crea una dashboard")).toBe("dashboard");
    expect(artifactRequest("fammi i KPI")).toBe("kpi");
    expect(artifactRequest("scrivi un brief")).toBe("document");
    expect(artifactRequest("When is the Salesforce notice?")).toBeNull();
  });

  it("reads a change to a KPI or dashboard built in the conversation as a request for its next version", () => {
    expect(artifactRequest("modifica il KPI", ["kpi"])).toBe("kpi");
    expect(artifactRequest("visualizza il kpi per anno, voglio capire il trend", ["kpi"])).toBe("kpi");
    expect(artifactRequest("crea una nuova versione del KPI", ["kpi"])).toBe("kpi");
    expect(artifactRequest("aggiorna la dashboard", ["dashboard", "kpi"])).toBe("dashboard");
    expect(artifactRequest("fanne una nuova versione", ["dashboard", "kpi"])).toBe("kpi");
    expect(artifactRequest("Update the KPIs by year", ["kpi"])).toBe("kpi");
  });

  it("leaves a change to nothing built, or a question about the content, to Raffa's answer", () => {
    expect(artifactRequest("modifica il KPI")).toBeNull();
    expect(artifactRequest("aggiorna la dashboard", ["kpi"])).toBeNull();
    expect(artifactRequest("Cosa cambia nei KPI se rinnovo Salesforce?", ["kpi"])).toBeNull();
    expect(artifactRequest("modifica il contratto Salesforce", ["kpi"])).toBeNull();
  });

  it("numbers a later version", () => {
    expect(buildArtifact("kpi", [], t, "2026-11-04").version).toBe(1);
    expect(buildArtifact("kpi", [], t, "2026-11-04", 2).version).toBe(2);
  });
});
