import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import AccountMenu, { initials } from "../../../src/components/shell/AccountMenu";
import { I18nProvider, LANG_STORAGE_KEY } from "../../../src/i18n/I18nProvider";

function renderMenu(over: Partial<Parameters<typeof AccountMenu>[0]> = {}) {
  const onSignOut = vi.fn();
  render(
    <MemoryRouter>
      <I18nProvider>
        <AccountMenu workspaceName="Helvetia Foods AG" role="admin" userLabel="luca.lamalfa@helvetia-foods.example" onSignOut={onSignOut} {...over} />
      </I18nProvider>
    </MemoryRouter>,
  );
  return onSignOut;
}

beforeEach(() => window.localStorage.clear());
afterEach(() => window.localStorage.clear());

describe("AccountMenu", () => {
  it("makes initials from a name or an address", () => {
    expect(initials("luca.lamalfa@helvetia-foods.example")).toBe("LL");
    expect(initials("Marta Keller")).toBe("MK");
    expect(initials("x")).toBe("X");
  });

  it("switches the language between IT and EN and remembers it", async () => {
    renderMenu();
    const user = userEvent.setup();
    const it = screen.getByRole("button", { name: "IT" });
    const en = screen.getByRole("button", { name: "EN" });
    expect(en).toHaveAttribute("aria-pressed", "true");
    await user.click(it);
    expect(window.localStorage.getItem(LANG_STORAGE_KEY)).toBe("it");
    expect(screen.getByRole("button", { name: "IT" })).toHaveAttribute("aria-pressed", "true");
    await user.click(screen.getByRole("button", { name: "EN" }));
    expect(window.localStorage.getItem(LANG_STORAGE_KEY)).toBe("en");
  });

  it("shows the user, role and workspace, the admin's members link and signs out", async () => {
    const onSignOut = renderMenu();
    const user = userEvent.setup();
    expect(screen.queryByRole("menu")).toBeNull();
    await user.click(screen.getByRole("button", { name: /Account: luca/ }));
    const menu = screen.getByRole("menu");
    expect(menu).toHaveTextContent("luca.lamalfa@helvetia-foods.example");
    expect(menu).toHaveTextContent("Workspace Admin");
    expect(menu).toHaveTextContent("Helvetia Foods AG");
    expect(screen.getByRole("menuitem", { name: "Workspace & members" })).toHaveAttribute("href", "/workspace/members");
    expect(screen.getByRole("menuitem", { name: "Documents" })).toHaveAttribute("href", "/documents");
    await user.click(screen.getByRole("menuitem", { name: "Sign out" }));
    expect(onSignOut).toHaveBeenCalledOnce();
  });

  it("gives a Procurement member the roster without the admin wording, and closes on Escape", async () => {
    renderMenu({ role: "procurement", userLabel: "Marta Keller" });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /Account: Marta/ }));
    expect(screen.getByRole("menuitem", { name: "Workspace members" })).toBeInTheDocument();
    expect(screen.getByRole("menu")).toHaveTextContent("Procurement");
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("menu")).toBeNull();
  });

  it("offers 'Switch or create a workspace' only when the host can do it", async () => {
    const onSwitchWorkspace = vi.fn();
    renderMenu({ onSwitchWorkspace });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /Account: luca/ }));
    await user.click(screen.getByRole("menuitem", { name: "Switch or create a workspace" }));
    expect(onSwitchWorkspace).toHaveBeenCalledOnce();
  });

  it("has no switch entry without a handler", async () => {
    renderMenu();
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /Account: luca/ }));
    expect(screen.queryByRole("menuitem", { name: "Switch or create a workspace" })).toBeNull();
  });
});
