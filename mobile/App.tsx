import { StatusBar } from 'expo-status-bar';
import { ActivityIndicator, Button, StyleSheet, Text, View } from 'react-native';
import { useOidcAuth } from './src/auth';

// Minimal shell proving the OIDC Authorization Code + PKCE flow end to end
// (parent story us-01 AC-1 native redirect, AC-2 PKCE against Entra ID, no
// client secret): "Sign in" opens the system browser at the Entra authority
// named in per-environment config (src/config/env.ts) and returns via the
// native `raffa://callback` redirect (src/config/redirectUri.ts); "Sign
// out" forgets the in-memory tokens. Screens for the actual product
// surfaces (workspace, portfolio, Contract 360, ...) land in later feature
// tasks, mirroring web/src/App.tsx's equivalent sign-in/out shell.
export default function App() {
  const { isAuthenticated, isLoading, error, signIn, signOut } = useOidcAuth();

  return (
    <View style={styles.container}>
      <Text style={styles.title}>Raffa</Text>
      {isAuthenticated ? (
        <>
          <Text>Signed in.</Text>
          <Button title="Sign out" onPress={signOut} disabled={isLoading} />
        </>
      ) : (
        <>
          <Text>Sign in with your organization account to continue.</Text>
          <Button title="Sign in" onPress={signIn} disabled={isLoading} />
        </>
      )}
      {isLoading ? <ActivityIndicator /> : null}
      {error ? <Text style={styles.error}>{error}</Text> : null}
      <StatusBar style="auto" />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: '#fff',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 12,
    padding: 16,
  },
  title: {
    fontSize: 20,
    fontWeight: '600',
  },
  error: {
    color: '#b00020',
    textAlign: 'center',
  },
});
