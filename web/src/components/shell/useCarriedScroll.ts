import { useEffect, useLayoutEffect, useRef } from "react";

/**
 * The main column is one scroll container shared by every screen, as in the prototype: moving to
 * another screen keeps the scroll offset. A screen that first renders a short loading state would
 * let the browser clamp that offset to 0, so the offset the user left is carried over the route
 * change and re-applied as the new screen's content grows (only until the user scrolls, clicks or the
 * carry window ends).
 */
export function useCarriedScroll(pathname: string) {
  const ref = useRef<HTMLElement | null>(null);
  const wanted = useRef(0);
  const carryUntil = useRef(0);

  useLayoutEffect(() => {
    carryUntil.current = performance.now() + 3000;
  }, [pathname]);

  useEffect(() => {
    const main = ref.current;
    if (!main) return undefined;
    const onScroll = () => {
      if (performance.now() < carryUntil.current && main.scrollTop < wanted.current) return; // a clamp, not the user
      wanted.current = main.scrollTop;
    };
    const stopCarry = () => {
      carryUntil.current = 0;
    };
    const reapply = () => {
      if (performance.now() >= carryUntil.current) return;
      if (main.scrollTop < wanted.current && main.scrollHeight - main.clientHeight > main.scrollTop) {
        main.scrollTop = Math.min(wanted.current, main.scrollHeight - main.clientHeight);
      }
    };
    const observer = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(reapply);
    const content = main.firstElementChild;
    if (observer && content) observer.observe(content);
    const mutations = typeof MutationObserver === "undefined" ? null : new MutationObserver(() => {
      if (observer && main.firstElementChild && main.firstElementChild !== content) observer.observe(main.firstElementChild);
      reapply();
    });
    mutations?.observe(main, { childList: true, subtree: true });
    main.addEventListener("scroll", onScroll, { passive: true });
    main.addEventListener("wheel", stopCarry, { passive: true });
    main.addEventListener("touchmove", stopCarry, { passive: true });
    main.addEventListener("keydown", stopCarry);
    main.addEventListener("pointerdown", stopCarry);
    return () => {
      observer?.disconnect();
      mutations?.disconnect();
      main.removeEventListener("scroll", onScroll);
      main.removeEventListener("wheel", stopCarry);
      main.removeEventListener("touchmove", stopCarry);
      main.removeEventListener("keydown", stopCarry);
      main.removeEventListener("pointerdown", stopCarry);
    };
  }, []);

  return ref;
}
