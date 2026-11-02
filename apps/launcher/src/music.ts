/**
 * Music behind the launcher, as Quiver Launcher 3 had: one sound file the player picks in Settings, looped at their
 * volume. It fades out while the launcher isn't in front (another window has it, it's minimised or hidden, a phone's
 * home screen) and as Play starts a game, without waiting for the game's window; and back in when the launcher is in
 * front again. By the page's own focus, not the window's, which Chromium's (CEF's) runtime on Linux never reports.
 */
import { t } from "@lingui/core/macro";
import { native } from "./native";

export const DEFAULT_MUSIC_VOLUME = 0.2;
const FADE_MS = 500;
/** A game started that never took the launcher's place (no window, or it failed): the music comes back after this. */
const HOLD_MS = 20_000;

let path: string | undefined;
let volume = DEFAULT_MUSIC_VOLUME;
let audio: HTMLAudioElement | null = null;
let url: string | null = null;
/** How far faded in, 0 to 1, times the volume. */
let level = 0;
let fading: ReturnType<typeof setInterval> | undefined;
/** Play started a game: quiet until the launcher has been left and come back to, or `HOLD_MS` pass. */
let held = false;
let left = false;
let holding: ReturnType<typeof setTimeout> | undefined;

/** What went wrong with the music file, for Settings to say; null when it plays, or there's none. */
let problem: string | null = null;
const watchers = new Set<() => void>();
const report = (next: string | null) => {
  problem = next;
  for (const watch of watchers) watch();
};
export const musicProblem = () => problem;
export function watchMusicProblem(watch: () => void) {
  watchers.add(watch);
  return () => void watchers.delete(watch);
}

/** The player's music and its volume (Settings); a new file loads, none stops it. */
export function setMusic({ music, musicVolume }: { music?: string; musicVolume?: number }) {
  volume = Math.min(1, Math.max(0, musicVolume ?? DEFAULT_MUSIC_VOLUME));
  if (audio) audio.volume = volume * level;
  if (music === path) return;
  path = music;
  stop();
  report(null);
  if (path) void load(path);
}

/** Play started a game: the music fades now, not once the game's window is up. */
export function musicForGame() {
  if (!audio) return;
  held = true;
  left = false;
  clearTimeout(holding);
  holding = setTimeout(() => ((held = false), follow()), HOLD_MS);
  follow();
}

async function load(from: string) {
  // Gone since it was picked, most likely: moved, renamed or deleted.
  const bytes = await native.readMusic(from).catch(() => (report(t`Quiver couldn't open that music file. It may have been moved or deleted.`), null));
  if (from !== path || !bytes) return;
  url = URL.createObjectURL(new Blob([bytes]));
  audio = new Audio(url);
  audio.loop = true;
  audio.volume = 0;
  level = 0;
  audio.addEventListener("error", () => report(cantPlay()));
  listen();
  follow();
}

function stop() {
  clearInterval(fading);
  audio?.pause();
  audio = null;
  if (url) URL.revokeObjectURL(url);
  url = null;
  level = 0;
}

const cantPlay = () => t`Quiver can't play that file. Try an MP3 or OGG.`;

const inFront = () => document.visibilityState === "visible" && document.hasFocus();

/** Faded in or out, as the launcher is in front or not; at once when it's hidden, where timers are slowed to a crawl. */
function follow() {
  if (!audio) return;
  const quiet = held || !inFront();
  if (quiet && document.visibilityState === "hidden") {
    clearInterval(fading);
    level = 0;
    audio.volume = 0;
    audio.pause();
    return;
  }
  fadeTo(quiet ? 0 : 1);
}

function fadeTo(target: number) {
  const playing = audio!;
  clearInterval(fading);
  if (target > 0 && playing.paused) playing.play().then(() => report(null), afterGesture);
  const from = level;
  const since = performance.now();
  fading = setInterval(() => {
    const t = Math.min(1, (performance.now() - since) / (FADE_MS * Math.abs(target - from) || 1));
    level = from + (target - from) * t;
    playing.volume = volume * level;
    if (t < 1) return;
    clearInterval(fading);
    if (target === 0) playing.pause();
  }, 30);
}

/**
 * Sound that starts by itself can need the player to have pressed something first: it starts with their first click
 * or key. Anything else is a file that won't play.
 */
function afterGesture(e: unknown) {
  if (!(e instanceof DOMException && e.name === "NotAllowedError")) return report(cantPlay());
  const again = () => {
    for (const type of ["pointerdown", "keydown"]) window.removeEventListener(type, again, true);
    follow();
  };
  for (const type of ["pointerdown", "keydown"]) window.addEventListener(type, again, true);
}

let listening = false;
function listen() {
  if (listening) return;
  listening = true;
  window.addEventListener("blur", () => ((left = true), follow()));
  window.addEventListener("focus", () => {
    // Back from the game it started: it's the launcher's music again.
    if (held && left) {
      held = false;
      clearTimeout(holding);
    }
    follow();
  });
  document.addEventListener("visibilitychange", () => (document.visibilityState === "hidden" && (left = true), follow()));
}
