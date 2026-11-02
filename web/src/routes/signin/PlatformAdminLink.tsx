import { Link, useInRouterContext } from "react-router-dom";
import type { ApiClient } from "../../api/client";
import { usePlatformAdmin } from "../../components/shell/usePlatformAdmin";

/**
 * "Pannello admin" on the workspace picker: shown only to a Raffa platform admin, so they can reach the
 * Super Admin functions before (or without) entering any workspace. The portal re-checks with the server.
 */
export default function PlatformAdminLink({ apiClient }: { apiClient: ApiClient }) {
  const isPlatformAdmin = usePlatformAdmin(apiClient);
  const inRouter = useInRouterContext();
  if (!isPlatformAdmin) return null;
  const className = "btn btn-secondary workspace-admin-link";
  // Inside the app's router the link navigates client-side (no reload, so MSAL keeps its state); a plain
  // anchor is only the fallback for a render outside any router.
  return inRouter ? (
    <Link to="/admin" className={className}>
      Pannello admin
    </Link>
  ) : (
    <a href="/admin" className={className}>
      Pannello admin
    </a>
  );
}
