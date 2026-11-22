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
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { useLauncher } from "../../store";
import { native } from "../../native";
import { tvNative } from "../tvNative";
import { useInputHandler } from "../context";
import type { InputAction } from "../input/actions";
import { LAYER } from "../input/router";
import { Glyph } from "../views/Glyph";
import { romNeed } from "./romNeed";

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
    ...(need.file
      ? [{ action: "accept" as const, label: busy ? t({ message: "Copying…", comment: "The player's copy of a game (a ROM) is being copied into the game's folder" }) : t`Choose the file…`, run: choose }]
      : []),
    { action: need.file ? "secondary" : "accept", label: t`Open folder`, run: () => void native.openFolder(need.id).catch((e) => setError(String(e))) },
    ...(need.readme
      ? [{ action: "details" as const, label: t`Read the instructions`, run: () => void tvNative.openReadme(need.readme!).catch((e) => setError(String(e))) }]
      : []),
    { action: "back", label: t({ message: "Close", comment: "Closes a dialog" }), run: close },
  ];
  return { need, buttons, error, busy };
}

function Frame({ rom, glyphs }: { rom: NonNullable<ReturnType<typeof useRom>>; glyphs: boolean }) {
  const { need, buttons, error } = rom;
  const name = need.title;
  const file = need.file;
  const folder = need.into;
  const heading = t`${name} needs your own copy of the game`;
  return (
    <div className="tv-dialog-backdrop" role="dialog" aria-modal="true" aria-label={heading}>
      <section className="tv-dialog tv-share">
        <div className="tv-dialog-body">
          <h2>
            <Disc3 aria-hidden="true" /> {heading}
          </h2>
          {file ? (
            <>
              <p className="tv-dialog-lead">
                {folder ? (
                  <Trans comment="{folder} is a folder inside the game's, such as roms or data; a ROM is a copy of a console game's cartridge or disc">
                    It closed right away because it looks for <b className="tv-share-path">{file}</b> in its {folder} folder, and it isn't there. That's the
                    original game (a ROM), which no download includes: you need a copy you own.
                  </Trans>
                ) : (
                  <Trans comment="A ROM is a copy of a console game's cartridge or disc">
                    It closed right away because it looks for <b className="tv-share-path">{file}</b> in its folder, and it isn't there. That's the original
                    game (a ROM), which no download includes: you need a copy you own.
                  </Trans>
                )}
              </p>
              <p className="tv-dialog-lead">
                <Trans>Choose your copy, and Quiver puts it there under that name, then starts the game.</Trans>
              </p>
              {glyphs && (
                <p className="tv-dialog-lead small">
                  <Trans>The file picker opens on the desktop: use the mouse or keyboard there.</Trans>
                </p>
              )}
            </>
          ) : (
            <p className="tv-dialog-lead">
              <Trans comment="A ROM is a copy of a console game's cartridge or disc">
                It closed right away because it can't find the original game (a ROM), which no download includes: you need a copy you own.
              </Trans>{" "}
              {need.readme ? t`Its instructions say where to put it.` : t`Its page says where to put it.`}
            </p>
          )}
          {error && <p className="tv-error">{error}</p>}
          <div className="tv-dialog-buttons">
            {buttons.map((b) => (
              <button key={b.action} type="button" className={b.action === "accept" ? "tv-primary" : ""} onClick={b.run}>
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

/** In TV mode: on top, with the controller. Choosing plays through TV mode's own Play. */
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
