import { useLocation, useNavigate } from "react-router-dom";
import { useI18n } from "../../i18n/I18nProvider";
import { useGuideState } from "./guide";
import { fallbackGuide } from "./guideFallback";

/**
 * Guida Raffa (prototype lines 310-318): dark 300px column with "Dove sei / Cosa fare ora / Perché /
 * Dopo", one recommended action and the footer note. The content is announced politely when it
 * changes (DESIGN-GAPS A4).
 */
export default function GuidePanel() {
  const { t } = useI18n();
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const { content, setOpen } = useGuideState();
  const guide = content ?? fallbackGuide(pathname, t, (to) => navigate(to));

  return (
    <aside className="v4-guide" data-screen-label="Guida Raffa" aria-label={t("Raffa guide")}>
      <div className="v4-guide-head">
        <span className="v4-mark" aria-hidden="true" />
        <span className="v4-guide-title">{t("Raffa guide")}</span>
        <button type="button" className="v4-guide-hide" onClick={() => setOpen(false)}>
          {t("Hide")}
        </button>
      </div>
      <div className="v4-guide-live" aria-live="polite">
        <div>
          <div className="v4-guide-label">{t("Where you are")}</div>
          <div className="v4-guide-where">{guide.where}</div>
        </div>
        <div>
          <div className="v4-guide-label">{t("What to do now")}</div>
          <div className="v4-guide-now">{guide.now}</div>
        </div>
        <div>
          <div className="v4-guide-label">{t("Why")}</div>
          <div className="v4-guide-text">{guide.why}</div>
        </div>
        <div>
          <div className="v4-guide-label">{t("Next⟦guide⟧")}</div>
          <div className="v4-guide-text">{guide.next}</div>
        </div>
        {guide.cta ? (
          <button type="button" className="btn btn-primary v4-guide-cta" onClick={guide.cta.onClick}>
            {guide.cta.label}
          </button>
        ) : null}
      </div>
      <div className="v4-guide-foot">
        {t("Everything stays saved. You can ask a question, open a source, or leave and resume without losing your place.")}
      </div>
    </aside>
  );
}
