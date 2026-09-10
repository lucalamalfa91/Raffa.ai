import { CodeChallengeMethod, ResponseType } from 'expo-auth-session';
import {
  STANDARD_OIDC_SCOPES,
  buildAuthRequestConfig,
  buildOidcScopes,
  buildTokenExchangeConfig,
} from '../../src/auth/oidcConfig';
import type { MobileEnvConfig } from '../../src/config/env';

const env: MobileEnvConfig = {
  apiBaseUrl: 'https://api.dev.raffa.example',
  oidcAuthority: 'https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000/v2.0',
  oidcClientId: '11111111-1111-1111-1111-111111111111',
  oidcApiScopes: [
    'api://11111111-1111-1111-1111-111111111111/Raffa.Read',
    'api://11111111-1111-1111-1111-111111111111/Raffa.Write',
  ],
};

const redirectUri = 'raffa://callback';

describe('buildOidcScopes', () => {
  it('prepends the standard OIDC scopes to the per-environment API scopes', () => {
    expect(buildOidcScopes(env)).toEqual([...STANDARD_OIDC_SCOPES, ...env.oidcApiScopes]);
  });

  it('requests openid, profile and offline_access (needed for an ID token + refresh token)', () => {
    expect(buildOidcScopes(env)).toEqual(
      expect.arrayContaining(['openid', 'profile', 'offline_access'])
    );
  });
});

describe('buildAuthRequestConfig', () => {
  it('wires clientId and the given redirectUri from runtime config (ADR-013 config-not-code)', () => {
    const config = buildAuthRequestConfig(env, redirectUri);
    expect(config.clientId).toBe(env.oidcClientId);
    expect(config.redirectUri).toBe(redirectUri);
  });

  it('requests exactly the standard + per-environment API scopes', () => {
    const config = buildAuthRequestConfig(env, redirectUri);
    expect(config.scopes).toEqual(buildOidcScopes(env));
  });

  it('uses the Authorization Code response type (ADR-010)', () => {
    expect(buildAuthRequestConfig(env, redirectUri).responseType).toBe(ResponseType.Code);
  });

  it('enables PKCE with the S256 challenge method, never "plain" (ADR-010)', () => {
    const config = buildAuthRequestConfig(env, redirectUri);
    expect(config.usePKCE).toBe(true);
    expect(config.codeChallengeMethod).toBe(CodeChallengeMethod.S256);
  });

  it('uses the native raffa:// redirect scheme when passed one (AC-1)', () => {
    expect(buildAuthRequestConfig(env, 'raffa://callback').redirectUri).toBe('raffa://callback');
  });

  it('never carries a client secret (public client / PKCE only, AC-2)', () => {
    const config = buildAuthRequestConfig(env, redirectUri);
    expect(config).not.toHaveProperty('clientSecret');
    // Belt-and-braces, mirroring web/tests/auth/msalConfig.test.ts: nothing in
    // the built config should contain the substring "secret" anywhere, so a
    // future field being added and accidentally populated is also caught.
    expect(JSON.stringify(config).toLowerCase()).not.toContain('secret');
  });
});

describe('buildTokenExchangeConfig', () => {
  const code = 'auth-code-123';
  const codeVerifier = 'a-pkce-code-verifier';

  it('wires clientId and the given redirectUri from runtime config', () => {
    const config = buildTokenExchangeConfig(env, redirectUri, code, codeVerifier);
    expect(config.clientId).toBe(env.oidcClientId);
    expect(config.redirectUri).toBe(redirectUri);
  });

  it('sends the authorization code', () => {
    expect(buildTokenExchangeConfig(env, redirectUri, code, codeVerifier).code).toBe(code);
  });

  it('sends the PKCE code_verifier as an extra param, proving possession instead of a secret', () => {
    const config = buildTokenExchangeConfig(env, redirectUri, code, codeVerifier);
    expect(config.extraParams).toEqual({ code_verifier: codeVerifier });
  });

  it('never carries a client secret (public client / PKCE only, AC-2)', () => {
    const config = buildTokenExchangeConfig(env, redirectUri, code, codeVerifier);
    expect(config).not.toHaveProperty('clientSecret');
    expect(JSON.stringify(config).toLowerCase()).not.toContain('secret');
  });
});
