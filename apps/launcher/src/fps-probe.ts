import { invoke } from "@tauri-apps/api/core";

/**
 * For the CEF spike's measurements: with QUIVER_FPS_LOG set, the page's own frame rate goes to that file once a
 * second, the same way on WebKitGTK and Chromium and with or without gamescope's counter (Desktop Mode has none).
 * Each line: the time (ms since 1970), frames drawn in that second, the longest frame (ms) and frames over 25 ms.
 * Also "ready <time>" when the first picture on the page has been drawn, for start-up time.
 */
export function startFpsProbe() {
  void invoke<boolean>("fps_probe")
    .then((on) => {
      if (!on) return;
      let frames = 0;
      let longest = 0;
      let slow = 0;
      let last = performance.now();
      let ready = false;
      const frame = (now: number) => {
        const gap = now - last;
        last = now;
        frames++;
        longest = Math.max(longest, gap);
        if (gap > 25) slow++;
        if (!ready && [...document.images].some((img) => img.complete && img.naturalWidth > 0 && img.getBoundingClientRect().width > 40)) {
          ready = true;
          void invoke("fps_report", { line: `ready ${Math.round(performance.timeOrigin + now)}` });
        }
        requestAnimationFrame(frame);
      };
      requestAnimationFrame(frame);
      setInterval(() => {
        void invoke("fps_report", { line: `${Date.now()} ${frames} ${Math.round(longest)} ${slow}` });
        frames = 0;
        longest = 0;
        slow = 0;
      }, 1000);
    })
    .catch(() => {});
}
