/**
 * Desktop mode: when a controller is turned on, a note offers TV mode; its Menu button (Start), which the desktop
 * leaves unbound, or a click opens it. A stays the desktop's. Not Start when the player gave it to something else.
 * TV mode's Settings → "Offer TV mode when a controller connects" turns it off.
 */
import { useEffect, useRef, useState } from "react";
import { Gamepad2, X } from "lucide-react";
import { listen } from "@tauri-apps/api/event";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { native } from "../native";
import { useLauncher } from "../store";
import { DEFAULT_PAD, label, withDefaults } from "../spatial";
import { usePadLabels } from "../pad-hints";

const SHOWN_FOR = 10_000;

export function ControllerToast({ enabled, onOpen }: { enabled: boolean; onOpen: () => void }) {
  const [shown, setShown] = useState<string | null>(null);
  const known = useRef<number | null>(null);
  const { settings } = useLauncher();
  const labels = usePadLabels();
  const startFree = !Object.values(withDefaults(settings.pad, DEFAULT_PAD)).some((bound) => bound.includes("Start"));

  useEffect(() => {
    if (!enabled) return;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const update = (names: string[]) => {
      // The first list is what was already connected; only a new pad after that is news.
      if (known.current !== null && names.length > known.current) {
        setShown(names.at(-1) ?? t({ message: "Controller", comment: "A controller that gives no name of its own, as in “Controller connected”" }));
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

  // Start on the pad that was just turned on.
  useEffect(() => {
    if (!shown || !startFree) return;
    const pad = listen<{ key: string; down: boolean }>("pad", ({ payload }) => {
      if (payload.key === "Start" && payload.down) {
        setShown(null);
        onOpen();
      }
    });
    return () => void pad.then((stop) => stop());
  }, [shown, onOpen, startFree]);

  if (!enabled || !shown) return null;
  const name = shown;
  const button = label("pad", "Start", labels ?? { kind: "xbox", south: "A", east: "B", west: "X", north: "Y" });
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
        <strong>
          <Trans comment="{name} is a controller's name">{name} connected</Trans>
        </strong>
        <span>
          {startFree ? (
            <Trans comment="{button} is the controller's Menu button, as it's labelled (Menu, Options, +)">
              Press <b className="tv-toast-a">{button}</b> for TV mode
            </Trans>
          ) : (
            <Trans>Open TV mode</Trans>
          )}
        </span>
      </button>
      <button type="button" className="tv-toast-close" aria-label={t`Dismiss`} onClick={() => setShown(null)}>
        <X aria-hidden="true" />
      </button>
    </div>
  );
}
