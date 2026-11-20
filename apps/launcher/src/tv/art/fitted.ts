/**
 * Pictures far bigger than they're ever shown, made smaller once (src-tauri/src/tv/art.rs).
 * LEGO Island's logo is 18106 x 4509: WebKitGTK scaled it, drop shadow and all, from full size
 * each time it was drawn, and Details lagged.
 *
 * A picture is shown once its size is known: as it is, or the smaller copy when it's oversized.
 */
import { useEffect, useState } from "react";
import { tvNative } from "../tvNative";

/** Pixels above which a picture is made smaller (about a 4K screen's worth). */
export const OVERSIZED = 3840 * 2160;

export const isOversized = (width: number, height: number) => width * height > OVERSIZED;

/** What to show for each URL: itself, or a blob URL of the smaller copy; `ready` once known. */
const shown = new Map<string, Promise<string>>();
const ready = new Map<string, string>();

function fitted(url: string): Promise<string> {
  let result = shown.get(url);
  if (!result) {
    result = new Promise<string>((resolve) => {
      const probe = new Image();
      probe.onload = () => {
        if (!isOversized(probe.naturalWidth, probe.naturalHeight)) return resolve(url);
        tvNative
          .fitArt(url)
          .then((bytes) => resolve(URL.createObjectURL(new Blob([bytes], { type: "image/png" }))))
          .catch(() => resolve(url));
      };
      // A picture that won't load: the page's own <img> shows its error.
      probe.onerror = () => resolve(url);
      probe.src = url;
    });
    shown.set(url, result);
    void result.then((src) => ready.set(url, src));
  }
  return result;
}

/** The URL to show for `url`, or null while its size isn't known yet. */
export function useFitted(url: string | null | undefined): string | null {
  const [state, setState] = useState<{ url: string; src: string } | null>(null);
  useEffect(() => {
    if (!url) return;
    let live = true;
    void fitted(url).then((src) => live && setState({ url, src }));
    return () => void (live = false);
  }, [url]);
  if (!url) return null;
  return ready.get(url) ?? (state?.url === url ? state.src : null);
}
