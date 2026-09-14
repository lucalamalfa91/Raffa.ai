import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { PublicClientApplication } from "@azure/msal-browser";
import { MsalProvider } from "@azure/msal-react";
import App from "./App";
import { AppConfigError, loadAppConfig } from "./config/appConfig";
import { acquireApiAccessToken, buildMsalConfig, handleRedirectPromiseOptions } from "./auth/msalConfig";
import { createApiClient } from "./api/client";
import "./index.css";

const rootElement = document.getElementById("root");
if (!rootElement) {
  throw new Error("#root element not found in index.html");
}
const root = createRoot(rootElement);

// Config must resolve before MSAL can be constructed (ADR-012: client id,
// authority and redirect URI are runtime config, not source).
//
// Task E14/F03/US02/T01 (wave w14; ADR-025 Rule C10a): this bootstrap now calls
// `instance.initialize()` + `instance.handleRedirectPromise(handleRedirectPromiseOptions)` itself,
// once, before `<MsalProvider>` ever mounts -- see `auth/msalConfig.ts`'s own header comment on
// `handleRedirectPromiseOptions` for why (the installed `@azure/msal-browser` major version has no
// config-level equivalent of this flag any more, and `MsalProvider` calls `handleRedirectPromise()`
// bare, with no options, and no prop to inject any). `MsalProvider`'s own later `initialize()` /
// `handleRedirectPromise()` calls are not a second, conflicting round-trip: `initialize()` is
// idempotent, and MSAL caches the redirect-promise result internally, so that second bare call
// resolves to the same, already-computed outcome instead of re-running with the library default.
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
    console.error("Raffa.ai web client failed to start:", error);
    root.render(
      <StrictMode>
        <div role="alert" className="startup-error">
          <h1>Raffa.ai could not start</h1>
          <p>{message}</p>
        </div>
      </StrictMode>,
    );
    return;
  }

  const msalInstance = new PublicClientApplication(buildMsalConfig(appConfig));
  await msalInstance.initialize();
  // Errors here surface through the LOGIN_FAILURE event MsalProvider/useMsal consumers already
  // listen for (the same posture MsalProvider's own internal call takes, per its source) -- this
  // bootstrap only needs the *options* to be applied, not the redirect result itself.
  await msalInstance.handleRedirectPromise(handleRedirectPromiseOptions).catch(() => undefined);

  // Task E01/F07/US01/T02: the generated-type-backed API client (src/api/client.ts),
  // built from the same runtime config as MSAL (ADR-012 "config, not code") --
  // never a hard-coded origin.
  //
  // Task E18/F01/US02/T01 (wave w15, NW-05; ADR-012 w15 footer clause 1, ADR-010 w14 footer clause
  // 3): the access-token accessor is resolved lazily, at request time, off `msalInstance` directly
  // via `acquireApiAccessToken` (auth/msalConfig.ts) rather than the `useMsal()` hook -- this
  // bootstrap runs before `<MsalProvider>` even mounts, so no React account state exists yet at the
  // point `createApiClient` is called, only the instance itself. Replaces the synchronous
  // per-caller-username closure task E13/F09/US01/T04 (OQ-askv2-005/ADR-022) left here -- deleted
  // whole by this task, not made conditional; see `acquireApiAccessToken`'s own doc comment for the
  // account-fallback and never-throws rules that used to live in this comment.
  const apiClient = createApiClient(appConfig.apiBaseUrl, () => acquireApiAccessToken(msalInstance, appConfig));

  root.render(
    <StrictMode>
      <MsalProvider instance={msalInstance}>
        <App appConfig={appConfig} apiClient={apiClient} />
      </MsalProvider>
    </StrictMode>,
  );
}

void bootstrap();
