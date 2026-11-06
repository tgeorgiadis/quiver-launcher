/**
 * Errors as they happen (spec S12): a game that wouldn't start, a download or
 * update that failed. The tile keeps its ⚠ badge; this says what went wrong.
 */
import { useEffect, useRef, useState } from "react";
import { AlertTriangle } from "lucide-react";
import { useTv } from "../context";
import type { TvState } from "../bridge";
import { romNeed } from "../programs/RomDialog";

const SHOWN_FOR = 7000;

type Notice = { key: number; title: string; text: string };

export function Notices() {
  const { bridge } = useTv();
  const [notices, setNotices] = useState<Notice[]>([]);
  const before = useRef<Map<string, TvState> | null>(null);
  const next = useRef(0);

  useEffect(() => {
    const was = before.current;
    const fresh: Notice[] = [];
    for (const app of bridge.apps) {
      // Errors already there when Big Picture opened aren't news.
      // A game that needs its ROM has its own dialog (programs/RomDialog.tsx).
      if (was && app.state === "error" && was.get(app.id) !== "error" && romNeed.get()?.id !== app.id)
        fresh.push({ key: ++next.current, title: !app.installedVersion ? `${app.title} didn't install` : app.error?.startsWith("It crashed") ? `${app.title} crashed` : `${app.title} didn't start`, text: app.error ?? "" });
    }
    before.current = new Map(bridge.apps.map((a) => [a.id, a.state]));
    if (!fresh.length) return;
    setNotices((n) => [...n, ...fresh].slice(-3));
    const keys = fresh.map((f) => f.key);
    setTimeout(() => setNotices((n) => n.filter((x) => !keys.includes(x.key))), SHOWN_FOR);
  }, [bridge.apps]);

  return (
    <div className="tv-notices" aria-live="assertive">
      {notices.map((n) => (
        <div key={n.key} className="tv-notice">
          <AlertTriangle aria-hidden="true" />
          <div>
            <strong>{n.title}</strong>
            {n.text && <span>{n.text}</span>}
          </div>
        </div>
      ))}
    </div>
  );
}
