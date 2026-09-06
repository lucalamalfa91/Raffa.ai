import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import RequireRole from "../../../src/components/shell/RequireRole";

describe("RequireRole", () => {
  it("renders children when the role matches the allowed role", () => {
    render(
      <RequireRole role="admin" allow="admin">
        <p>Members table</p>
      </RequireRole>,
    );

    expect(screen.getByText("Members table")).toBeInTheDocument();
  });

  it("renders the ADR-018 request-access state instead of children when the role does not match", () => {
    render(
      <RequireRole role="procurement" allow="admin">
        <p>Members table</p>
      </RequireRole>,
    );

    expect(screen.queryByText("Members table")).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /you don.t manage this workspace/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /request access/i })).toBeInTheDocument();
  });
});
