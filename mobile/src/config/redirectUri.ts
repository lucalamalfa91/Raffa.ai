import appConfig from '../../app.json';

const DEFAULT_CALLBACK_PATH = 'callback';

/**
 * The native app scheme registered in app.json ("expo.scheme"). This is the
 * same scheme Entra ID's public-client registration must allow as a native
 * reply URL (ADR-010: "the redirect URI for the native client is the
 * platform's declared scheme (e.g. `raffa://callback`), registered on the
 * public client registration").
 *
 * app.json stays the single source of truth; this constant is read from it
 * (via tsconfig's resolveJsonModule) rather than duplicated as a literal.
 */
export const APP_SCHEME: string = appConfig.expo.scheme;

/**
 * Builds the native OIDC redirect URI for a given app scheme + callback
 * path. Pure and side-effect free so it is usable both by the eventual
 * OIDC PKCE flow (task E01/F08/US01/T02) and by tests, without pulling in
 * any auth-session/native module.
 */
export function buildNativeRedirectUri(scheme: string, path: string = DEFAULT_CALLBACK_PATH): string {
  if (!scheme) {
    throw new Error('buildNativeRedirectUri requires a non-empty app scheme (see app.json "expo.scheme").');
  }
  return `${scheme}://${path}`;
}

/**
 * The Raffa mobile app's OIDC native redirect URI (parent story us-01
 * AC-1: Expo + TypeScript scaffold with `raffa://callback` native
 * redirect).
 */
export function getNativeRedirectUri(): string {
  return buildNativeRedirectUri(APP_SCHEME);
}
