import { APP_SCHEME, buildNativeRedirectUri, getNativeRedirectUri } from '../../src/config/redirectUri';

describe('buildNativeRedirectUri', () => {
  it('joins scheme and path with "://"', () => {
    expect(buildNativeRedirectUri('raffa', 'callback')).toBe('raffa://callback');
  });

  it('defaults the path to "callback"', () => {
    expect(buildNativeRedirectUri('raffa')).toBe('raffa://callback');
  });

  it('throws on an empty scheme instead of returning a malformed URI', () => {
    expect(() => buildNativeRedirectUri('')).toThrow(/non-empty app scheme/);
  });
});

describe('getNativeRedirectUri', () => {
  it('reads the scheme from app.json ("expo.scheme")', () => {
    expect(APP_SCHEME).toBe('raffa');
  });

  it('produces "raffa://callback" (parent story us-01 AC-1)', () => {
    expect(getNativeRedirectUri()).toBe('raffa://callback');
  });
});
