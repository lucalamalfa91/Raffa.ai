import { useEffect, useMemo, useRef, useState } from "react";
import type { ApiClient } from "../../api/client";
import type { AskTurnView } from "./askViewModel";
import type { ReplyCitation } from "./reply/replyTypes";

function previewKey(citation: Pick<ReplyCitation, "documentId" | "page">): string | null {
  const documentId = citation.documentId?.trim() ?? "";
  if (documentId === "") return null;
  return `${documentId}:${citation.page ?? 1}`;
}

/**
 * Loads authenticated page-preview object URLs for tenant citations that name a document.
 * Raw `/api/documents/{id}/preview` paths cannot be used as `<img src>` (no tenant/auth headers).
 */
export function useCitationPreviews(
  apiClient: ApiClient,
  tenantId: string | undefined,
  turns: readonly AskTurnView[],
): ReadonlyMap<string, string> {
  const [previews, setPreviews] = useState<ReadonlyMap<string, string>>(() => new Map());
  const loadedKeys = useRef(new Set<string>());
  const objectUrls = useRef<string[]>([]);

  const needed = useMemo(() => {
    const keys: { key: string; documentId: string; page: number | undefined }[] = [];
    const seen = new Set<string>();
    for (const turn of turns) {
      if (turn.role !== "raffa" || turn.reply.kind !== "answer") continue;
      for (const citation of turn.reply.citations) {
        const key = previewKey(citation);
        if (key === null || seen.has(key)) continue;
        seen.add(key);
        keys.push({
          key,
          documentId: citation.documentId!,
          page: citation.page ?? undefined,
        });
      }
    }
    return keys;
  }, [turns]);

  useEffect(() => {
    if (!tenantId) return;

    for (const item of needed) {
      if (loadedKeys.current.has(item.key)) continue;
      loadedKeys.current.add(item.key);
      void apiClient.getDocumentPreviewUrl(tenantId, item.documentId, item.page).then((result) => {
        if (!result.ok || result.objectUrl === null) return;
        objectUrls.current.push(result.objectUrl);
        setPreviews((current) => {
          const next = new Map(current);
          next.set(item.key, result.objectUrl!);
          return next;
        });
      });
    }
  }, [apiClient, tenantId, needed]);

  useEffect(() => {
    const urls = objectUrls.current;
    return () => {
      for (const url of urls) URL.revokeObjectURL(url);
    };
  }, []);

  return previews;
}

export function applyCitationPreviews<T extends { kind: string }>(
  reply: T,
  previews: ReadonlyMap<string, string>,
): T {
  if (reply.kind !== "answer" || !("citations" in reply)) return reply;
  const answer = reply as T & { citations: readonly ReplyCitation[] };
  return {
    ...answer,
    citations: answer.citations.map((citation) => {
      const key = previewKey(citation);
      const previewUrl = (key !== null ? previews.get(key) : undefined) ?? citation.previewUrl;
      return { ...citation, previewUrl };
    }),
  };
}
