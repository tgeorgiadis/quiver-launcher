/**
 * Catalog components shared by Quiver Launcher and quiverlauncher.com, so an
 * app looks the same in both. Router-free: callers pass `onOpen` and actions.
 */
import { useState, type ReactNode } from "react";
import { Gamepad2 } from "lucide-react";
import type { IconType } from "react-icons";
import { FaAndroid, FaApple, FaLinux, FaWindows } from "react-icons/fa6";
import { SiIos } from "react-icons/si";
import type { Entry } from "@quiver/api";

export const OS_NAMES: Record<string, string> = {
  windows: "Windows",
  linux: "Linux",
  macos: "macOS",
  android: "Android",
  ios: "iOS",
};
const OS_ICONS: Record<string, IconType> = {
  windows: FaWindows,
  macos: FaApple,
  linux: FaLinux,
  android: FaAndroid,
  ios: SiIos,
};

export function PlatformIcons({ os }: { os: readonly string[] }) {
  const shown = Object.keys(OS_ICONS).filter((id) => os.includes(id));
  if (!shown.length) return null;
  const label = shown.map((id) => OS_NAMES[id]).join(", ");
  return (
    <div className="platform-icons" role="img" aria-label={label} title={label}>
      {shown.map((id) => {
        const Icon = OS_ICONS[id];
        return <Icon key={id} aria-hidden />;
      })}
    </div>
  );
}

export function Artwork({ src, name, className = "" }: { src?: string | null; name: string; className?: string }) {
  const [failed, setFailed] = useState(false);
  return (
    <div className={`artwork ${className}`}>
      {src && !failed ? (
        <img src={src} alt={`${name} artwork`} loading="lazy" decoding="async" onError={() => setFailed(true)} />
      ) : (
        <Gamepad2 size={52} strokeWidth={1} />
      )}
    </div>
  );
}

/** What players said about an app, in one line. */
export function Score({ runs, issues, broken }: { runs: number; issues: number; broken: number }) {
  if (runs + issues + broken === 0) return <span className="score muted">Not rated yet</span>;
  const max = Math.max(runs, issues, broken);
  const mixed = [runs, issues, broken].filter((n) => n === max).length > 1;
  const [label, tone] = mixed
    ? ["Mixed", "muted"]
    : runs === max
      ? ["Mostly runs", "positive"]
      : issues === max
        ? ["Mostly runs with issues", "caution"]
        : ["Mostly doesn't run", "negative"];
  return <span className={`score ${tone}`}>{label}</span>;
}

/** The cover image a card shows: wide library art, else the icon. */
export const coverOf = (entry: Pick<Entry, "libraryArt" | "artwork">) =>
  entry.libraryArt?.header || entry.libraryArt?.capsule || entry.artwork;

export function EntryCard({
  entry,
  onOpen,
  badge,
  action,
}: {
  entry: Entry;
  onOpen?: () => void;
  /** A label over the cover, such as "In library". */
  badge?: ReactNode;
  /** A button under the card, such as Get or Play. */
  action?: ReactNode;
}) {
  const wide = Boolean(entry.libraryArt?.header || entry.libraryArt?.capsule);
  return (
    <article className="entry-card" data-slug={entry.slug}>
      <button type="button" className="card-open" onClick={onOpen} aria-label={entry.projectName}>
        <div className="card-cover">
          <Artwork src={coverOf(entry)} name={entry.projectName} className={wide ? "cover-photo" : ""} />
          {badge && <span className="cover-badge">{badge}</span>}
        </div>
        <div className="card-body">
          <div className="card-tags">
            {entry.games[0] && <span className="card-kind">{entry.games[0].title}</span>}
            <PlatformIcons os={entry.supportedOS} />
          </div>
          <h3 className="card-title">{entry.projectName}</h3>
          <div className="card-bottom">
            <Score runs={entry.recommended} issues={entry.reportIssues} broken={entry.reportBroken} />
            {entry.verified && <span className="card-meta">v{entry.verified.version}</span>}
          </div>
        </div>
      </button>
      {action && <div className="card-action">{action}</div>}
    </article>
  );
}
