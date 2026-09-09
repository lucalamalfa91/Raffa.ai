import { describe, expect, it } from "vitest";
import {
  INVITE_ROLE_ORDER,
  INVITE_ROLE_SUMMARY,
  MEMBERS_TIP,
  formatWorkspaceLine,
  getMemberStatusTag,
  inviteEmailPlaceholder,
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

  it("rejects a different domain in the prototype's own words (screens-v2.md #10: email must match the workspace domain)", () => {
    expect(validateInviteEmail("buyer@gmail.com", "acme.example")).toBe("Use an @acme.example address.");
  });

  it("accepts a same-domain address", () => {
    expect(validateInviteEmail("buyer@acme.example", "acme.example")).toBeNull();
  });

  it("skips the domain check when no tenant domain is known", () => {
    expect(validateInviteEmail("buyer@gmail.com", null)).toBeNull();
  });
});

describe("roles (requirements D8 win over the prototype's summaries)", () => {
  it("offers Procurement first, then Workspace Admin", () => {
    expect(INVITE_ROLE_ORDER).toEqual(["Procurement", "Admin"]);
    expect(memberRoleLabel("Admin")).toBe("Workspace Admin");
    expect(memberRoleLabel("Procurement")).toBe("Procurement");
    expect(memberRoleLabel("Legal")).toBe("Legal");
  });

  it("puts uploading on the Procurement line and only delete/manage on the Admin line", () => {
    expect(INVITE_ROLE_SUMMARY.Procurement).toBe("Asks, uploads, reviews, triages renewals");
    expect(INVITE_ROLE_SUMMARY.Admin).toBe("Also deletes documents and manages members");
  });
});

describe("copy and small formatters", () => {
  it("quotes the prototype's tip verbatim", () => {
    expect(MEMBERS_TIP).toBe("Tip: invite the team once the first contract is validated — there is nothing for them to ask before that.");
  });

  it("formats the header line from the real workspace name and tenant id", () => {
    expect(formatWorkspaceLine("Acme Procurement", "1111")).toBe("Acme Procurement · tenant 1111");
  });

  it("builds the Work email placeholder from the tenant domain, neutral when unknown", () => {
    expect(inviteEmailPlaceholder("acme.example")).toBe("name@acme.example");
    expect(inviteEmailPlaceholder(null)).toBe("name@company.com");
  });

  it("tags Active rows neutral and Invited rows accent, text first", () => {
    expect(getMemberStatusTag("Active")).toEqual({ variant: "neutral", label: "Active" });
    expect(getMemberStatusTag("Invited")).toEqual({ variant: "accent", label: "Invited" });
  });
});
