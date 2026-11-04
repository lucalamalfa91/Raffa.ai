import type { ReactNode } from "react";
import { CopyTip } from "./InfoTip";
import { SCREEN_GUIDES, type ScreenGuideKey } from "./infoTipCopy";

/**
 * A screen's `h2.screen-title` with its guide beside it: the "i" that says what the screen is for,
 * how to use it and where it leads (`SCREEN_GUIDES`). The guide is a sibling of the heading, not
 * inside it, so the heading's accessible name stays the screen's own name. It stays accent-inked
 * until opened once in this browser, so a newcomer sees where to start on every screen.
 */
export default function ScreenTitle({ guide, children }: { guide: ScreenGuideKey; children: ReactNode }) {
  return (
    <div className="screen-title-row">
      <h2 className="screen-title">{children}</h2>
      <CopyTip tip={SCREEN_GUIDES[guide]} guideKey={guide} />
    </div>
  );
}
