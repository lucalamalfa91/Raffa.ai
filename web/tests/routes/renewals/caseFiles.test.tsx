import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import type { ApiClient } from "../../../src/api/client";
import type { CaseCallResult, CaseEnvelope, CaseFile } from "../../../src/api/renewalCases";
import { I18nProvider } from "../../../src/i18n/I18nProvider";
import { CaseFiles } from "../../../src/routes/renewals/case/CaseFiles";

const ok = <T,>(body: T): CaseCallResult<T> => ({ ok: true, statusCode: 200, body, error: null });

const file = (over: Partial<CaseFile>): CaseFile => ({
  id: "f1", kind: "email", fileName: "Re_Splunk_renewal.eml", mimeType: "message/rfc822", sizeBytes: 2048,
  createdAt: "2026-11-20T09:30:00Z", uploadedBy: "Luca", draftVersion: null, vendor: null, replyIndex: 0,
  sourceFileId: null, documentId: null, ...over,
});

const files: CaseFile[] = [
  file({}),
  file({ id: "f2", kind: "attachment", fileName: "Splunk_OrderForm_draft.pdf", mimeType: "application/pdf", sourceFileId: "f1" }),
  file({ id: "f3", kind: "signed", fileName: "Splunk_signed.pdf", mimeType: "application/pdf", replyIndex: null, documentId: "d1" }),
];

function renderFiles(client: Partial<ApiClient>, canUse = true, onCase = vi.fn()) {
  render(
    <MemoryRouter initialEntries={["/renewals/case-1?lang=en"]}>
      <I18nProvider>
        <CaseFiles apiClient={client as ApiClient} tenantId="t1" caseId="case-1" version={4} canUse={canUse} onCase={onCase} />
      </I18nProvider>
    </MemoryRouter>,
  );
  return onCase;
}

describe("the case files", () => {
  it("lists the files, marks the signed contract filed in Documents and uses an attachment as the contract document", async () => {
    const next = { caseId: "case-1", version: 5 } as CaseEnvelope;
    const apply = vi.fn().mockResolvedValue(ok(next));
    const onCase = renderFiles({ listRenewalCaseFiles: vi.fn().mockResolvedValue(ok(files)), applyRenewalCaseFile: apply });

    expect(await screen.findByText("Case files · 3")).toBeTruthy();
    expect(screen.getByText("Splunk_OrderForm_draft.pdf")).toBeTruthy();
    expect(screen.getByText("Filed in Documents")).toBeTruthy();
    expect(screen.getAllByRole("button", { name: "Download" })).toHaveLength(3);

    await userEvent.click(screen.getByRole("button", { name: "Use as contract document" }));
    expect(apply).toHaveBeenCalledWith("t1", "case-1", "f2", { type: "addDoc", version: 4, lang: "en" });
    expect(onCase).toHaveBeenCalledWith(next, true);
  });

  it("offers no way to use a file to someone who cannot work on the case, and shows nothing without files", async () => {
    renderFiles({ listRenewalCaseFiles: vi.fn().mockResolvedValue(ok(files)) }, false);
    expect(await screen.findByText("Case files · 3")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Use as contract document" })).toBeNull();
  });
});
