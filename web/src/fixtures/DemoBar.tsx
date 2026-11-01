import { useI18n } from "../i18n/I18nProvider";
import { useFixtureSession } from "./FixtureSessionContext";
import { formatItalianDate } from "./fixtureMode";
import "./demoBar.css";

/**
 * The prototype's demo bar (AskRaffa V4.dc.html lines 28-36), fixture mode only — not part of the
 * product. `data-no-i18n`: its text stays Italian in both languages, exactly as the prototype, and
 * the parity suite hides it (`[data-no-i18n]{visibility:hidden}`).
 */
export default function DemoBar() {
  const session = useFixtureSession();
  const { lang, setLang } = useI18n();
  if (!session) return null;
  const on = (active: boolean) => (active ? "is-on" : undefined);

  return (
    <div className="demo-bar" data-no-i18n="1">
      <span className="demo-bar-kicker">Demo</span>
      <span className="demo-bar-clock">
        Orologio di scenario {formatItalianDate(session.now)} · {session.tenantName} · dati dimostrativi, risposte dei fornitori
        simulate
      </span>
      <span className="demo-bar-label">Vedi come</span>
      <div className="demo-bar-seg">
        <button type="button" className={on(session.viewer === "luca")} onClick={() => session.setViewer("luca")}>
          Luca · CPO
        </button>
        <button type="button" className={on(session.viewer === "marta")} onClick={() => session.setViewer("marta")}>
          Marta · Buyer
        </button>
      </div>
      <span className="demo-bar-label">Scenario</span>
      <div className="demo-bar-seg">
        <button type="button" className={on(session.scenario === "zero")} onClick={() => void session.setScenario("zero")}>
          Da zero
        </button>
        <button type="button" className={on(session.scenario === "full")} onClick={() => void session.setScenario("full")}>
          Con dati
        </button>
      </div>
      <div className="demo-bar-seg demo-bar-lang">
        <button type="button" className={on(lang !== "en")} onClick={() => setLang("it")}>
          IT
        </button>
        <button type="button" className={on(lang === "en")} onClick={() => setLang("en")}>
          EN
        </button>
      </div>
      <button type="button" className="demo-bar-reset" onClick={() => void session.reset()}>
        Reimposta
      </button>
    </div>
  );
}
