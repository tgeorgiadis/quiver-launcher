/**
 * "Game needs your own copy of the game": a game that closed at once because
 * its ROM isn't there, and said so in its output (src-tauri/src/tv/
 * romneed.rs). Most ports ask for the ROM themselves; some, like GoldenEye
 * 007's, only quit. When the output names the file and folder, the player
 * picks their copy and Quiver puts it there under that name, then plays.
 * In TV mode with the controller (and the file picker too in Steam's Game
 * Mode, file-picker.tsx); in desktop mode as an ordinary dialog.
 */
import { useEffect, useState, useSyncExternalStore } from "react";
import { Disc3 } from "lucide-react";
import { useLauncher } from "../../store";
import { native } from "../../native";
import { tvNative } from "../tvNative";
import { useInputHandler } from "../context";
import type { InputAction } from "../input/actions";
import { LAYER } from "../input/router";
import { Glyph } from "../views/Glyph";

export type RomNeed = {
  id: string;
  title: string;
  /** The file name the game looks for, when its output names one. */
  file: string | null;
  /** The folder it looks in, relative to `dir` ("" is beside its program). */
  into: string;
  /** The folder of the game's program. */
  dir: string;
  readme: string | null;
};

let current: RomNeed | null = null;
const listeners = new Set<() => void>();

/** The game waiting for its ROM, if any (programs/usePrograms.ts sets it when a game fails). */
export const romNeed = {
  get: () => current,
  set(need: RomNeed | null) {
    current = need;
    for (const listener of listeners) listener();
  },
  subscribe(listener: () => void) {
    listeners.add(listener);
    return () => void listeners.delete(listener);
  },
};

type Button = { action: InputAction; label: string; run: () => void };

function useRom(play: (id: string) => void) {
  const need = useSyncExternalStore(romNeed.subscribe, romNeed.get);
  const { dismiss } = useLauncher();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => (setBusy(false), setError(null)), [need]);
  if (!need) return null;
  const close = () => romNeed.set(null);
  const choose = () => {
    if (busy || !need.file) return;
    setBusy(true);
    setError(null);
    tvNative.placeRom(need.dir, need.into, need.file).then(
      (placed) => {
        setBusy(false);
        if (!placed) return;
        close();
        dismiss(need.id);
        play(need.id);
      },
      (e) => (setBusy(false), setError(String(e))),
    );
  };
  const buttons: Button[] = [
    ...(need.file ? [{ action: "accept" as const, label: busy ? "Copying…" : "Choose the file…", run: choose }] : []),
    { action: need.file ? "secondary" : "accept", label: "Open folder", run: () => void native.openFolder(need.id).catch((e) => setError(String(e))) },
    ...(need.readme ? [{ action: "details" as const, label: "Read the instructions", run: () => void tvNative.openReadme(need.readme!).catch((e) => setError(String(e))) }] : []),
    { action: "back", label: "Close", run: close },
  ];
  return { need, buttons, error, busy };
}

function Frame({ rom, glyphs }: { rom: NonNullable<ReturnType<typeof useRom>>; glyphs: boolean }) {
  const { need, buttons, error } = rom;
  const where = need.into ? `its ${need.into} folder` : "its folder";
  return (
    <div className="tv-dialog-backdrop" role="dialog" aria-modal="true" aria-label={`${need.title} needs your own copy of the game`}>
      <section className="tv-dialog tv-share">
        <div className="tv-dialog-body">
          <h2>
            <Disc3 aria-hidden="true" /> {need.title} needs your own copy of the game
          </h2>
          {need.file ? (
            <>
              <p className="tv-dialog-lead">
                It closed right away because it looks for <b className="tv-share-path">{need.file}</b> in {where}, and it isn't there. That's the original game (a
                ROM), which no download includes: you need a copy you own.
              </p>
              <p className="tv-dialog-lead">Choose your copy, and Big Quiver puts it there under that name, then starts the game.</p>
              {glyphs && <p className="tv-dialog-lead small">The file picker opens on the desktop: use the mouse or keyboard there.</p>}
            </>
          ) : (
            <p className="tv-dialog-lead">
              It closed right away because it can't find the original game (a ROM), which no download includes: you need a copy you own.{" "}
              {need.readme ? "Its instructions say where to put it." : "Its page says where to put it."}
            </p>
          )}
          {error && <p className="tv-error">{error}</p>}
          <div className="tv-dialog-buttons">
            {buttons.map((b) => (
              <button key={b.action} type="button" className={b.action === "accept" ? "primary" : ""} onClick={b.run}>
                {glyphs && <Glyph action={b.action} size={22} />}
                {b.label}
              </button>
            ))}
          </div>
        </div>
      </section>
    </div>
  );
}

/** In Big Picture: on top, with the controller. Choosing plays through Big Picture's own Play. */
export function RomDialogTv({ play }: { play: (id: string) => void }) {
  const rom = useRom(play);
  useInputHandler(
    {
      onAction: (action) => rom?.buttons.find((b) => b.action === action)?.run(),
      hints: () => rom?.buttons.map(({ action, label }) => ({ action, label })) ?? [],
    },
    LAYER.dialog,
    Boolean(rom),
  );
  if (!rom) return null;
  return <Frame rom={rom} glyphs />;
}

/** In desktop mode: an ordinary dialog. */
export function RomDialogDesktop() {
  const { play } = useLauncher();
  const rom = useRom((id) => void play(id));
  useEffect(() => {
    if (!rom) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && (e.stopPropagation(), romNeed.set(null));
    window.addEventListener("keydown", onKey, true);
    return () => window.removeEventListener("keydown", onKey, true);
  }, [rom]);
  if (!rom) return null;
  return (
    <div className="tv-share-desktop">
      <Frame rom={rom} glyphs={false} />
    </div>
  );
}
