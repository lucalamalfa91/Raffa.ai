import { useEffect, useState } from "react";
import type { AppConfig } from "./config/appConfig";
import type { ApiClient } from "./api/client";
import SignInRoute from "./routes/signin";

interface AppProps {
  appConfig: AppConfig;
  apiClient: ApiClient;
}

// Task E01/F07/US01/T02: mirrors the generated API client's HealthCheckResult
// (src/api/client.ts) but adds the "still in flight" phase a UI needs and the
// wrapper's own result did not: a health check that has not resolved yet is
// not the same thing as an unreachable API.
type HealthState =
  | { phase: "checking" }
  | { phase: "ok"; body: string }
  | { phase: "unreachable"; statusCode: number | null; body: string };

// Task E06/F03/US01/T01 (signin-workspace-picker): App used to own a minimal
// inline sign-in/sign-out shell directly (task E01/F07/US01/T02's proof that
// the OIDC Authorization Code + PKCE flow works end to end). That shell is
// now ./routes/signin (SignInRoute) -- the real `/signin` screen (ADR-018)
// with the workspace picker AC-1/AC-2 need -- so App's only remaining job is
// the composition root: run the /health proof-of-connectivity effect (still
// independent of sign-in state) and mount the sign-in route beneath it.
export default function App({ appConfig, apiClient }: AppProps) {
  // "wire /health" (task E01/F07/US01/T02) and the parent story's Definition
  // of Done ("curl on /health via the API client succeeds"): a static SPA has
  // no shell to literally run curl in, so this effect is the equivalent
  // proof-of-connectivity -- every load of the deployed bundle calls /health
  // through the generated-type-backed client and surfaces the result,
  // independent of sign-in state (a reachability probe, not an authenticated
  // call).
  const [health, setHealth] = useState<HealthState>({ phase: "checking" });
  useEffect(() => {
    let cancelled = false;
    setHealth({ phase: "checking" });
    void apiClient.getHealth().then((result) => {
      if (cancelled) return;
      setHealth(
        result.ok
          ? { phase: "ok", body: result.body }
          : { phase: "unreachable", statusCode: result.statusCode, body: result.body },
      );
    });
    return () => {
      cancelled = true;
    };
  }, [apiClient]);

  return (
    <main>
      <p data-testid="api-health-status">
        {health.phase === "checking" && "API: checking…"}
        {health.phase === "ok" && `API: reachable (${health.body})`}
        {health.phase === "unreachable" &&
          `API: unreachable (${health.statusCode ?? "network error"}: ${health.body})`}
      </p>
      <SignInRoute appConfig={appConfig} apiClient={apiClient} />
    </main>
  );
}
