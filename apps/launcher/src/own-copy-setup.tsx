/**
 * Setting up the player's own copy of an app's game (own-copy.ts, own_copy.rs): a chip in the app page's Play row for each copy it takes, saying
 * whether it's set up, and the dialog that finds the copy where players keep them (or lets them choose
 * it), checks it and puts it where the game reads it. Play opens the dialog when the game still needs a copy.
 */
import { useEffect, useRef, useState } from "react";
import { ChevronDown, CircleCheck, Disc3, FolderOpen, FolderPlus, LoaderCircle, Play, ShieldCheck, TriangleAlert } from "lucide-react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { Entry, SetupFile } from "@quiverlauncher/api";
import { native, type CheckedCopy, type CopyProgress } from "./native";
import { useLauncher, type Install, type OwnCopy } from "./store";
import { Dialog } from "./library";
import { copiesNeeded, copyFoldersFor, delivered, EVERY_CONSOLE, rankCopies, setupFiles, sizeText, withCopyFolder } from "./own-copy";

const fileName = (path: string) => path.split(/[\\/]/).filter(Boolean).pop() ?? path;
const folderOf = (path: string) => path.slice(0, Math.max(0, path.length - fileName(path).length)).replace(/[\\/]+$/, "");

/**
 * In an installed app's Play row: a chip for each copy of the game it takes, saying whether the player's is set up,
 * that opens the dialog to set it up or change it. More than two fold into one chip that shows them.
 */
export function OwnCopyChips({ entry, install }: { entry: Entry; install: Install }) {
  const { config, setUpCopy } = useLauncher();
  const files = setupFiles(entry, config.os);
  const [gone, setGone] = useState<Set<string>>(new Set());
  const [open, setOpen] = useState(false);
  // A copy put in place before may have gone since: a drive unplugged, a file moved.
  useEffect(() => {
    let current = true;
    const checks = files.map(async (f) => {
      const copy = install.copies?.[f.id];
      if (!copy) return null;
      const there = await native.copyPresent(install.folder, install.dir, Boolean(install.wine), f, copy.path).catch(() => true);
      return there ? null : f.id;
    });
    void Promise.all(checks).then((ids) => current && setGone(new Set(ids.filter((id): id is string => !!id))));
    return () => void (current = false);
  }, [entry.id, install.copies]); // eslint-disable-line react-hooks/exhaustive-deps
  if (!files.length) return null;
  const chip = (f: SetupFile) => {
    const label = f.label;
    const copy = install.copies?.[f.id];
    const missing = Boolean(copy && gone.has(f.id));
    const tone = copy && !missing ? "ready" : (missing || (f.required !== false && delivered(f))) ? "needed" : "";
    const state = copyState(f, copy, missing);
    const action = copy ? t({ message: "Change", comment: "Button: choose another copy of the game" }) : t`Set up your copy`;
    return (
      <button key={f.id} className={`own-copy-chip ${tone}`} title={`${label}: ${state} ${action}`} aria-label={`${label}: ${state} ${action}`} onClick={() => setUpCopy({ id: entry.id, fileId: f.id })}>
        {tone === "needed" ? <TriangleAlert size={15} aria-hidden="true" /> : <Disc3 size={15} aria-hidden="true" />}
        <span className="own-copy-name">{tone === "needed" && !missing ? t`Needs your ${label}` : label}</span>
        {tone !== "needed" && <span className="own-copy-state">{chipState(f, copy)}</span>}
        {tone === "ready" && <CircleCheck size={14} aria-hidden="true" />}
      </button>
    );
  };
  if (files.length <= 2) return <>{files.map(chip)}</>;
  // Many copies (one game's discs, a collection's games): one chip with how many are set up, opening the rest.
  const ready = files.filter((f) => install.copies?.[f.id] && !gone.has(f.id)).length;
  const total = files.length;
  return (
    <>
      <button className={`own-copy-chip ${ready === total ? "ready" : "needed"}`} aria-expanded={open} onClick={() => setOpen(!open)}>
        <Disc3 size={15} aria-hidden="true" /> {t`Your copies: ${ready} of ${total} set up`}
        <ChevronDown size={14} aria-hidden="true" className={open ? "flipped" : undefined} />
      </button>
      {open && <div className="own-copy-chips">{files.map(chip)}</div>}
    </>
  );
}

/** A chip's short state, after the copy's name. */
function chipState(file: SetupFile, copy: OwnCopy | undefined) {
  if (copy) return copy.dump ?? (copy.verified ? t`Good copy` : t`Ready`);
  if (!delivered(file)) return t`Check yours`;
  return t`Optional`;
}

function copyState(file: SetupFile, copy: OwnCopy | undefined, missing: boolean) {
  if (missing) return t`It isn't where the game reads it any more. Set it up again.`;
  if (copy) {
    const dump = copy.dump;
    if (copy.verified) return dump ? t`Ready: a good copy (${dump})` : t`Ready: a good copy`;
    return t`Ready`;
  }
  if (!delivered(file)) return t`The game asks for it when it starts. Quiver can check yours first.`;
  return file.required === false ? t`Optional` : t`Needed to play`;
}

/** What the dialog asks for, in the words for what it is: a ROM, a disc image, a folder or a file. */
function intro(file: SetupFile, name: string) {
  // The game asks for this one itself: Quiver only checks it, and says which file to pick.
  if (!delivered(file)) {
    if (file.kind === "folder") return t`${name} needs your own copy of the game to run, and asks for it when it starts. Choose the folder with the game's files in it, and Quiver checks it's one ${name} takes.`;
    if (/\brom\b/i.test(file.label)) return t`${name} needs your own copy of the game to run, and asks for it when it starts. Choose the ROM, and Quiver checks it's one ${name} takes.`;
    if (/\b(disc|disk|iso|cd|dvd)\b/i.test(file.label)) return t`${name} needs your own copy of the game to run, and asks for it when it starts. Choose the disc image, and Quiver checks it's one ${name} takes.`;
    return t`${name} needs your own copy of the game to run, and asks for it when it starts. Choose the file, and Quiver checks it's one ${name} takes.`;
  }
  if (file.kind === "folder") return t`${name} needs your own copy of the game to run. Choose the folder with the game's files in it, and Quiver checks it and puts it where ${name} needs it.`;
  if (/\brom\b/i.test(file.label)) return t`${name} needs your own copy of the game to run. Choose the ROM, and Quiver checks it and puts it where ${name} needs it.`;
  if (/\b(disc|disk|iso|cd|dvd)\b/i.test(file.label)) return t`${name} needs your own copy of the game to run. Choose the disc image, and Quiver checks it and puts it where ${name} needs it.`;
  return t`${name} needs your own copy of the game to run. Choose the file, and Quiver checks it and puts it where ${name} needs it.`;
}

type Phase =
  | { kind: "looking" }
  | { kind: "choose"; found: { path: string; size: number; mine?: boolean }[] }
  | { kind: "checking"; path: string }
  | { kind: "placing"; path: string }
  | { kind: "problem"; path: string; checked: CheckedCopy | null; text: string }
  | { kind: "done"; copy: OwnCopy };

/** The dialog Play and the panel open: one copy at a time, then Ready to play. */
export function OwnCopyPrompt() {
  const { settingUpCopy: asking, setUpCopy, catalog, installs, config, settings, setSettings, consoles, setCopy, play } = useLauncher();
  const entry = asking ? catalog[asking.id] : undefined;
  const install = asking ? installs[asking.id] : undefined;
  const files = setupFiles(entry, config.os);
  // The one asked for, or each the game still needs in turn: the first left, as each set up drops out.
  const queue = asking?.fileId ? files.filter((f) => f.id === asking.fileId) : files.filter((f) => !install?.copies?.[f.id] && f.required !== false && delivered(f));
  const [placedCount, setPlacedCount] = useState(0);
  const [done, setDone] = useState<OwnCopy | null>(null);
  const file = done ? files.find((f) => install?.copies?.[f.id]?.at === done.at) ?? queue[0] : queue[0];
  const [phase, setPhase] = useState<Phase>({ kind: "looking" });
  const [progress, setProgress] = useState<CopyProgress | null>(null);
  // Files whose names don't look like this game stay folded away till asked for.
  const [showOthers, setShowOthers] = useState(false);
  // Play asked for it: the game starts once its last copy is in place.
  const [launching, setLaunching] = useState(false);
  const open = Boolean(asking && entry && install && file);
  const asked = useRef(0);

  useEffect(() => {
    setPlacedCount(0);
    setDone(null);
    setLaunching(false);
  }, [asking?.id, asking?.fileId]);
  useEffect(() => setShowOthers(false), [asking?.id, file?.id]);
  // Started from here once the store has the copy, so Play sees it and passes it to the game.
  useEffect(() => {
    if (!launching || !asking || !entry || !install) return;
    if (copiesNeeded(entry, install, config.os).length) return;
    setLaunching(false);
    setUpCopy(null);
    void play(asking.id);
  }, [launching, install?.copies]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (!open) return;
    const unlisten = native.onCopyProgress(setProgress);
    return () => void unlisten.then((f) => f());
  }, [open]);
  // Only where the player keeps this game's console's copies (Settings → Game copies): nowhere they didn't say.
  const folders = copyFoldersFor(settings.copyFolders, entry?.consoles);
  // Looks for copies as each one is asked for, and again when the player adds a folder.
  useEffect(() => {
    if (!open || !file || done) return;
    const turn = ++asked.current;
    if (!folders.length) return setPhase({ kind: "choose", found: [] });
    setPhase({ kind: "looking" });
    native.findCopies(file, folders).then(
      (found) => turn === asked.current && setPhase({ kind: "choose", found }),
      () => turn === asked.current && setPhase({ kind: "choose", found: [] }),
    );
  }, [open, file?.id, asking?.id, folders.join("\n")]); // eslint-disable-line react-hooks/exhaustive-deps

  if (!open || !entry || !install || !file || !asking) return null;
  const name = entry.projectName;
  const close = () => setUpCopy(null);
  const folder = file.kind === "folder";

  const use = async (path: string) => {
    const turn = asked.current;
    setProgress(null);
    setPhase({ kind: "checking", path });
    let checked: CheckedCopy;
    try {
      checked = await native.checkCopy(file, path);
    } catch (e) {
      return turn === asked.current && setPhase({ kind: "problem", path, checked: null, text: String(e) });
    }
    if (turn !== asked.current) return;
    if (!checked.ok) return setPhase({ kind: "problem", path, checked, text: checked.problem ?? t`That isn't a copy this game takes.` });
    setProgress(null);
    setPhase({ kind: "placing", path });
    try {
      const placed = await native.placeCopy(install.folder, install.dir, Boolean(install.wine), file, path);
      const copy: OwnCopy = { path, ...(placed ? { placed } : {}), ...(checked.dump ? { dump: checked.dump } : {}), ...(checked.verified ? { verified: true } : {}), at: Date.now() };
      setCopy(entry.id, file.id, copy);
      if (turn !== asked.current) return;
      setPlacedCount((n) => n + 1);
      // The next one the game needs, if there is one: it's first in the queue once this one drops out.
      if (!asking.fileId && queue.length > 1) return;
      setDone(copy);
      setPhase({ kind: "done", copy });
      if (asking.play) setLaunching(true);
    } catch (e) {
      if (turn === asked.current) setPhase({ kind: "problem", path, checked, text: String(e) });
    }
  };
  const choose = async () => {
    const path = await native.pickCopy(file).catch(() => null);
    if (path) void use(path);
  };
  // A folder of this game's console's copies (or, for a game on several consoles, of every console's), saved in Settings
  // → Game copies like one added there; the search runs again with it.
  const only = entry.consoles?.length === 1 ? entry.consoles[0] : undefined;
  const consoleName = only ? (consoles.find((c) => c.id === only)?.name ?? only) : undefined;
  const addFolder = async () => {
    const title = consoleName ? t`Choose where you keep your ${consoleName} games` : t`Choose a folder you keep game copies in`;
    const picked = await native.pickFolder(title, folders[0]).catch(() => null);
    if (picked) setSettings((s) => ({ ...s, copyFolders: withCopyFolder(s.copyFolders, only ?? EVERY_CONSOLE, picked, true) }));
  };
  const busy = phase.kind === "checking" || phase.kind === "placing";
  const ranked = phase.kind === "choose" ? rankCopies(phase.found, [...entry.games.map((g) => g.title), entry.name], file.label) : null;
  // Choosing a file is the way on, highlighted and focused for keys and a controller, until Quiver finds a copy that
  // looks like the game: while it's still looking, when nothing found does, and when what was chosen wasn't it.
  const chooseNext = phase.kind === "looking" || phase.kind === "problem" || (ranked !== null && !ranked.likely.length);
  const pct = progress && progress.total > 0 ? Math.min(100, Math.round((progress.done / progress.total) * 100)) : null;

  return (
    <Dialog label={t`Set up your copy for ${name}`} className="own-copy-prompt" onClose={busy ? () => {} : close}>
      <div className="detail-body">
        {phase.kind === "done" ? (
          <>
            <h2>
              <CircleCheck size={18} aria-hidden="true" /> <Trans>Ready to play</Trans>
            </h2>
            <p>
              {!delivered(file) ? (
                phase.copy.verified ? (
                  phase.copy.dump ? (
                    <Trans>Your copy is a good one ({phase.copy.dump}).</Trans>
                  ) : (
                    <Trans>Your copy is a good one.</Trans>
                  )
                ) : (
                  <Trans>Quiver has no list of good copies for {name} to check this one against.</Trans>
                )
              ) : phase.copy.verified ? (
                phase.copy.dump ? (
                  <Trans>
                    Your copy is a good one ({phase.copy.dump}), and it&apos;s where {name} reads it.
                  </Trans>
                ) : (
                  <Trans>Your copy is a good one, and it&apos;s where {name} reads it.</Trans>
                )
              ) : (
                <Trans>Your copy is where {name} reads it.</Trans>
              )}
            </p>
            {!delivered(file) && <p className="muted">{t`When ${name} asks for it, pick this file: ${fileName(phase.copy.path)}`}</p>}
            {launching ? (
              <p className="muted" role="status">
                {t`Starting ${name}…`}
              </p>
            ) : (
              <div className="row">
                <button className="primary" autoFocus onClick={() => (close(), void play(entry.id))}>
                  <Play size={16} aria-hidden="true" /> {t({ message: "Play", comment: "Button that starts an app" })}
                </button>
                <button onClick={close}>{t`Close`}</button>
              </div>
            )}
          </>
        ) : (
          <>
            <h2>
              <Disc3 size={18} aria-hidden="true" /> {t`Your copy: ${file.label}`}
            </h2>
            <p>{file.prompt ?? intro(file, name)}</p>
            {file.accepted.length > 0 && (
              <p className="muted small">
                <ShieldCheck size={14} aria-hidden="true" />{" "}
                {file.accepted.some((d) => d.label) ? t`It takes: ${file.accepted.map((d) => d.label).filter(Boolean).join(", ")}` : t`Quiver checks it's a copy the game takes.`}
              </p>
            )}
            <p className="muted small">
              <Trans>Quiver checks your copy, and puts it where the game reads it unless the game asks for it itself. It never downloads game files.</Trans>
            </p>
            {phase.kind === "looking" && (
              <p className="muted own-copy-looking" role="status">
                <LoaderCircle size={15} className="spinning" aria-hidden="true" /> {t`Looking in your game folders…`}
              </p>
            )}
            {phase.kind === "choose" &&
              (() => {
                const { likely, others } = ranked ?? { likely: [], others: [] };
                const shown = showOthers ? [...likely, ...others] : likely;
                const game = entry.games[0]?.title ?? entry.name;
                return (
                  <>
                    {shown.length > 0 && (
                      <div className="choices own-copy-choices">
                        {shown.map((f, i) => (
                          <button key={f.path} autoFocus={i === 0 && likely.length > 0} onClick={() => void use(f.path)}>
                            <strong>{fileName(f.path)}</strong>
                            <span className="muted">
                              {folderOf(f.path)}
                              {f.size ? ` · ${sizeText(f.size)}` : ""}
                            </span>
                          </button>
                        ))}
                      </div>
                    )}
                    {!folders.length ? (
                      <p className="muted">{consoleName ? t`Tell Quiver where you keep your ${consoleName} games, and it finds this copy and the next ones for you.` : t`Tell Quiver where you keep your game copies, and it finds this one and the next ones for you.`}</p>
                    ) : (
                      !phase.found.length && <p className="muted">{t`Quiver didn't find one in your game folders.`}</p>
                    )}
                    {!likely.length && (
                      <button className="link-button own-copy-add-folder" onClick={() => void addFolder()}>
                        <FolderPlus size={14} aria-hidden="true" /> {folders.length ? t`Add another folder…` : consoleName ? t`Add your ${consoleName} games folder…` : t`Add your games folder…`}
                      </button>
                    )}
                    {phase.found.length > 0 && !likely.length && <p className="muted">{t`None of the files Quiver found look like ${game}.`}</p>}
                    {others.length > 0 && (
                      <button className="link-button own-copy-others" onClick={() => setShowOthers(!showOthers)}>
                        {showOthers ? t`Hide the other files` : plural(others.length, { one: "Show # other file", other: "Show # other files" })}
                      </button>
                    )}
                  </>
                );
              })()}
            {busy && (
              <div className="progress" role="status">
                <div className="bar" style={{ width: `${pct ?? 0}%` }} />
                <span>
                  {phase.kind === "checking"
                    ? progress && pct !== null
                      ? t`Checking… ${sizeText(progress.done)} of ${sizeText(progress.total)}`
                      : t`Checking your copy…`
                    : t`Putting it in place…`}
                </span>
              </div>
            )}
            {phase.kind === "problem" && (
              <div className="own-copy-problem" role="alert">
                <p>
                  <TriangleAlert size={15} aria-hidden="true" /> {phase.text}
                </p>
                <p className="muted small own-copy-path">{phase.path}</p>
                {phase.checked?.sha1 && <p className="muted small own-copy-path">SHA-1 {phase.checked.sha1}</p>}
              </div>
            )}
            <div className="row">
              {/* Made anew as the dialog moves on, so autoFocus takes again: a found copy takes focus in its place. */}
              <button key={`${file.id}-${phase.kind}`} className={chooseNext ? "primary" : undefined} autoFocus={chooseNext} disabled={busy} onClick={() => void choose()}>
                <FolderOpen size={16} aria-hidden="true" /> {folder ? t`Choose a folder…` : t`Choose a file…`}
              </button>
              {asking.play && !busy && (
                <button onClick={() => (close(), void play(entry.id, undefined, true))}>{t`Play without it`}</button>
              )}
              <button disabled={busy} onClick={close}>
                {t`Cancel`}
              </button>
            </div>
            {!asking.fileId && placedCount + queue.length > 1 && <p className="muted small">{t`${placedCount + 1} of ${placedCount + queue.length}`}</p>}
          </>
        )}
      </div>
    </Dialog>
  );
}
