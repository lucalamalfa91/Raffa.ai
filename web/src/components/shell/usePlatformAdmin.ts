import { useEffect, useState } from "react";
import type { ApiClient } from "../../api/client";

/**
 * Is the signed-in person a Raffa platform admin (the portal admin profile)? Asked once per mount;
 * any failure -- an older API, a network error, a client without the method -- reads as "no", so the
 * Admin entry simply does not appear. The server re-checks on every `/api/admin/*` call; this only
 * decides whether to show the link.
 */
export function usePlatformAdmin(apiClient: ApiClient): boolean {
  const [isPlatformAdmin, setIsPlatformAdmin] = useState(false);

  useEffect(() => {
    let alive = true;
    void (async () => {
      try {
        if (typeof apiClient.getPlatformAdminStatus !== "function") return;
        const result = await apiClient.getPlatformAdminStatus();
        if (alive) setIsPlatformAdmin(result === true);
      } catch {
        // Not an admin as far as the UI is concerned.
      }
    })();
    return () => {
      alive = false;
    };
  }, [apiClient]);

  return isPlatformAdmin;
}
