/**
 * TV mode's sounds as the controller moves and chooses, as a console's menus have them: short soft tones made here
 * (Web Audio), so there are no sound files. Off with Settings → Sounds.
 */
import type { InputAction } from "./input/actions";

type Sound = "move" | "choose" | "back" | "tab";

const SOUND_OF: Partial<Record<InputAction, Sound>> = {
  up: "move",
  down: "move",
  left: "move",
  right: "move",
  accept: "choose",
  secondary: "choose",
  details: "choose",
  menu: "choose",
  search: "choose",
  back: "back",
  tabPrev: "tab",
  tabNext: "tab",
  pagePrev: "tab",
  pageNext: "tab",
};

/** Each sound: its wave, the pitch it starts and ends on (Hz), how long it lasts (s) and how loud it is. */
const TONES: Record<Sound, { wave: OscillatorType; from: number; to: number; length: number; volume: number }> = {
  move: { wave: "sine", from: 1500, to: 1150, length: 0.04, volume: 0.05 },
  choose: { wave: "triangle", from: 660, to: 1000, length: 0.09, volume: 0.08 },
  back: { wave: "triangle", from: 560, to: 340, length: 0.09, volume: 0.07 },
  tab: { wave: "sine", from: 880, to: 1480, length: 0.07, volume: 0.06 },
};

let context: AudioContext | null = null;

/** The sound for a press; a held direction's repeats are quieter. */
export function playFor(action: InputAction, repeat: boolean) {
  const sound = SOUND_OF[action];
  if (!sound || typeof AudioContext === "undefined") return;
  try {
    context ??= new AudioContext();
    if (context.state === "suspended") void context.resume();
    const { wave, from, to, length, volume } = TONES[sound];
    const now = context.currentTime;
    const tone = context.createOscillator();
    const level = context.createGain();
    tone.type = wave;
    tone.frequency.setValueAtTime(from, now);
    tone.frequency.exponentialRampToValueAtTime(to, now + length);
    level.gain.setValueAtTime(0.0001, now);
    level.gain.exponentialRampToValueAtTime(volume * (repeat ? 0.6 : 1), now + 0.005);
    level.gain.exponentialRampToValueAtTime(0.0001, now + length);
    tone.connect(level).connect(context.destination);
    tone.start(now);
    tone.stop(now + length + 0.02);
  } catch {
    // No sound where there's no audio device: the press still goes through.
  }
}
