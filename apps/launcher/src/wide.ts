import { useLayoutEffect, useState, type RefObject } from "react";

/** Whether `ref`'s element is at least `width` wide, kept current as it's resized; the window's width until it's measured. */
export function useWiderThan(ref: RefObject<HTMLElement | null>, width: number) {
  const [wider, setWider] = useState(() => window.innerWidth >= width);
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const measure = () => setWider(el.clientWidth >= width);
    measure();
    if (typeof ResizeObserver === "undefined") return;
    const sized = new ResizeObserver(measure);
    sized.observe(el);
    return () => sized.disconnect();
  }, [ref, width]);
  return wider;
}
