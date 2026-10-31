/**
 * Gives the app's window the keyboard focus on its virtual display, as a
 * window manager would. Xvfb has none, so X's focus follows the pointer, which
 * sits still in the middle of the screen: whether the app counts as in front
 * comes down to the order X reports the window appearing under it, and a
 * tooltip popping up there takes it away. Out of focus, WebKitGTK shows no
 * focus ring (no :focus or :focus-visible) and the app ignores a controller
 * (document.hasFocus(), "a game is running in front"). Focus given to the
 * window stays with it whatever the pointer is over.
 *
 * Only the few X11 requests needed, straight over Xvfb's socket (which asks
 * for no authorisation), so nothing more needs installing.
 */
import { connect } from "node:net";

/** Focuses the largest window showing on `display` (":99"), once one shows; false if none does in `timeout` ms. */
export async function focusWindow(display, timeout = 20000) {
  const x = await open(`/tmp/.X11-unix/X${display.replace(/^:/, "").split(".")[0]}`);
  try {
    for (const deadline = Date.now() + timeout; Date.now() < deadline; await new Promise((r) => setTimeout(r, 100))) {
      const window = await largestShowing(x);
      if (!window) continue;
      // SetInputFocus, back to following the pointer if the window goes (minimised); GetInputFocus to know it's done.
      x.send(42, 1, u32(window), u32(0));
      const reply = await x.request(43, 0);
      if (reply.readUInt32LE(8) === window) return true;
    }
    return false;
  } finally {
    x.close();
  }
}

/** The biggest window mapped at the top level that isn't a popup (a tooltip, a menu), by area. */
async function largestShowing(x) {
  const tree = await x.request(15, 0, u32(x.root));
  const children = Array.from({ length: tree.readUInt16LE(16) }, (_, i) => tree.readUInt32LE(32 + i * 4));
  let best = null;
  for (const child of children) {
    const attributes = await x.request(3, 0, u32(child)).catch(() => null);
    // map-state 2: viewable; override-redirect: a popup.
    if (!attributes || attributes[26] !== 2 || attributes[27]) continue;
    const geometry = await x.request(14, 0, u32(child)).catch(() => null);
    const area = geometry ? geometry.readUInt16LE(16) * geometry.readUInt16LE(18) : 0;
    if (area > (best?.area ?? 0)) best = { child, area };
  }
  return best?.child ?? null;
}

const u32 = (n) => {
  const b = Buffer.alloc(4);
  b.writeUInt32LE(n);
  return b;
};

/** A connection to the X server at `path`: its root window, and requests that resolve with their replies. */
function open(path) {
  return new Promise((resolve, reject) => {
    const socket = connect(path);
    let data = Buffer.alloc(0);
    let x = null;
    let sequence = 0;
    const waiting = new Map();
    socket.once("error", reject);
    socket.on("data", (chunk) => {
      data = Buffer.concat([data, chunk]);
      if (!x) {
        if (data.length < 8) return;
        const size = 8 + data.readUInt16LE(6) * 4;
        if (data.length < size) return;
        if (data[0] !== 1) return reject(new Error(`the X server at ${path} refused the connection`));
        // The first screen's root: after the vendor's name (padded to 4 bytes) and the pixmap formats (8 bytes each).
        const vendor = data.readUInt16LE(24);
        x = { root: data.readUInt32LE(40 + Math.ceil(vendor / 4) * 4 + data[29] * 8), send, request, close: () => socket.destroy() };
        data = data.subarray(size);
        socket.off("error", reject);
        socket.on("error", (error) => waiting.forEach(({ fail }) => fail(error)));
        resolve(x);
      }
      // Replies (1) and errors (0); there are no events, as none are asked for.
      while (data.length >= 32) {
        const size = data[0] === 1 ? 32 + data.readUInt32LE(4) * 4 : 32;
        if (data.length < size) break;
        const message = data.subarray(0, size);
        data = data.subarray(size);
        const pending = waiting.get(message.readUInt16LE(2));
        if (!pending) continue;
        waiting.delete(message.readUInt16LE(2));
        if (message[0] === 1) pending.done(message);
        else pending.fail(new Error(`X error ${message[1]}`));
      }
    });
    // Little-endian, protocol 11.0, no authorisation.
    socket.write(Buffer.from([0x6c, 0, 11, 0, 0, 0, 0, 0, 0, 0, 0, 0]));

    function send(opcode, detail, ...parts) {
      const body = Buffer.concat(parts);
      const head = Buffer.from([opcode, detail, 0, 0]);
      head.writeUInt16LE(1 + body.length / 4, 2);
      socket.write(Buffer.concat([head, body]));
      return (sequence = (sequence + 1) & 0xffff);
    }
    function request(opcode, detail, ...parts) {
      return new Promise((done, fail) => waiting.set(send(opcode, detail, ...parts), { done, fail }));
    }
  });
}
