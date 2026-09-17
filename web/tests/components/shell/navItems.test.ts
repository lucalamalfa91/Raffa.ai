import { describe, expect, it } from "vitest";
import {
  buildPrimaryNavItems,
  buildSecondaryNavItems,
  canManageMembers,
  getDocumentsBadge,
} from "../../../src/components/shell/navItems";

describe("navItems (V2 two-tier model, ADR-024 amendment; task E13/F09/US01/T01, gap G-IA-V2)", () => {
  describe("buildPrimaryNavItems", () => {
    it("lists Ask Raffa then Documents, in that order, with no Home item", () => {
      const items = buildPrimaryNavItems(null);
      expect(items.map((item) => item.label)).toEqual(["Ask Raffa", "Documents"]);
      expect(items.map((item) => item.label)).not.toContain("Home");
    });

    it("Ask Raffa always carries the constant ⌘K badge and the conversation slot flag", () => {
      const [ask] = buildPrimaryNavItems(null);
      expect(ask.badge).toEqual({ text: "⌘K", tone: "muted" });
      expect(ask.hasConversationSlot).toBe(true);
      expect(ask.path).toBe("/ask");
    });

    it("Documents renders whatever badge it is handed (RailNav computes it from tracked documents) and never has a conversation slot", () => {
      const [, documents] = buildPrimaryNavItems({ text: "3 to review", tone: "attention" });
      expect(documents.badge).toEqual({ text: "3 to review", tone: "attention" });
      expect(documents.hasConversationSlot).toBe(false);
      expect(documents.path).toBe("/documents");
    });
  });

  describe("getDocumentsBadge (`app.jsx`: needReview?needReview+' to review':(docs.length?docs.length+' docs':''))", () => {
    it("no tracked documents at all -> no badge (never a fabricated '0 docs')", () => {
      expect(getDocumentsBadge({ total: 0, needsReview: 0 })).toBeNull();
    });

    it("some documents, none needing review -> a muted 'N docs' count", () => {
      expect(getDocumentsBadge({ total: 4, needsReview: 0 })).toEqual({ text: "4 docs", tone: "muted" });
    });

    it("any document needing review -> an attention-toned 'N to review', even when others are also docs", () => {
      expect(getDocumentsBadge({ total: 4, needsReview: 1 })).toEqual({ text: "1 to review", tone: "attention" });
    });
  });

  describe("buildSecondaryNavItems (\"From your contracts\")", () => {
    it("lists Portfolio, Renewals, Savings, Quote check in that order, with no Review queue item", () => {
      const items = buildSecondaryNavItems({ kbReady: false, validatedContractCount: 0 });
      expect(items.map((item) => item.label)).toEqual(["Portfolio", "Renewals", "Savings", "Quote check"]);
      expect(items.map((item) => item.label)).not.toContain("Review queue");
    });

    it("Savings is a first-class rail destination at /savings, with no badge", () => {
      const items = buildSecondaryNavItems({ kbReady: false, validatedContractCount: 0 });
      const savings = items.find((item) => item.id === "savings");
      expect(savings).toEqual({ id: "savings", label: "Savings", path: "/savings", badge: null, greyed: false });
    });

    it("greys Portfolio/Renewals/Quote check (and gives Portfolio/Renewals no badge) while kbReady is false; Savings stays un-greyed", () => {
      const items = buildSecondaryNavItems({ kbReady: false, validatedContractCount: 0 });
      const [portfolio, renewals, savings, quoteCheck] = items;
      expect(portfolio.greyed).toBe(true);
      expect(renewals.greyed).toBe(true);
      expect(savings.greyed).toBe(false);
      expect(quoteCheck.greyed).toBe(true);
      expect(portfolio.badge).toBeNull();
      expect(renewals.badge).toBeNull();
      expect(savings.badge).toBeNull();
    });

    it("un-greys every item and badges Portfolio/Renewals with the validated count once kbReady", () => {
      const items = buildSecondaryNavItems({ kbReady: true, validatedContractCount: 7 });
      expect(items.every((item) => !item.greyed)).toBe(true);
      const [portfolio, renewals] = items;
      expect(portfolio.badge).toEqual({ text: "7", tone: "muted" });
      expect(renewals.badge).toEqual({ text: "7", tone: "muted" });
    });

    it("Quote check's badge is always the constant 'optional', never a count, ready or not", () => {
      const notReady = buildSecondaryNavItems({ kbReady: false, validatedContractCount: 0 });
      const ready = buildSecondaryNavItems({ kbReady: true, validatedContractCount: 5 });
      expect(notReady[3].badge).toEqual({ text: "optional", tone: "muted" });
      expect(ready[3].badge).toEqual({ text: "optional", tone: "muted" });
    });
  });

  describe("canManageMembers (role gate -- the task's required unit test)", () => {
    it("is true for admin", () => {
      expect(canManageMembers("admin")).toBe(true);
    });

    it("is false for procurement (roles are a permission gate, never an IA fork)", () => {
      expect(canManageMembers("procurement")).toBe(false);
    });
  });
});
