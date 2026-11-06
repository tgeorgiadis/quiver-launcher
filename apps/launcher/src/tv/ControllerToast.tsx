/**
 * Desktop mode (spec §11): when a controller is turned on, a note offers Big
 * Picture; A (or a click) opens it. It reads upstream's own controller events,
 * which desktop mode already has. Settings → "Offer Big Picture when a
 * controller connects" turns it off.
 */
import { useEffect, useRef, useState } from "react";
import { Gamepad2, X } from "lucide-react";
import { listen } from "@tauri-apps/api/event";
import { native } from "../native";

const SHOWN_FOR = 10_000;

export function ControllerToast({ enabled, onOpen }: { enabled: boolean; onOpen: () => void }) {
  const [shown, setShown] = useState<string | null>(null);
  const known = useRef<number | null>(null);

  useEffect(() => {
    if (!enabled) return;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const update = (names: string[]) => {
      // The first list is what was already connected; only a new pad after that is news.
      if (known.current !== null && names.length > known.current) {
        setShown(names.at(-1) ?? "Controller");
        clearTimeout(timer);
        timer = setTimeout(() => setShown(null), SHOWN_FOR);
      }
      known.current = names.length;
    };
    void native.controllers().then(update, () => {});
    const off = native.onControllers(update);
    return () => {
      clearTimeout(timer);
      void off.then((stop) => stop());
    };
  }, [enabled]);

  // A on the pad that was just turned on.
  useEffect(() => {
    if (!shown) return;
    const pad = listen<{ key: string; down: boolean }>("pad", ({ payload }) => {
      if (payload.key === "South" && payload.down) {
        setShown(null);
        onOpen();
      }
    });
    return () => void pad.then((stop) => stop());
  }, [shown, onOpen]);

  if (!enabled || !shown) return null;
  return (
    <div className="tv-toast" role="status">
      <Gamepad2 aria-hidden="true" />
      <button
        type="button"
        className="tv-toast-main"
        onClick={() => {
          setShown(null);
          onOpen();
        }}
      >
        <strong>{shown} connected</strong>
        <span>
          Press <b className="tv-toast-a">A</b> for Big Picture
        </span>
      </button>
      <button type="button" className="tv-toast-close" aria-label="Dismiss" onClick={() => setShown(null)}>
        <X aria-hidden="true" />
      </button>
    </div>
  );
}
