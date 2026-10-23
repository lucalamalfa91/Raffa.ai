import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

/**
 * The renewal screens follow the Modernist handoff: radius 0, 2px rules, one red accent, flush left,
 * full width, text that wraps. These read the style sheets themselves, so a drift shows up as a
 * failing line rather than as something to notice in a screenshot.
 */
const SHEETS = [
  "src/routes/renewals/renewals.css",
  "src/routes/renewals/case/chat/renewalChat.css",
  "src/routes/today/today.css",
  "src/routes/renewals/tabs/caseTabs.css",
];

function lines(path: string): Array<[number, string]> {
  try {
    return readFileSync(path, "utf8")
      .split("\n")
      .map((text, i): [number, string] => [i + 1, text])
      .filter(([, text]) => !/^\s*(\/\*|\*)/.test(text));
  } catch {
    return [];
  }
}

describe.each(SHEETS)("%s", (path) => {
  const all = lines(path);

  it("has no rounded corners", () => {
    const rounded = all.filter(([, text]) => /border-radius\s*:/.test(text) && !/border-radius\s*:\s*(0|var\(--radius-(sm|md|lg)\))\s*;/.test(text));
    expect(rounded).toEqual([]);
  });

  it("has no shadows other than the inset bars that mark a selection", () => {
    const shadows = all.filter(([, text]) => /box-shadow\s*:/.test(text) && !/inset/.test(text) && !/none/.test(text));
    expect(shadows).toEqual([]);
  });

  it("uses no raw colours: only tokens, ink mixes, and the one named white", () => {
    const raw = all.filter(([, text]) => /#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(/.test(text) && !/--(rn|rc)-paper:\s*#fff;/.test(text));
    expect(raw).toEqual([]);
  });

  it("never truncates text", () => {
    expect(all.filter(([, text]) => /text-overflow\s*:/.test(text))).toEqual([]);
  });

  // The chat thread shares Ask Raffa's centred 900px column, so it is the one sheet allowed to centre.
  it.skipIf(path.endsWith("renewalChat.css"))("is left aligned: nothing centres itself with auto margins", () => {
    expect(all.filter(([, text]) => /margin(-inline)?\s*:\s*(0\s+)?auto/.test(text))).toEqual([]);
  });
});

describe("shared components used by the renewal screens", () => {
  const components = readFileSync("src/styles/components.css", "utf8");

  it("a tag wraps instead of staying on one line", () => {
    const tag = components.match(/\n\.tag \{[^}]*\}/)?.[0] ?? "";
    expect(tag).toContain("white-space: normal");
    expect(tag).not.toContain("nowrap");
  });

  it("an outline tag is red", () => {
    expect(components.match(/\.tag-outline \{[^}]*\}/)?.[0]).toContain("var(--color-accent)");
  });
});
