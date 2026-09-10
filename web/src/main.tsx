import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { PublicClientApplication } from "@azure/msal-browser";
import { MsalProvider } from "@azure/msal-react";
import App from "./App";
import { AppConfigError, loadAppConfig } from "./config/appConfig";
import { buildMsalConfig } from "./auth/msalConfig";
import { createApiClient } from "./api/client";
import "./index.css";

const rootElement = document.getElementById("root");
if (!rootElement) {
  throw new Error("#root element not found in index.html");
}
const root = createRoot(rootElement);

// Config must resolve before MSAL can be constructed (ADR-012: client id,
// authority and redirect URI are runtime config, not source). MsalProvider
// (@azure/msal-react) owns calling `instance.initialize()` and
// `instance.handleRedirectPromise()` itself once mounted -- this bootstrap
// only needs to hand it an already-configured, un-initialized
// PublicClientApplication.
async function bootstrap() {
  let appConfig;
  try {
    appConfig = await loadAppConfig();
  } catch (error) {
    const message =
      error instanceof AppConfigError
        ? error.message
        : "Unexpected startup error while loading runtime config. See console for details.";
    // eslint-disable-next-line no-console
    console.error("Raffa web client failed to start:", error);
    root.render(
      <StrictMode>
        <div role="alert" className="startup-error">
          <h1>Raffa could not start</h1>
          <p>{message}</p>
        </div>
      </StrictMode>,
    );
    return;
  }

  const msalInstance = new PublicClientApplication(buildMsalConfig(appConfig));
  // Task E01/F07/US01/T02: the generated-type-backed API client (src/api/client.ts),
  // built from the same runtime config as MSAL (ADR-012 "config, not code") --
  // never a hard-coded origin.
  //
  // Task E13/F09/US01/T04 (OQ-askv2-005/R-CONV-03/ADR-022): `getUserId` is resolved lazily, at
  // request time, off `msalInstance` directly (its own synchronous, non-React
  // `getActiveAccount()`/`getAllAccounts()` API) rather than the `useMsal()` hook -- this bootstrap
  // runs before `<MsalProvider>` even mounts, so no React account state exists yet at the point
  // `createApiClient` is called, only the instance itself. `getActiveAccount()` is null until
  // something calls `setActiveAccount`, which nothing in this app does today (single-account usage
  // throughout, `App.tsx`'s own `accounts[0]`), so this falls back to the first cached account --
  // the same account `App.tsx` itself already treats as "the" signed-in one.
  const apiClient = createApiClient(
    appConfig.apiBaseUrl,
    () => msalInstance.getActiveAccount()?.username ?? msalInstance.getAllAccounts()[0]?.username ?? null,
  );

  root.render(
    <StrictMode>
      <MsalProvider instance={msalInstance}>
        <App appConfig={appConfig} apiClient={apiClient} />
      </MsalProvider>
    </StrictMode>,
  );
}

void bootstrap();
