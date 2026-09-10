import { getMobileEnvConfig } from '../../src/config/env';

const validSource = {
  EXPO_PUBLIC_API_BASE_URL: 'https://api.dev.raffa.example',
  EXPO_PUBLIC_OIDC_AUTHORITY: 'https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000/v2.0',
  EXPO_PUBLIC_OIDC_CLIENT_ID: 'dev-public-client-id',
  EXPO_PUBLIC_OIDC_API_SCOPES:
    'api://11111111-1111-1111-1111-111111111111/Raffa.Read,api://11111111-1111-1111-1111-111111111111/Raffa.Write',
};

describe('getMobileEnvConfig', () => {
  it('reads apiBaseUrl, oidcAuthority, oidcClientId and oidcApiScopes from the given env source', () => {
    expect(getMobileEnvConfig(validSource)).toEqual({
      apiBaseUrl: validSource.EXPO_PUBLIC_API_BASE_URL,
      oidcAuthority: validSource.EXPO_PUBLIC_OIDC_AUTHORITY,
      oidcClientId: validSource.EXPO_PUBLIC_OIDC_CLIENT_ID,
      oidcApiScopes: [
        'api://11111111-1111-1111-1111-111111111111/Raffa.Read',
        'api://11111111-1111-1111-1111-111111111111/Raffa.Write',
      ],
    });
  });

  it.each(Object.keys(validSource))(
    'fails fast when %s is missing, instead of silently pointing at the wrong environment',
    (missingKey) => {
      const partialSource = { ...validSource, [missingKey]: undefined };
      expect(() => getMobileEnvConfig(partialSource)).toThrow(new RegExp(missingKey));
    }
  );

  it('never falls back to a hardcoded default (no secrets/URLs baked into the bundle)', () => {
    expect(() => getMobileEnvConfig({})).toThrow(/Missing required environment variable/);
  });

  it('trims whitespace around each comma-separated scope', () => {
    const config = getMobileEnvConfig({
      ...validSource,
      EXPO_PUBLIC_OIDC_API_SCOPES: ' api://x/Raffa.Read , api://x/Raffa.Write ',
    });
    expect(config.oidcApiScopes).toEqual(['api://x/Raffa.Read', 'api://x/Raffa.Write']);
  });

  it('fails fast when the scopes variable is set but has no usable scope', () => {
    expect(() => getMobileEnvConfig({ ...validSource, EXPO_PUBLIC_OIDC_API_SCOPES: ' , ,' })).toThrow(
      /EXPO_PUBLIC_OIDC_API_SCOPES must contain at least one non-empty/
    );
  });
});
