import { describe, expect, it, vi } from "vitest";
import type { ApiClient, ConversationReplyBody } from "../../api/client";
import { buildBoundContractChip, createConversationAndAsk, fetchBoundContractChip } from "./askViewModel";

/**
 * Task E27/F04/US01/T01 (binding-chip; parent story us-01-binding-chip AC-1/AC-2/AC-3; closes
 * NW-78, ADR-012 cl. 49 / ADR-020 37.2 per `reports/architecture/waves/w19.md`; screens-v2.md #2
 * "scope line" as anchor). Pure-logic coverage only (no React here -- `index.tsx` owns the render
 * wiring and the effect wiring that feeds these functions their inputs): proves the chip's own
 * label/href format (AC-1); that it is rebuilt from the conversation's own persisted
 * `scopeContractId` wire field at both moments a conversation can become scoped -- create and
 * resume -- rather than any `?scope=` query these functions have no way to even read (AC-2,
 * "survives resume"); and that it resolves to `null` -- unrendered -- whenever the bound contract is
 * not actually resolvable (AC-3).
 */

const CONTRACT_ID = "11111111-1111-1111-1111-111111111111";
const SUPPLIER_ID = "22222222-2222-2222-2222-222222222222";

/** Same "cast rather than fill every generated field" convention `GlobalAskBar.test.tsx
 * #fakeApiClient` already establishes for this app's own wire-shaped mocks. */
function fakeApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getContract360: vi.fn().mockResolvedValue({ ok: false, statusCode: null, contract: null, error: "not scripted" }),
    createConversation: vi.fn().mockResolvedValue({ ok: false, statusCode: null, conversation: null, error: "not scripted" }),
    postMessage: vi.fn().mockResolvedValue({ ok: false, statusCode: null, reply: null, error: "not scripted" }),
    ...overrides,
  } as unknown as ApiClient;
}

describe("buildBoundContractChip (AC-1)", () => {
  it('renders "{supplierName} · {type}" linking /contracts/{id}', () => {
    expect(buildBoundContractChip(CONTRACT_ID, { supplierName: "Salesforce", supplierId: SUPPLIER_ID, type: "Msa" })).toEqual({
      href: `/contracts/${CONTRACT_ID}`,
      label: "Salesforce · MSA",
    });
  });

  it("reuses the real getContractTypeLabel mapping, not an invented one", () => {
    const chip = buildBoundContractChip(CONTRACT_ID, { supplierName: "Acme", supplierId: SUPPLIER_ID, type: "OrderForm" });
    expect(chip.label).toBe("Acme · Order Form");
  });

  it("falls back to the id-fragment supplier label when supplierName is blank -- never a guid (R-SUP-04)", () => {
    const chip = buildBoundContractChip(CONTRACT_ID, { supplierName: "   ", supplierId: SUPPLIER_ID, type: "Msa" });
    expect(chip.label).toBe(`Supplier ${SUPPLIER_ID.slice(0, 8)} · MSA`);
    expect(chip.label).not.toContain(SUPPLIER_ID);
  });

  it("falls back to the id-fragment supplier label when supplierName is null", () => {
    const chip = buildBoundContractChip(CONTRACT_ID, { supplierName: null, supplierId: SUPPLIER_ID, type: "Sow" });
    expect(chip.label).toBe(`Supplier ${SUPPLIER_ID.slice(0, 8)} · SOW`);
  });
});

describe("fetchBoundContractChip -- rebuilt from a fresh getContract360 read (AC-2), unresolvable -> null (AC-3)", () => {
  it("rebuilds the chip from getContract360, keyed by the given contractId", async () => {
    const apiClient = fakeApiClient({
      getContract360: vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        contract: { header: { supplierName: "Salesforce", supplierId: SUPPLIER_ID, type: "Msa" } },
        error: null,
      }),
    });

    const chip = await fetchBoundContractChip(apiClient, "w-1", CONTRACT_ID);

    expect(chip).toEqual({ href: `/contracts/${CONTRACT_ID}`, label: "Salesforce · MSA" });
    expect(apiClient.getContract360).toHaveBeenCalledWith("w-1", CONTRACT_ID);
  });

  it("AC-3: unrendered (null) on a transport failure", async () => {
    const apiClient = fakeApiClient();
    expect(await fetchBoundContractChip(apiClient, "w-1", CONTRACT_ID)).toBeNull();
  });

  it("AC-3: unrendered (null) on a 404 -- a deleted or cross-tenant contract", async () => {
    const apiClient = fakeApiClient({
      getContract360: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, contract: null, error: "not found" }),
    });
    expect(await fetchBoundContractChip(apiClient, "w-1", CONTRACT_ID)).toBeNull();
  });

  it("AC-3: unrendered (null) on an ok response carrying no contract, defensively", async () => {
    const apiClient = fakeApiClient({
      getContract360: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, contract: null, error: null }),
    });
    expect(await fetchBoundContractChip(apiClient, "w-1", CONTRACT_ID)).toBeNull();
  });

  it("AC-2 survives resume: rebuilds identically when the id comes from a resumed conversation detail's own scopeContractId -- this function has no `location`/query string in its signature to read a `?scope=` from even if a stale one were still in the URL", async () => {
    // Simulates useConversation's resume path: GET /api/conversations/{id} returns a
    // ConversationDetailBody; index.tsx passes its own persisted scopeContractId straight through.
    const resumedConversation = {
      id: "conv-3",
      title: "Renewal terms",
      scopeContractId: CONTRACT_ID as string | null,
      createdAt: "2026-09-10T00:00:00Z",
      updatedAt: "2026-09-17T00:00:00Z",
      messages: [] as unknown[],
    };
    const apiClient = fakeApiClient({
      getContract360: vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        contract: { header: { supplierName: "Salesforce", supplierId: SUPPLIER_ID, type: "Msa" } },
        error: null,
      }),
    });

    const chip =
      resumedConversation.scopeContractId !== null
        ? await fetchBoundContractChip(apiClient, "w-1", resumedConversation.scopeContractId)
        : null;

    expect(chip).toEqual({ href: `/contracts/${CONTRACT_ID}`, label: "Salesforce · MSA" });
  });
});

describe("createConversationAndAsk -- scopeContractId is echoed off the wire, not the caller's own argument (AC-2)", () => {
  it("returns the created conversation's own persisted scopeContractId", async () => {
    const reply = { kind: "answer" } as unknown as ConversationReplyBody;
    const apiClient = fakeApiClient({
      createConversation: vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 201,
        conversation: { id: "conv-1", title: "New chat", scopeContractId: CONTRACT_ID, updatedAt: "2026-09-17T00:00:00Z" },
        error: null,
      }),
      postMessage: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, reply, error: null }),
    });

    const result = await createConversationAndAsk(apiClient, "w-1", "When does it renew?", CONTRACT_ID);

    expect(result).toEqual({ ok: true, conversationId: "conv-1", reply, scopeContractId: CONTRACT_ID });
  });

  it("surfaces null for an unscoped new chat, when the created conversation carries no scopeContractId", async () => {
    const reply = { kind: "answer" } as unknown as ConversationReplyBody;
    const apiClient = fakeApiClient({
      createConversation: vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 201,
        conversation: { id: "conv-2", title: "New chat", scopeContractId: null, updatedAt: "2026-09-17T00:00:00Z" },
        error: null,
      }),
      postMessage: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, reply, error: null }),
    });

    const result = await createConversationAndAsk(apiClient, "w-1", "Hello", undefined);

    expect(result).toEqual({ ok: true, conversationId: "conv-2", reply, scopeContractId: null });
  });
});
