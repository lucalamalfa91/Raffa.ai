import type { ApiClient } from "../../api/client";
import { usePlatformAdmin } from "../../components/shell/usePlatformAdmin";

/**
 * "Admin portal" on the workspace picker: shown only to a Raffa platform admin, so they can reach the
 * Super Admin functions before (or without) entering any workspace. A plain link, so the picker needs
 * no router; the portal re-checks with the server.
 */
export default function PlatformAdminLink({ apiClient }: { apiClient: ApiClient }) {
  const isPlatformAdmin = usePlatformAdmin(apiClient);
  if (!isPlatformAdmin) return null;
  return (
    <a href="/admin" className="btn btn-secondary workspace-admin-link">
      Admin portal
    </a>
  );
}
