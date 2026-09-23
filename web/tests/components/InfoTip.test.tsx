import { afterEach, describe, expect, it } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import InfoTip, { CopyTip } from "../../src/components/InfoTip";
import ScreenTitle from "../../src/components/ScreenTitle";
import { SCREEN_GUIDES } from "../../src/components/infoTipCopy";

const SEEN_GUIDES_KEY = "raffa.tips.seenGuides";

describe("InfoTip", () => {
  afterEach(() => {
    window.localStorage.clear();
  });

  it("is a labelled button described by its bubble, and never adds its glyph to the label's text", () => {
    render(
      <table>
        <thead>
          <tr>
            <th>
              Status
              <InfoTip label="What the statuses mean">
                <p>Completed: validated.</p>
              </InfoTip>
            </th>
          </tr>
        </thead>
      </table>,
    );

    const trigger = screen.getByRole("button", { name: "What the statuses mean" });
    expect(trigger).toHaveAccessibleDescription("Completed: validated.");
    expect(screen.getByRole("columnheader").textContent).toBe("Status");
  });

  it("opens on a tap and closes on a press anywhere else", () => {
    render(
      <InfoTip label="About this">
        <p>Explained.</p>
      </InfoTip>,
    );

    const bubble = screen.getByRole("tooltip", { hidden: true });
    expect(bubble).not.toHaveClass("is-open");

    fireEvent.click(screen.getByRole("button", { name: "About this" }));
    expect(bubble).toHaveClass("is-open");

    fireEvent.pointerDown(document.body);
    expect(bubble).not.toHaveClass("is-open");
  });

  it("closes on Escape", () => {
    render(
      <InfoTip label="About this">
        <p>Explained.</p>
      </InfoTip>,
    );

    const trigger = screen.getByRole("button", { name: "About this" });
    fireEvent.focus(trigger);
    expect(screen.getByRole("tooltip", { hidden: true })).toHaveClass("is-open");

    fireEvent.keyDown(trigger, { key: "Escape" });
    expect(screen.getByRole("tooltip", { hidden: true })).not.toHaveClass("is-open");
  });
});

describe("CopyTip", () => {
  it("renders one paragraph per line and the small print last", () => {
    render(<CopyTip tip={{ label: "What P50 means", lines: ["The market median.", "Half pay less."], meta: "Source: market data." }} />);

    const trigger = screen.getByRole("button", { name: "What P50 means" });
    expect(trigger).toHaveAccessibleDescription("The market median. Half pay less. Source: market data.");
    expect(screen.getByText("Source: market data.")).toHaveClass("info-tip-meta");
  });
});

describe("ScreenTitle", () => {
  afterEach(() => {
    window.localStorage.clear();
  });

  it("keeps the heading's name the screen's own and puts the guide beside it", () => {
    render(<ScreenTitle guide="documents">Documents</ScreenTitle>);

    expect(screen.getByRole("heading", { level: 2, name: "Documents" })).toHaveClass("screen-title");
    expect(screen.getByRole("button", { name: SCREEN_GUIDES.documents.label })).toHaveAccessibleDescription(
      new RegExp(SCREEN_GUIDES.documents.lines[0].slice(0, 30)),
    );
  });

  it("stays highlighted until the guide is opened once, then remembers it in this browser", () => {
    const { unmount } = render(<ScreenTitle guide="renewals">Renewals</ScreenTitle>);
    const trigger = screen.getByRole("button", { name: SCREEN_GUIDES.renewals.label });
    expect(trigger).toHaveClass("is-unseen");

    fireEvent.mouseEnter(trigger);
    expect(trigger).not.toHaveClass("is-unseen");
    expect(JSON.parse(window.localStorage.getItem(SEEN_GUIDES_KEY) ?? "[]")).toEqual(["renewals"]);
    unmount();

    render(<ScreenTitle guide="renewals">Renewals</ScreenTitle>);
    expect(screen.getByRole("button", { name: SCREEN_GUIDES.renewals.label })).not.toHaveClass("is-unseen");
  });

  it("remembers each screen's guide on its own", () => {
    window.localStorage.setItem(SEEN_GUIDES_KEY, JSON.stringify(["savings"]));
    render(
      <>
        <ScreenTitle guide="savings">Savings</ScreenTitle>
        <ScreenTitle guide="quotes">Quote check</ScreenTitle>
      </>,
    );

    expect(screen.getByRole("button", { name: SCREEN_GUIDES.savings.label })).not.toHaveClass("is-unseen");
    expect(screen.getByRole("button", { name: SCREEN_GUIDES.quotes.label })).toHaveClass("is-unseen");
  });

  it("still renders when the stored value is unreadable", () => {
    window.localStorage.setItem(SEEN_GUIDES_KEY, "{not json");
    render(<ScreenTitle guide="members">Workspace &amp; members</ScreenTitle>);

    expect(screen.getByRole("button", { name: SCREEN_GUIDES.members.label })).toHaveClass("is-unseen");
  });
});
