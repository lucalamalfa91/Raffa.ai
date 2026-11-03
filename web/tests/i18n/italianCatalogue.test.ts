import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { IT } from "../../src/i18n/it";
import { format } from "../../src/i18n/translate";

const SRC = join(__dirname, "../../src");
const SCANNED = ["routes/renewals", "routes/today", "components/shell", "routes/ask", "routes/quotes", "routes/savings", "routes/settings", "routes/workspace", "routes/documents", "components/table"];

function sources(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) return sources(path);
    return /\.tsx?$/.test(name) ? [path] : [];
  });
}

/** Every plain-literal id handed to `t(...)`, `tr(...)` or `msg(...)`. */
function idsUsed(): Map<string, string> {
  const found = new Map<string, string>();
  const call = /(?<![\w.$])(?:t|tr|msg)\(\s*"((?:[^"\\]|\\.)*)"/g;
  for (const root of SCANNED) {
    for (const file of sources(join(SRC, root))) {
      for (const match of readFileSync(file, "utf8").matchAll(call)) {
        found.set(JSON.parse(`"${match[1]}"`) as string, file.replace(SRC, "src"));
      }
    }
  }
  return found;
}

const placeholders = (text: string): string[] => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1] ?? "").sort();

describe("the Italian catalogue", () => {
  const used = idsUsed();

  it("finds the messages the screens use", () => {
    expect(used.size).toBeGreaterThan(200);
  });

  it("has Italian for every message the renewal screens and engine use", () => {
    const missing = [...used].filter(([id]) => IT[id] === undefined).map(([id, file]) => `${file}: ${id}`);
    expect(missing).toEqual([]);
  });

  // Italian may drop English-only plural markers ({s1} in "difference{s1}"), never invent one.
  it("uses only {placeholders} the English text has, and keeps the data ones", () => {
    const plural = /^s\d*$/;
    const broken = Object.entries(IT)
      .filter(([id, it]) => {
        const en = placeholders(id);
        const itp = placeholders(it);
        return itp.some((p) => !en.includes(p)) || en.filter((p) => !plural.test(p)).some((p) => !itp.includes(p));
      })
      .map(([id]) => id);
    expect(broken).toEqual([]);
  });

  it("fills placeholders, including a nested message", () => {
    expect(format("Hello {name}", { name: "Eva" })).toBe("Hello Eva");
    expect(format("{a} / {b}", { a: { id: "Done" }, b: 2 }, "it")).toBe("Fatto / 2");
    expect(format("x{gone}y", { gone: null })).toBe("xy");
  });
});
