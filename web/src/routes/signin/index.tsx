import { useMsal } from "@azure/msal-react";
import { InteractionStatus } from "@azure/msal-browser";
import type { AppConfig } from "../../config/appConfig";
import type { ApiClient, WorkspaceSummaryBody } from "../../api/client";
import { buildLoginRequest } from "../../auth/msalConfig";
import SignInScreen from "./SignInScreen";
import WorkspacePickerScreen, { type WorkspacePickerState } from "./WorkspacePickerScreen";
import "./signin.css";

export interface SignInRouteProps {
  appConfig: AppConfig;
  apiClient: ApiClient;
  /**
   * Task E14/F03/US02/T01 (wave w14): non-`null` exactly when `App.tsx` has an account but has not
   * (yet, or ever, on this attempt) resolved a definite workspace to enter -- the async counterpart
   * of the old, purely-synchronous `!account || !workspace` gate. `App.tsx` owns the one
   * `apiClient.listWorkspaces()` call this resolution is built on and the session hint it revalidates
   * against; this route only renders whatever that resolution decided.
   */
  picker: {
    state: WorkspacePickerState;
    onRetry: () => void;
    onEnter: (workspace: WorkspaceSummaryBody) => void;
  } | null;
}

/**
 * ADR-018 route `/signin`: Entra sign-in -> workspace picker (story
 * us-02-signin-resolves-from-server).
 *
 * No client-side router path is dedicated to this screen (`App.tsx`'s gate renders it directly,
 * independent of the actual URL) -- unchanged from every task before this one; the `BrowserRouter`
 * this wave hoists into `App.tsx` exists for `/invite/accept`'s public branch, not to give this
 * screen a route of its own.
 */
export default function SignInRoute({ appConfig, apiClient, picker }: SignInRouteProps) {
  const { instance, accounts, inProgress } = useMsal();
  const account = accounts[0];
  const interactionInFlight = inProgress !== InteractionStatus.None;

  const handleContinue = () => {
    void instance.loginRedirect(buildLoginRequest(appConfig));
  };

  const handleSignOut = () => {
    void instance.logoutRedirect();
  };

  if (!account || !picker) {
    return <SignInScreen onContinue={handleContinue} interactionInFlight={interactionInFlight} />;
  }

  return (
    <WorkspacePickerScreen
      apiClient={apiClient}
      accountLabel={account.username}
      onSignOut={handleSignOut}
      state={picker.state}
      onRetry={picker.onRetry}
      onEnter={picker.onEnter}
    />
  );
}
