import { describe, expect, it } from "vitest";
import {
  memberRoleLabel,
  validateInviteEmail,
  workspaceDomainFromEmail,
} from "../../../../src/routes/workspace/members/memberViewModel";

describe("workspaceDomainFromEmail", () => {
  it("returns the lowercased domain", () => {
    expect(workspaceDomainFromEmail("Marta@Acme.Example")).toBe("acme.example");
  });

  it("returns null when there is no domain", () => {
    expect(workspaceDomainFromEmail("nodomain")).toBeNull();
    expect(workspaceDomainFromEmail("@acme.example")).toBeNull();
    expect(workspaceDomainFromEmail("marta@")).toBeNull();
  });
});

describe("validateInviteEmail", () => {
  it("requires a non-empty address", () => {
    expect(validateInviteEmail("  ", "acme.example")).toBe("An email is required.");
  });

  it("rejects a malformed address", () => {
    expect(validateInviteEmail("not-an-email", "acme.example")).toBe("Enter a valid email address.");
  });

  it("rejects a different domain when a tenant domain is known (screens.md #2 non-tenant domain)", () => {
    expect(validateInviteEmail("buyer@gmail.com", "acme.example")).toBe(
      "Invite addresses must use this workspace's domain (@acme.example).",
    );
  });

  it("accepts a same-domain address", () => {
    expect(validateInviteEmail("buyer@acme.example", "acme.example")).toBeNull();
  });

  it("skips the domain check when no tenant domain is known", () => {
    expect(validateInviteEmail("buyer@gmail.com", null)).toBeNull();
  });
});

describe("memberRoleLabel", () => {
  it("maps Admin to Workspace Admin", () => {
    expect(memberRoleLabel("Admin")).toBe("Workspace Admin");
  });
});
