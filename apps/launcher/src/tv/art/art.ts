/**
 * Artwork (spec §10, translated for v4 in PLAN-v4 §7). First found wins:
 * the player's own pick in bigpicture.json, then upstream's own cover (already
 * in the app's header and hero), then the catalog's library art; tiles without
 * a 2:3 picture get a stand-in drawn from the app's icon. Pure.
 */
import type { TvApp } from "../bridge";
import type { TvAppState } from "../data/sidecar";

/** Apps with the player's own pictures (Details → Change artwork) over the catalog's. Unchanged apps stay the same objects. */
export function withArtOverrides(apps: readonly TvApp[], states: Record<string, TvAppState>): TvApp[] {
  return apps.map((app) => {
    const own = states[app.id]?.art;
    if (!own?.grid && !own?.hero && !own?.logo) return app;
    return {
      ...app,
      art: {
        ...app.art,
        ...(own.grid ? { capsule: own.grid } : {}),
        ...(own.hero ? { hero: own.hero } : {}),
        ...(own.logo ? { logo: own.logo } : {}),
      },
    };
  });
}

/**
 * The two colours an icon is mostly made of, for a stand-in tile's gradient: the
 * most common hue among its strong pixels, and the most common other one (or a
 * darker shade of the first). RGBA pixels, as a canvas gives them. Null for an
 * icon with no colour to speak of (transparent, black, white or grey).
 */
export function dominantColors(rgba: ArrayLike<number>): [string, string] | null {
  const buckets = new Map<number, { n: number; r: number; g: number; b: number }>();
  for (let i = 0; i + 3 < rgba.length; i += 4) {
    const [r, g, b, a] = [rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]];
    if (a < 128) continue;
    const max = Math.max(r, g, b);
    const min = Math.min(r, g, b);
    // Greys, near-black and near-white say nothing about the icon's colour.
    if (max - min < 40 || max < 40 || min > 225) continue;
    const hue = hueOf(r, g, b, max, min);
    const bucket = Math.round(hue / 30) % 12;
    const sum = buckets.get(bucket) ?? { n: 0, r: 0, g: 0, b: 0 };
    sum.n++;
    sum.r += r;
    sum.g += g;
    sum.b += b;
    buckets.set(bucket, sum);
  }
  const ranked = [...buckets.entries()].sort((a, b) => b[1].n - a[1].n);
  if (!ranked.length) return null;
  const css = ({ n, r, g, b }: { n: number; r: number; g: number; b: number }, k = 1) =>
    `rgb(${Math.round((r / n) * k)} ${Math.round((g / n) * k)} ${Math.round((b / n) * k)})`;
  const [first, second] = ranked;
  return [css(first[1], 0.75), second && second[1].n >= first[1].n / 6 ? css(second[1], 0.45) : css(first[1], 0.3)];
}

function hueOf(r: number, g: number, b: number, max: number, min: number) {
  const d = max - min;
  const h = max === r ? ((g - b) / d) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return (h * 60 + 360) % 360;
}
