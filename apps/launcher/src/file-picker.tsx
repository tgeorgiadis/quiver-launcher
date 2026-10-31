/**
 * Choosing a file or a folder in Steam's Game Mode (picker.rs): GTK's
 * dialog there is a desktop window that a controller can't get around and
 * a trackpad's clicks miss, so the launcher asks here instead, as any other
 * of its dialogs. Places to start from (home, Downloads, a microSD card),
 * Up, a row for each folder (and file, for a program), and Choose this
 * folder or Cancel; B cancels.
 */
import { useEffect, useRef, useState } from "react";
import { ArrowUp, File, Folder } from "lucide-react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { native, type Listing, type PickAsk } from "./native";
import { closesOutside } from "./overlay";

/** Answers the pickers' questions one at a time, in the order they came. */
export function FilePicker() {
  const [asks, setAsks] = useState<PickAsk[]>([]);
  useEffect(() => {
    const off = native.onPick((ask) => setAsks((q) => [...q, ask]));
    return () => void off.then((stop) => stop());
  }, []);
  const ask = asks[0];
  if (!ask) return null;
  const done = (path: string | null) => {
    void native.picked(ask.id, path);
    setAsks((q) => q.slice(1));
  };
  return <Browser key={ask.id} ask={ask} onDone={done} />;
}

function Browser({ ask, onDone }: { ask: PickAsk; onDone: (path: string | null) => void }) {
  const [listing, setListing] = useState<Listing | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const list = useRef<HTMLDivElement>(null);
  const go = (path: string | null) =>
    native.browse(path).then(
      (l) => (setListing(l), setProblem(null)),
      (e) => setProblem(String(e)),
    );
  useEffect(() => void go(ask.dir ?? null), []); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onDone(null);
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onDone]);
  // Each folder opened starts on its first row, so the d-pad goes down it.
  useEffect(() => list.current?.querySelector<HTMLElement>(".file-picker-list button, .file-picker-up")?.focus(), [listing?.path]);
  const sep = listing?.path.includes("\\") ? "\\" : "/";
  const inside = (name: string) => (listing!.path.endsWith(sep) ? listing!.path + name : listing!.path + sep + name);
  const wanted = (name: string) => !ask.extensions.length || ask.extensions.includes(name.split(".").pop()?.toLowerCase() ?? "");
  const shown = listing?.entries.filter((e) => e.folder || (!ask.folder && wanted(e.name))) ?? [];
  return (
    <div className="overlay" {...closesOutside(() => onDone(null))}>
      <section className="detail choose file-picker" role="dialog" aria-label={ask.title} onClick={(e) => e.stopPropagation()}>
        <div className="detail-body" ref={list}>
          <h2>{ask.title}</h2>
          {listing && listing.places.length > 0 && (
            <div className="row file-picker-places" role="group" aria-label={t`Places`}>
              {listing.places.map((place) => (
                <button key={place.path} className={place.path === listing.path ? "chosen" : undefined} onClick={() => void go(place.path)}>
                  {place.name}
                </button>
              ))}
            </div>
          )}
          {listing && (
            <div className="file-picker-path">
              <button className="file-picker-up" disabled={!listing.parent} onClick={() => void go(listing.parent ?? null)}>
                <ArrowUp size={15} aria-hidden="true" /> <Trans comment="Goes to the folder around the one shown">Up</Trans>
              </button>
              <span>{listing.path}</span>
            </div>
          )}
          {listing && (
            <ul className="file-picker-list" aria-label={t`In this folder`}>
              {shown.map((entry) => (
                <li key={entry.name}>
                  <button onClick={() => (entry.folder ? void go(inside(entry.name)) : onDone(inside(entry.name)))}>
                    {entry.folder ? <Folder size={16} aria-hidden="true" /> : <File size={16} aria-hidden="true" />}
                    <span>{entry.name}</span>
                  </button>
                </li>
              ))}
              {!shown.length && <li className="muted">{ask.folder ? t`No folders in here.` : t`Nothing to choose in this folder.`}</li>}
            </ul>
          )}
          {problem && (
            <p className="job-error" role="status">
              {problem}
            </p>
          )}
          <div className="row">
            {ask.folder && listing && (
              <button className="primary" onClick={() => onDone(listing.path)}>
                <Trans>Choose this folder</Trans>
              </button>
            )}
            <button onClick={() => onDone(null)}>
              <Trans>Cancel</Trans>
            </button>
          </div>
        </div>
      </section>
    </div>
  );
}
