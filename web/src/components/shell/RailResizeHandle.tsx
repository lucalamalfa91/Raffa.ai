import { useEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from "react";
import { RAIL_WIDTH_MAX, RAIL_WIDTH_MIN, type RailWidth } from "./useRailWidth";

const KEY_STEP = 16;

/**
 * The rail's right edge, draggable (the WAI-ARIA window-splitter pattern): drag it to resize the
 * rail, or focus it and use the arrow keys (Home/End for the narrowest/widest); double-click puts
 * the default width back. Sits over the rail's own 2px border, so it adds nothing to the layout.
 */
export default function RailResizeHandle({ width, resize, commit, reset }: RailWidth) {
  const drag = useRef<{ startX: number; startWidth: number } | null>(null);
  const [dragging, setDragging] = useState(false);

  // While dragging, the whole page shows the resize cursor and text never gets selected.
  useEffect(() => {
    if (!dragging) return;
    document.body.classList.add("is-resizing-rail");
    return () => document.body.classList.remove("is-resizing-rail");
  }, [dragging]);

  const widthAt = (event: PointerEvent<HTMLDivElement>) =>
    drag.current === null ? width : drag.current.startWidth + event.clientX - drag.current.startX;

  const endDrag = (event: PointerEvent<HTMLDivElement>) => {
    if (drag.current === null) return;
    commit(widthAt(event));
    drag.current = null;
    setDragging(false);
    if (event.currentTarget.hasPointerCapture?.(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const next =
      event.key === "ArrowLeft"
        ? width - KEY_STEP
        : event.key === "ArrowRight"
          ? width + KEY_STEP
          : event.key === "Home"
            ? RAIL_WIDTH_MIN
            : event.key === "End"
              ? RAIL_WIDTH_MAX
              : null;
    if (next === null) return;
    event.preventDefault();
    commit(next);
  };

  return (
    <div
      role="separator"
      aria-orientation="vertical"
      aria-label="Resize the sidebar"
      aria-valuemin={RAIL_WIDTH_MIN}
      aria-valuemax={RAIL_WIDTH_MAX}
      aria-valuenow={width}
      tabIndex={0}
      title="Drag to resize · double-click to reset"
      className={`shell-rail-resizer${dragging ? " is-dragging" : ""}`}
      onPointerDown={(event) => {
        if (event.button !== 0) return;
        event.preventDefault();
        event.currentTarget.setPointerCapture?.(event.pointerId);
        drag.current = { startX: event.clientX, startWidth: width };
        setDragging(true);
      }}
      onPointerMove={(event) => {
        if (drag.current !== null) resize(widthAt(event));
      }}
      onPointerUp={endDrag}
      onPointerCancel={endDrag}
      onKeyDown={onKeyDown}
      onDoubleClick={reset}
    />
  );
}
