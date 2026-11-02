import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import type { ApiClient } from "../../api/client";
import PlatformAdminLink from "./PlatformAdminLink";

const client = (status: boolean): ApiClient => ({ getPlatformAdminStatus: vi.fn().mockResolvedValue(status) }) as unknown as ApiClient;

describe("PlatformAdminLink", () => {
  it("links to the admin portal for a platform admin", async () => {
    render(<PlatformAdminLink apiClient={client(true)} />);
    expect(await screen.findByRole("link", { name: "Admin portal" })).toHaveAttribute("href", "/admin");
  });

  it("shows nothing to anyone else, nor when the check is unavailable", async () => {
    const { container, rerender } = render(<PlatformAdminLink apiClient={client(false)} />);
    await Promise.resolve();
    expect(container).toBeEmptyDOMElement();
    rerender(<PlatformAdminLink apiClient={{} as ApiClient} />);
    expect(container).toBeEmptyDOMElement();
  });
});
