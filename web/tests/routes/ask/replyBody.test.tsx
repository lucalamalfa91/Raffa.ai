import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import ReplyBody, { splitBlocks } from "../../../src/routes/ask/ReplyBody";
import { splitEmailDraft } from "../../../src/routes/ask/draft";

const REPLY = `Certo: **migliorare** il rinnovo è plausibile. [1][2]

**Diagnosi**
- GitLab vale EUR 101000 l'anno. [3]
- Manca la durata del rinnovo.

**Piano e timing**
1. Questa settimana apri il confronto.
2. Poi prepara la controproposta. [1]`;

describe("splitBlocks", () => {
  it("turns bold-only lines into headings and dash / number lines into lists", () => {
    const kinds = splitBlocks(REPLY).map((b) => b.type);
    expect(kinds).toEqual(["p", "h", "ul", "h", "ol"]);
  });
});

describe("ReplyBody", () => {
  it("renders bold, headings, lists and source chips instead of raw markdown", async () => {
    render(<ReplyBody text={REPLY} sources={[{ title: "GitLab MSA", page: 4 }, { title: "Market data" }, { title: "Order form", page: 1, section: "§2" }]} />);
    expect(screen.queryByText(/\*\*/)).toBeNull();
    expect(screen.getByRole("heading", { name: "Diagnosi" })).toBeInTheDocument();
    expect(screen.getAllByRole("listitem").length).toBeGreaterThanOrEqual(4);
    expect(screen.getByText("migliorare").tagName).toBe("STRONG");
    const chip = screen.getAllByRole("button", { name: "1" })[0]!;
    await userEvent.hover(chip);
    expect(await screen.findByRole("tooltip")).toHaveTextContent("GitLab MSA");
    expect(screen.getByText("3 sources")).toBeInTheDocument();
  });

  it("never turns text into HTML", () => {
    render(<ReplyBody text={"<script>alert(1)</script> **ok**"} sources={[]} />);
    expect(document.querySelector("script")).toBeNull();
    expect(screen.getByText(/<script>/)).toBeInTheDocument();
  });
});

describe("splitEmailDraft", () => {
  it("takes the section under a draft heading out of the reply", () => {
    const text = `**Strategia**\n- Chiedi uno sconto.\n\n**Bozza email**\nOggetto: Rinnovo GitLab\nGentile team,\nvorremmo rivedere le condizioni.\nCordiali saluti\n\n**Rischi**\n- Nessuno.`;
    const { text: rest, draft } = splitEmailDraft(text);
    expect(draft?.subject).toBe("Rinnovo GitLab");
    expect(draft?.body).toContain("Gentile team");
    expect(draft?.body).not.toContain("Oggetto");
    expect(rest).toContain("**Rischi**");
    expect(rest).not.toContain("Gentile team");
  });

  it("finds a salutation-to-sign-off email with no heading, and nothing in plain prose", () => {
    const mail = `Ecco la bozza.\n\nDear team,\nplease confirm in writing that the practice is cancelled.\nBest regards,\n[Your name]`;
    expect(splitEmailDraft(mail).draft?.body).toContain("please confirm");
    expect(splitEmailDraft("**Diagnosi**\n- niente da segnalare.").draft).toBeNull();
  });
});
