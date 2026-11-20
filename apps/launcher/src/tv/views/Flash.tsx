/**
 * A name large over the page for a moment: the tab LB / RB or LT / RT went to, or the console in Discover, so the
 * change can't be missed from across a room. It goes by itself, with Reduce motion too (no animation then).
 */
import { useCallback, useEffect, useState } from "react";

const FLASH_MS = 900;

export type FlashState = { n: number; label: string } | null;

export function useFlash() {
  const [flash, setFlash] = useState<FlashState>(null);
  useEffect(() => {
    if (!flash) return;
    const timer = setTimeout(() => setFlash(null), FLASH_MS);
    return () => clearTimeout(timer);
  }, [flash]);
  const show = useCallback((label: string) => setFlash((f) => ({ n: (f?.n ?? 0) + 1, label })), []);
  return { flash, show };
}

export function Flash({ flash }: { flash: FlashState }) {
  return flash ? (
    <div key={flash.n} className="tv-flash" aria-live="polite">
      {flash.label}
    </div>
  ) : null;
}
