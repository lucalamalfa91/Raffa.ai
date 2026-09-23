import { beforeEach, describe, expect, it } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import RailResizeHandle from "../../../src/components/shell/RailResizeHandle";
import {
  RAIL_WIDTH_DEFAULT,
  RAIL_WIDTH_MAX,
  RAIL_WIDTH_MIN,
  clampRailWidth,
  useRailWidth,
} from "../../../src/components/shell/useRailWidth";

const STORAGE_KEY = "raffa.shell.railWidth";

/** The shell's own wiring, minus the rest of the shell: the width lands in `--shell-rail-width`. */
function Shell() {
  const railWidth = useRailWidth();
  return (
    <div data-testid="layout" style={{ ["--shell-rail-width" as string]: `${railWidth.width}px` }}>
      <RailResizeHandle {...railWidth} />
    </div>
  );
}

function railWidth(): string {
  return screen.getByTestId("layout").style.getPropertyValue("--shell-rail-width");
}

describe("resizable rail (RailResizeHandle + useRailWidth)", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it("starts at the mockup's 232px and exposes itself as a vertical window splitter", () => {
    render(<Shell />);

    const handle = screen.getByRole("separator", { name: "Resize the sidebar" });
    expect(handle).toHaveAttribute("aria-orientation", "vertical");
    expect(handle).toHaveAttribute("aria-valuenow", String(RAIL_WIDTH_DEFAULT));
    expect(handle).toHaveAttribute("aria-valuemin", String(RAIL_WIDTH_MIN));
    expect(handle).toHaveAttribute("aria-valuemax", String(RAIL_WIDTH_MAX));
    expect(railWidth()).toBe("232px");
  });

  it("follows a drag and remembers the width once the drag ends", () => {
    render(<Shell />);
    const handle = screen.getByRole("separator", { name: "Resize the sidebar" });

    fireEvent.pointerDown(handle, { button: 0, clientX: 232, pointerId: 1 });
    expect(document.body).toHaveClass("is-resizing-rail");
    fireEvent.pointerMove(handle, { clientX: 300, pointerId: 1 });
    expect(railWidth()).toBe("300px");
    expect(window.localStorage.getItem(STORAGE_KEY)).toBeNull();

    fireEvent.pointerUp(handle, { clientX: 332, pointerId: 1 });
    expect(railWidth()).toBe("332px");
    expect(window.localStorage.getItem(STORAGE_KEY)).toBe("332");
    expect(document.body).not.toHaveClass("is-resizing-rail");
  });

  it("never goes past its bounds, however far it is dragged", () => {
    render(<Shell />);
    const handle = screen.getByRole("separator", { name: "Resize the sidebar" });

    fireEvent.pointerDown(handle, { button: 0, clientX: 232, pointerId: 1 });
    fireEvent.pointerMove(handle, { clientX: 2000, pointerId: 1 });
    expect(railWidth()).toBe(`${RAIL_WIDTH_MAX}px`);
    fireEvent.pointerUp(handle, { clientX: -500, pointerId: 1 });
    expect(railWidth()).toBe(`${RAIL_WIDTH_MIN}px`);
  });

  it("resizes from the keyboard: arrows step 16px, Home and End jump to the bounds", async () => {
    const user = userEvent.setup();
    render(<Shell />);
    const handle = screen.getByRole("separator", { name: "Resize the sidebar" });

    handle.focus();
    await user.keyboard("{ArrowRight}{ArrowRight}");
    expect(handle).toHaveAttribute("aria-valuenow", "264");
    await user.keyboard("{ArrowLeft}");
    expect(railWidth()).toBe("248px");
    await user.keyboard("{End}");
    expect(railWidth()).toBe(`${RAIL_WIDTH_MAX}px`);
    await user.keyboard("{Home}");
    expect(railWidth()).toBe(`${RAIL_WIDTH_MIN}px`);
    expect(window.localStorage.getItem(STORAGE_KEY)).toBe(String(RAIL_WIDTH_MIN));
  });

  it("a double-click puts the default width back and forgets the stored one", async () => {
    window.localStorage.setItem(STORAGE_KEY, "380");
    const user = userEvent.setup();
    render(<Shell />);
    expect(railWidth()).toBe("380px");

    await user.dblClick(screen.getByRole("separator", { name: "Resize the sidebar" }));

    expect(railWidth()).toBe("232px");
    expect(window.localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it("ignores a stored width it cannot use", () => {
    window.localStorage.setItem(STORAGE_KEY, "not-a-number");
    render(<Shell />);
    expect(railWidth()).toBe("232px");
  });

  it("clamps and rounds any width", () => {
    expect(clampRailWidth(10)).toBe(RAIL_WIDTH_MIN);
    expect(clampRailWidth(10_000)).toBe(RAIL_WIDTH_MAX);
    expect(clampRailWidth(250.6)).toBe(251);
    expect(clampRailWidth(Number.NaN)).toBe(RAIL_WIDTH_DEFAULT);
  });
});
