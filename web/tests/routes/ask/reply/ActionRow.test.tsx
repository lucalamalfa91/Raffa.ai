import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import ActionRow from "../../../../src/routes/ask/reply/ActionRow";
import type { ReplyAction } from "../../../../src/routes/ask/reply/replyTypes";

function renderActionRow(actions: readonly ReplyAction[]) {
  return render(
    <MemoryRouter>
      <ActionRow actions={actions} />
    </MemoryRouter>,
  );
}

// Task E13/F09/US01/T02's own "Tests required" row: "unit | card variants, actions, layouts per kind".
describe("ActionRow (task E13/F09/US01/T02, AC-3)", () => {
  it("renders one .btn-primary / .btn-secondary link per action, from { label, href, kind }", () => {
    renderActionRow([
      { label: "Open Contract 360 →", href: "/contracts/contract-1", kind: "primary" },
      { label: "Track it in Renewals", href: "/renewals?select=contract-1", kind: "secondary" },
    ]);

    const primary = screen.getByRole("link", { name: "Open Contract 360 →" });
    expect(primary).toHaveClass("btn", "btn-primary");
    expect(primary).toHaveAttribute("href", "/contracts/contract-1");

    const secondary = screen.getByRole("link", { name: "Track it in Renewals" });
    expect(secondary).toHaveClass("btn", "btn-secondary");
    expect(secondary).toHaveAttribute("href", "/renewals?select=contract-1");
  });

  it("renders nothing for an empty actions list", () => {
    const { container } = renderActionRow([]);
    expect(container).toBeEmptyDOMElement();
  });
});
