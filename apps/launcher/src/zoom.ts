/**
 * The interface size zooms the page (html's zoom). Style, scrolling and an element's own sizes (offsetWidth,
 * clientHeight) are in the page's pixels; the window's size and the pointer (innerWidth, clientX) in the screen's, the
 * page's times the zoom. Where getBoundingClientRect puts a box depends on the engine: on the screen in Chromium
 * (WebView2, Android), in the page's pixels in WebKit (WebKitGTK, macOS), where dividing it by the zoom again put a
 * controller's focus box and the panels a quarter of the way off at 115%.
 */
export const zoom = () => Number(getComputedStyle(document.documentElement).zoom) || 1;

/** The interface sizes to pick from (Settings), the page's zoom at each. */
export const INTERFACE_SIZES = [0.9, 1, 1.25, 1.5, 1.75, 2];

/**
 * The fewest of the page's pixels across that the zoom leaves it, where the top bar still fits in every language
 * (German's the longest, at 1039). @media goes by the window, not the zoomed page, so it keeps the wide layout with
 * ever less room for it, and the bar runs off the screen.
 */
const NARROWEST_PAGE = 1060;

/** The size to pick nearest a saved one: Large (1.15) and Extra large (1.3) from before there were percentages. */
export const nearestInterfaceSize = (scale: number) => INTERFACE_SIZES.reduce((best, size) => (Math.abs(size - scale) < Math.abs(best - scale) ? size : best));

/**
 * The page's zoom: the interface size, and in TV mode a size larger again, read from across a room, and as much larger
 * as the window is past 1080p, which a 4K TV with no display scale of its own would otherwise show at half the size.
 * No further than the window has room for.
 */
export function pageZoom(scale: number, tv: boolean, width: number, height: number) {
  const wanted = tv ? scale * 1.15 * Math.max(1, Math.min(width / 1920, height / 1080)) : scale;
  return Math.min(wanted, Math.max(1, width / NARROWEST_PAGE));
}

let zoomedBoxes: boolean | undefined;
/** How many of a box's pixels (getBoundingClientRect's) make one of the page's: the zoom in Chromium, 1 in WebKit. */
export function boxZoom() {
  const z = zoom();
  if (z === 1) return 1;
  // The engine's way, seen once the page is zoomed: html's box against its own width.
  const html = document.documentElement;
  if (zoomedBoxes === undefined && html.offsetWidth) {
    const ratio = html.getBoundingClientRect().width / html.offsetWidth;
    zoomedBoxes = Math.abs(ratio - z) < Math.abs(ratio - 1);
  }
  return zoomedBoxes === false ? 1 : z;
}

/** The window's size in a box's pixels. */
export function windowBox() {
  const s = boxZoom() / zoom();
  return { width: window.innerWidth * s, height: window.innerHeight * s };
}
