// @vitest-environment jsdom
import { afterEach, beforeEach, expect, test, vi } from "vitest";

const readMusic = vi.fn(async (_path: string) => new ArrayBuffer(8));
vi.mock("./native", () => ({ native: { readMusic: (path: string) => readMusic(path) } }));

/** An audio element that plays at once, as a page that's allowed sound does. */
class FakeAudio extends EventTarget {
  static last: FakeAudio | null = null;
  loop = false;
  volume = 1;
  paused = true;
  constructor(public src: string) {
    super();
    FakeAudio.last = this;
  }
  play = vi.fn(async () => void (this.paused = false));
  pause = vi.fn(() => void (this.paused = true));
}

let focused = true;
let visibility: DocumentVisibilityState = "visible";
const audio = () => FakeAudio.last!;
const settle = async (ms = 1000) => {
  await vi.advanceTimersByTimeAsync(ms);
};

beforeEach(() => {
  vi.useFakeTimers();
  vi.resetModules();
  FakeAudio.last = null;
  focused = true;
  visibility = "visible";
  vi.stubGlobal("Audio", FakeAudio);
  URL.createObjectURL = () => "blob:music";
  URL.revokeObjectURL = () => {};
  vi.spyOn(document, "hasFocus").mockImplementation(() => focused);
  Object.defineProperty(document, "visibilityState", { configurable: true, get: () => visibility });
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

test("the music loops at the player's volume while the launcher is in front, and stops when it's removed", async () => {
  const { setMusic } = await import("./music");
  setMusic({ music: "C:/Music/theme.mp3", musicVolume: 0.5 });
  await settle();
  expect(readMusic).toHaveBeenCalledWith("C:/Music/theme.mp3");
  expect(audio().loop).toBe(true);
  expect(audio().paused).toBe(false);
  expect(audio().volume).toBeCloseTo(0.5);
  const playing = audio();
  setMusic({});
  expect(playing.paused).toBe(true);
});

test("it fades out as a game starts, and back in once the player comes back from it", async () => {
  const { setMusic, musicForGame } = await import("./music");
  setMusic({ music: "theme.ogg" });
  await settle();
  musicForGame();
  await settle();
  expect(audio().volume).toBe(0);
  expect(audio().paused).toBe(true);
  // Focus coming back before the game took it isn't the player coming back.
  window.dispatchEvent(new Event("focus"));
  await settle();
  expect(audio().paused).toBe(true);
  focused = false;
  window.dispatchEvent(new Event("blur"));
  await settle();
  focused = true;
  window.dispatchEvent(new Event("focus"));
  await settle();
  expect(audio().paused).toBe(false);
  expect(audio().volume).toBeCloseTo(0.2);
});

test("a game that never takes the launcher's place lets the music back after a while", async () => {
  const { setMusic, musicForGame } = await import("./music");
  setMusic({ music: "theme.ogg" });
  await settle();
  musicForGame();
  await settle(19_000);
  expect(audio().paused).toBe(true);
  await settle(2_000);
  expect(audio().paused).toBe(false);
});

test("hidden, it stops at once, where a fade's timers would crawl", async () => {
  const { setMusic } = await import("./music");
  setMusic({ music: "theme.ogg" });
  await settle();
  visibility = "hidden";
  document.dispatchEvent(new Event("visibilitychange"));
  expect(audio().paused).toBe(true);
  expect(audio().volume).toBe(0);
});

test("a file that's gone says so", async () => {
  readMusic.mockRejectedValueOnce(new Error("os error 2"));
  const { setMusic, musicProblem } = await import("./music");
  setMusic({ music: "gone.mp3" });
  await settle();
  expect(musicProblem()).toMatch(/moved or deleted/);
  expect(FakeAudio.last).toBeNull();
});
