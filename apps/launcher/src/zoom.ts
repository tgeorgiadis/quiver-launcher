/**
 * The interface size zooms the page (html's zoom). Style, scrolling and an element's own sizes (offsetWidth,
 * clientHeight) are in the page's pixels; the window's size and the pointer (innerWidth, clientX) in the screen's, the
 * page's times the zoom. Where getBoundingClientRect puts a box depends on the engine: on the screen in Chromium
 * (WebView2, Android), in the page's pixels in WebKit (WebKitGTK, macOS), where dividing it by the zoom again put a
 * controller's focus box and the panels a quarter of the way off at 115%.
 */
export const zoom = () => Number(getComputedStyle(document.documentElement).zoom) || 1;

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
