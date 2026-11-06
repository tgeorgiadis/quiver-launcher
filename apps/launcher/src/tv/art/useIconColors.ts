/** An icon's two main colours (art.ts), worked out once per icon from a small copy of it. */
import { useEffect, useState } from "react";
import { dominantColors } from "./art";

const known = new Map<string, [string, string] | null>();
const pending = new Map<string, Promise<[string, string] | null>>();

function colorsOf(url: string): Promise<[string, string] | null> {
  if (known.has(url)) return Promise.resolve(known.get(url)!);
  let p = pending.get(url);
  if (!p) {
    p = new Promise<[string, string] | null>((resolve) => {
      const img = new Image();
      // Without CORS the canvas can't be read; the tile then keeps its plain gradient.
      img.crossOrigin = "anonymous";
      img.decoding = "async";
      img.onload = () => {
        try {
          const canvas = document.createElement("canvas");
          canvas.width = canvas.height = 24;
          const ctx = canvas.getContext("2d", { willReadFrequently: true })!;
          ctx.drawImage(img, 0, 0, 24, 24);
          resolve(dominantColors(ctx.getImageData(0, 0, 24, 24).data));
        } catch {
          resolve(null);
        }
      };
      img.onerror = () => resolve(null);
      img.src = url;
    }).then((colors) => {
      known.set(url, colors);
      pending.delete(url);
      return colors;
    });
    pending.set(url, p);
  }
  return p;
}

export function useIconColors(url: string | undefined) {
  const [colors, setColors] = useState(() => (url ? (known.get(url) ?? null) : null));
  useEffect(() => {
    if (!url) return setColors(null);
    let live = true;
    void colorsOf(url).then((c) => live && setColors(c));
    return () => void (live = false);
  }, [url]);
  return colors;
}
