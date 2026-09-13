import { describe, expect, it } from "vitest";
import type { WorkspaceMemberBody } from "../../../../src/api/client";
import {
  INVITE_ROLE_ORDER,
  INVITE_ROLE_SUMMARY,
  MEMBERS_TIP,
  composeAcceptLink,
  formatExpiryDate,
  formatWorkspaceLine,
  getMemberStatusTag,
  inviteDomainWarning,
  inviteEmailPlaceholder,
  inviteLinkExpiryMeta,
  isLastActiveAdmin,
  memberRoleLabel,
  removeConsequence,
  requestAccessMailto,
  revokeConsequence,
  validateInviteEmail,
  workspaceDomainFromEmail,
} from "../../../../src/routes/workspace/members/memberViewModel";

function member(overrides: Partial<WorkspaceMemberBody> = {}): WorkspaceMemberBody {
  return {
    id: "member-1",
    email: "admin@acme.example",
    role: "Admin",
    status: "Active",
    ...overrides,
  };
}

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

describe("validateInviteEmail (format only -- the domain check moved to a warning)", () => {
  it("requires a non-empty address", () => {
    expect(validateInviteEmail("  ")).toBe("An email is required.");
  });

  it("rejects a malformed address", () => {
    expect(validateInviteEmail("not-an-email")).toBe("Enter a valid email address.");
  });

  it("accepts a cross-domain address -- the domain rule no longer blocks (AC-5, OQ-w14-005)", () => {
    expect(validateInviteEmail("buyer@gmail.com")).toBeNull();
  });

  it("accepts a same-domain address", () => {
    expect(validateInviteEmail("buyer@acme.example")).toBeNull();
  });
});

describe("inviteDomainWarning (AC-5: non-blocking, submit stays enabled)", () => {
  it("warns for a cross-domain address, naming the role that will get full access", () => {
    expect(inviteDomainWarning("buyer@gmail.com", "acme.example", "Procurement")).toBe(
      "buyer@gmail.com is outside acme.example. They will get full Procurement access to this workspace.",
    );
    expect(inviteDomainWarning("buyer@gmail.com", "acme.example", "Admin")).toBe(
      "buyer@gmail.com is outside acme.example. They will get full Workspace Admin access to this workspace.",
    );
  });

  it("is null for a same-domain address", () => {
    expect(inviteDomainWarning("buyer@acme.example", "acme.example", "Procurement")).toBeNull();
  });

  it("is null when no tenant domain is known", () => {
    expect(inviteDomainWarning("buyer@gmail.com", null, "Procurement")).toBeNull();
  });

  it("is null for an address with no parseable domain -- the blocking format check owns that case", () => {
    expect(inviteDomainWarning("not-an-email", "acme.example", "Procurement")).toBeNull();
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

  it("formats the header line from the tenant id alone when no workspace name is available", () => {
    expect(formatWorkspaceLine("1111")).toBe("tenant 1111");
  });

  it("formats the header line with the workspace name when one is available", () => {
    expect(formatWorkspaceLine("1111", "Acme Procurement")).toBe("Acme Procurement · tenant 1111");
  });

  it("builds the Work email placeholder from the tenant domain, neutral when unknown", () => {
    expect(inviteEmailPlaceholder("acme.example")).toBe("name@acme.example");
    expect(inviteEmailPlaceholder(null)).toBe("name@company.com");
  });
});

describe("getMemberStatusTag (ADR-019 w14 footer: Active / Invited / Expired)", () => {
  it("tags Active rows neutral and Invited rows accent, text first", () => {
    expect(getMemberStatusTag("Active")).toEqual({ variant: "neutral", label: "Active" });
    expect(getMemberStatusTag("Invited")).toEqual({ variant: "accent", label: "Invited" });
  });

  it("tags Expired rows with the existing 'needs your decision' outline treatment, never a new colour", () => {
    expect(getMemberStatusTag("Expired")).toEqual({ variant: "outline", label: "Expired" });
  });

  it("passes an unmodelled status through by its own name rather than mislabelling it Active", () => {
    expect(getMemberStatusTag("Suspended")).toEqual({ variant: "neutral", label: "Suspended" });
  });
});

describe("isLastActiveAdmin (ADR-025 Rule D.5a)", () => {
  it("is true for the sole Active Admin", () => {
    const admin = member();
    expect(isLastActiveAdmin([admin], admin)).toBe(true);
  });

  it("is false when another Active Admin exists", () => {
    const admin = member();
    const secondAdmin = member({ id: "member-2", email: "second-admin@acme.example" });
    expect(isLastActiveAdmin([admin, secondAdmin], admin)).toBe(false);
  });

  it("is false for a non-Admin row and for an Invited row", () => {
    const procurement = member({ id: "member-3", role: "Procurement" });
    const invitedAdmin = member({ id: "member-4", status: "Invited" });
    expect(isLastActiveAdmin([procurement], procurement)).toBe(false);
    expect(isLastActiveAdmin([invitedAdmin], invitedAdmin)).toBe(false);
  });
});

describe("revoke/remove consequence copy (ADR-020 w14 design footer, screen 10)", () => {
  it("revoke never claims a grant that never existed", () => {
    expect(revokeConsequence("buyer@acme.example")).toEqual({
      question: "Revoke the invitation for buyer@acme.example?",
      detail: "Their link stops working. They never had access to this workspace.",
    });
  });

  it("remove (someone else) says they lose access -- a different fact from revoke", () => {
    expect(removeConsequence("buyer@acme.example", false)).toEqual({
      question: "Remove buyer@acme.example?",
      detail: "They lose access immediately. To bring them back you will need to send a new invitation.",
    });
  });

  it("remove (self) uses the second-person variant", () => {
    expect(removeConsequence("me@acme.example", true)).toEqual({
      question: "Remove yourself from this workspace?",
      detail: "You will lose access immediately and will need a new invitation to return.",
    });
  });
});

describe("composeAcceptLink (task item 3: new URL(acceptUrl, window.location.origin))", () => {
  it("resolves a site-relative acceptUrl against the given origin", () => {
    expect(composeAcceptLink("/invite/accept#abc.def", "https://app.raffa.ai")).toBe("https://app.raffa.ai/invite/accept#abc.def");
  });

  it("leaves an already-absolute acceptUrl unchanged -- no client change needed when the transport wave lands", () => {
    expect(composeAcceptLink("https://app.raffa.ai/invite/accept#abc.def", "https://unused.example")).toBe(
      "https://app.raffa.ai/invite/accept#abc.def",
    );
  });
});

describe("expiry formatting", () => {
  it("formats a UTC date deterministically regardless of host timezone", () => {
    expect(formatExpiryDate("2026-09-20T00:00:00Z")).toBe("20/09/2026");
  });

  it("builds the false-outcome meta line verbatim (ADR-020's second w14 amendment footer)", () => {
    expect(inviteLinkExpiryMeta("2026-09-20T00:00:00Z")).toBe("It expires 20/09/2026, can be used once, and is not shown again.");
  });
});

describe("requestAccessMailto (AC-9, ADR-020 w14 footer D-58.8)", () => {
  it("builds a real mailto to every live Admin, subject-prefilled with the workspace name", () => {
    expect(requestAccessMailto(["admin@acme.example"], "Acme Procurement")).toBe(
      "mailto:admin@acme.example?subject=Workspace%20Admin%20access%20%E2%80%94%20Acme%20Procurement",
    );
  });

  it("joins multiple Admin addresses and degrades the subject when no workspace name is known", () => {
    expect(requestAccessMailto(["a@acme.example", "b@acme.example"])).toBe(
      "mailto:a@acme.example,b@acme.example?subject=Workspace%20Admin%20access",
    );
  });

  it("is null rather than a dead link when the roster has no live Admin yet", () => {
    expect(requestAccessMailto([])).toBeNull();
  });
});
