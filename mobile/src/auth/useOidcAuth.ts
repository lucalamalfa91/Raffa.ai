/**
 * OIDC Authorization Code + PKCE sign-in/out, wired to `expo-auth-session`
 * (ADR-010, ADR-013). This is the native-browser-opening glue: it is
 * intentionally thin and not unit-tested the same way `App.tsx`'s use of
 * this hook is exercised via a mock (`tests/App.test.tsx`) rather than by
 * re-testing `expo-auth-session`'s own request/PKCE/token-exchange plumbing
 * — the same boundary `web/src/App.tsx` draws around `@azure/msal-react`.
 *
 * Deliberately out of scope for this task (tracked as a follow-up, same
 * spirit as web/README.md's "Known gap" section): persisting tokens in
 * `expo-secure-store` and silent refresh via `AuthSession.refreshAsync`.
 * Tokens here live only in React state, so signing out (or closing the app)
 * simply forgets them.
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import * as AuthSession from 'expo-auth-session';
import * as WebBrowser from 'expo-web-browser';
import { getMobileEnvConfig, type MobileEnvConfig } from '../config/env';
import { getNativeRedirectUri } from '../config/redirectUri';
import { buildAuthRequestConfig, buildTokenExchangeConfig } from './oidcConfig';

// Required once per app so a completed auth session can close its popup and
// resolve `promptAsync()`'s promise on the web target (Expo AuthSession
// docs: "In order to close the popup window on web, you need to invoke
// WebBrowser.maybeCompleteAuthSession()"). A no-op on iOS/Android.
WebBrowser.maybeCompleteAuthSession();

export interface OidcAuthState {
  /** True once a token exchange has completed successfully. */
  isAuthenticated: boolean;
  /** True while the PKCE request is still loading or a code exchange is in flight. */
  isLoading: boolean;
  /** The exchanged tokens, or `null` before sign-in / after sign-out. */
  tokens: AuthSession.TokenResponse | null;
  /** The most recent sign-in error, if any. */
  error: string | null;
  /** Opens the system browser at Entra ID's authorize endpoint (PKCE). */
  signIn: () => void;
  /** Forgets the in-memory tokens. Does not call Entra's end-session endpoint. */
  signOut: () => void;
}

/**
 * Drives the Authorization Code + PKCE flow against the Entra ID authority
 * named in per-environment config, redirecting to the native
 * `raffa://callback` scheme (parent story us-01 AC-1/AC-2).
 *
 * `env` defaults to `getMobileEnvConfig()` (real `EXPO_PUBLIC_*` build-time
 * config) but can be injected, matching this codebase's existing
 * dependency-injection convention (`getMobileEnvConfig(source = process.env)`).
 */
export function useOidcAuth(env: MobileEnvConfig = getMobileEnvConfig()): OidcAuthState {
  const redirectUri = getNativeRedirectUri();
  const discovery = AuthSession.useAutoDiscovery(env.oidcAuthority);
  const [request, response, promptAsync] = AuthSession.useAuthRequest(
    buildAuthRequestConfig(env, redirectUri),
    discovery
  );
  const [tokens, setTokens] = useState<AuthSession.TokenResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isExchanging, setIsExchanging] = useState(false);
  // `env` defaults to a fresh `getMobileEnvConfig()` object every render (a
  // new reference each time, even though its values never change), and
  // `response` is only replaced when `promptAsync` resolves again -- so an
  // authorization code, which the token endpoint accepts exactly once
  // (RFC 6749 §4.1.2), must not be re-exchanged just because some *other*
  // dependency identity churned and re-ran this effect. This ref makes the
  // exchange idempotent per `response` object regardless of why the effect
  // re-fired, on top of depending on `env.oidcClientId` (a stable primitive)
  // rather than `env` itself so it doesn't churn in the first place.
  const exchangedResponseRef = useRef<AuthSession.AuthSessionResult | null>(null);

  useEffect(() => {
    if (!response || !discovery || exchangedResponseRef.current === response) {
      return;
    }
    exchangedResponseRef.current = response;

    if (response.type === 'success') {
      if (!request?.codeVerifier) {
        setError('OIDC sign-in succeeded but no PKCE code verifier was available to exchange it.');
        return;
      }
      setIsExchanging(true);
      setError(null);
      AuthSession.exchangeCodeAsync(
        buildTokenExchangeConfig(env, redirectUri, response.params.code, request.codeVerifier),
        discovery
      )
        .then((tokenResponse) => setTokens(tokenResponse))
        .catch((cause) => setError(cause instanceof Error ? cause.message : String(cause)))
        .finally(() => setIsExchanging(false));
    } else if (response.type === 'error') {
      setError(response.error?.message ?? 'OIDC sign-in failed.');
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- env.oidcClientId
    // (a stable primitive), not env (a new object reference every render), is
    // the intentional dependency; see the comment on exchangedResponseRef above.
  }, [response, request, discovery, env.oidcClientId, redirectUri]);

  const signIn = useCallback(() => {
    if (!request) {
      return;
    }
    void promptAsync();
  }, [request, promptAsync]);

  const signOut = useCallback(() => {
    setTokens(null);
    setError(null);
    exchangedResponseRef.current = null;
  }, []);

  return {
    isAuthenticated: tokens !== null,
    isLoading: !request || isExchanging,
    tokens,
    error,
    signIn,
    signOut,
  };
}
