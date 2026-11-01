import { createContext, useContext } from "react";
import type { FixtureScenario, FixtureViewer } from "./fixtureMode";

/** What the demo bar (and any screen that cares) can read and do in fixture mode. */
export interface FixtureSession {
  scenario: FixtureScenario;
  viewer: FixtureViewer;
  /** YYYY-MM-DD, the scenario clock. */
  now: string;
  tenantName: string;
  /** Switch scenario: reset the fixture tenant to it, then reload every screen's data. */
  setScenario: (scenario: FixtureScenario) => Promise<void>;
  /** "Vedi come": the next requests are made as this member; every screen reloads its data. */
  setViewer: (viewer: FixtureViewer) => void;
  /** "Reimposta": reset the current scenario. */
  reset: () => Promise<void>;
}

export const FixtureSessionContext = createContext<FixtureSession | null>(null);

/** The fixture session, or `null` outside fixture mode (the normal signed-in app). */
export function useFixtureSession(): FixtureSession | null {
  return useContext(FixtureSessionContext);
}
