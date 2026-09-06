import { useMsal } from "@azure/msal-react";
import { InteractionStatus } from "@azure/msal-browser";
import type { AppConfig } from "../../config/appConfig";
import type { ApiClient } from "../../api/client";
import { buildLoginRequest } from "../../auth/msalConfig";
import SignInScreen from "./SignInScreen";
import WorkspacePickerScreen from "./WorkspacePickerScreen";
import "./signin.css";

export interface SignInRouteProps {
  appConfig: AppConfig;
  apiClient: ApiClient;
}

/**
 * ADR-018 route `/signin`: Entra sign-in -> workspace picker, with the
 * redirect state in between (story us-01-signin-workspace-picker, AC-1/AC-2).
 *
 * No client-side router is wired into this app yet (web/package.json has no
 * `react-router-dom`) -- introducing one is E06/F03/US02/T01's
 * ("navigation-shell") job, since it also owns the admin/procurement route
 * guards a real router enables (ADR-018 "roles are a permission gate, not an
 * IA fork"). This component is gated on MSAL auth state instead, the same
 * mechanism src/App.tsx used for its own pre-this-task inline sign-in/out
 * shell (see git history / task E01/F07/US01/T02) -- swapping that gate for
 * a `<Route path="/signin">` later does not change either screen underneath.
 */
export default function SignInRoute({ appConfig, apiClient }: SignInRouteProps) {
  const { instance, accounts, inProgress } = useMsal();
  const account = accounts[0];
  const interactionInFlight = inProgress !== InteractionStatus.None;

  const handleContinue = () => {
    void instance.loginRedirect(buildLoginRequest(appConfig));
  };

  const handleSignOut = () => {
    void instance.logoutRedirect();
  };

  if (!account) {
    return <SignInScreen onContinue={handleContinue} interactionInFlight={interactionInFlight} />;
  }

  return (
    <WorkspacePickerScreen
      apiClient={apiClient}
      accountKey={account.homeAccountId}
      accountLabel={account.username}
      onSignOut={handleSignOut}
    />
  );
}
