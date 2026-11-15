/**
 * Importing a Quiver Launcher 3 library, installs and all: a banner for one
 * the launcher found by itself, Settings for one the player points at, and
 * the first start's setup. Installed apps aren't moved or copied: they're
 * played from where 3.x put them.
 */
import { useEffect, useRef, useState } from "react";
import { ArchiveRestore, Check, FolderInput, LoaderCircle, X } from "lucide-react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { bringsAlong, useLauncher } from "./store";
import { native, type V3Library } from "./native";

type Progress = { done: number; total: number };
/** Importing a library's apps: idle, under way (how many are done), or done with the names of the apps that couldn't come. */
type Import = "idle" | Progress | string[];

const importing = (state: Import): state is Progress => typeof state === "object" && !Array.isArray(state);

/** `onDone` hears the names of the apps that couldn't come, once it's done. */
function useImport(onDone?: (missing: string[]) => void) {
  const { importOld, setSettings } = useLauncher();
  const [state, setState] = useState<Import>("idle");
  const start = (library: V3Library) => {
    const total = library.apps.length;
    setState({ done: 0, total });
    // A library came over: the first run's banner has nothing more to ask.
    setSettings((s) => ({ ...s, oldLibraryNoticeSeen: true }));
    importOld(library, (done) => setState({ done, total })).then(
      (missing) => {
        setState(missing);
        onDone?.(missing);
      },
      () => setState("idle"),
    );
  };
  return { state, start, reset: () => setState("idle") };
}

export const notImported = (missing: string[]) => {
  const names = missing.join(", ");
  return t`Done. Your apps are in your library, except these, as Quiver Launcher 3 doesn't know where they come from: ${names}.`;
};

const importLabel = (count: number) => t`${plural(count, { one: "Add # app", other: "Add # apps" })}`;

const installedIn = (library: V3Library) => library.apps.filter((a) => a.dir).length;

/**
 * Whether some of a library's installs look to have moved: none were found, or a folder 3.x names is gone. Just some
 * not being found isn't a sign of it, as most of those were never installed; an app's own page can locate one that was.
 */
const looksMoved = (library: V3Library) => library.apps.some((a) => !a.dir) && (Boolean(library.moved) || !installedIn(library));

/** What's in a library and what becomes of it: how many apps, how many are installed, and what else is imported. */
function offer(library: V3Library) {
  const count = library.apps.length;
  const installed = installedIn(library);
  const missing = count - installed;
  const { tagged, filters, hidden } = bringsAlong(library);
  const parts = [
    !installed
      ? t`${plural(count, { one: "# app, not installed yet. If you already installed it somewhere else, show Quiver the folder.", other: "# apps, none installed yet. If you already installed them somewhere else, show Quiver the folder." })}`
      : !missing
        ? t`${plural(count, { one: "# app, already installed.", other: "# apps, all already installed." })}`
        : t`${plural(count, { one: "# app", other: "# apps" })}, ${plural(installed, { one: "# already installed.", other: "# already installed." })}`,
    installed > 0 && looksMoved(library) && t`${plural(missing, { one: "If you moved the other one, show Quiver the folder.", other: "If you moved the others, show Quiver the folder." })}`,
    (tagged > 0 || filters > 0 || hidden > 0) && t`Your tags, filters and hidden apps will be imported too.`,
  ];
  return parts.filter(Boolean).join(" ");
}

/**
 * What comes over from a library, a line each, before it's imported: its apps and how many are installed, then each
 * other thing it brings that it has, and the apps that can't come.
 */
function Brings({ library }: { library: V3Library }) {
  const count = library.apps.length;
  const installed = installedIn(library);
  const { tagged, filters, hidden, pictures, left } = bringsAlong(library);
  const lines = [
    !installed
      ? t`${plural(count, { one: "# app, not installed yet", other: "# apps, none installed yet" })}`
      : installed === count
        ? t`${plural(count, { one: "# app, already installed", other: "# apps, all already installed" })}`
        : t`${plural(count, { one: "# app", other: "# apps" })}, ${plural(installed, { one: "# already installed", other: "# already installed" })}`,
    tagged > 0 && t`${plural(tagged, { one: "The tags you gave # app", other: "The tags you gave # apps" })}`,
    hidden > 0 && t`${plural(hidden, { one: "# app you hid, kept hidden", other: "# apps you hid, kept hidden" })}`,
    pictures > 0 && t`${plural(pictures, { one: "# picture you chose for an app", other: "# pictures you chose for your apps" })}`,
    filters > 0 && t`${plural(filters, { one: "# tag filter, as a playlist", other: "# tag filters, as playlists" })}`,
  ].filter((l): l is string => Boolean(l));
  return (
    <ul className="old-library-brings" aria-label={t`What comes over`}>
      {lines.map((line) => (
        <li key={line}>
          <Check size={15} aria-hidden="true" /> {line}
        </li>
      ))}
      {left > 0 && (
        <li className="muted">
          <X size={15} aria-hidden="true" />{" "}
          {t`${plural(left, { one: "# app can't come over, as Quiver Launcher 3 doesn't know where it comes from", other: "# apps can't come over, as Quiver Launcher 3 doesn't know where they come from" })}`}
        </li>
      )}
    </ul>
  );
}

/** Where some installs may be, when they look to have moved or none were found: the folder button finds them. */
function movedHint(library: V3Library) {
  const installed = installedIn(library);
  const missing = library.apps.length - installed;
  if (!installed) return t`${plural(library.apps.length, { one: "If you already installed it somewhere else, show Quiver the folder.", other: "If you already installed them somewhere else, show Quiver the folder." })}`;
  return looksMoved(library) && t`${plural(missing, { one: "If you moved the other one, show Quiver the folder.", other: "If you moved the others, show Quiver the folder." })}`;
}

/**
 * What happens to the files: nothing moves, so the Quiver Launcher 3 folder is kept until the player moves its apps
 * into Quiver's own apps folder, in Settings once they've tried Quiver (AppsFolder's Move into default).
 */
function KeepNote({ library, inSetup }: { library: V3Library; inSetup?: boolean }) {
  return (
    <p className="muted old-library-keep">
      {installedIn(library) && inSetup
        ? t`Adding them moves nothing: your installed apps stay where Quiver Launcher 3 put them, and play from there. Next, you choose whether to move them into Quiver Launcher 4's folder now or later.`
        : installedIn(library)
        ? t`Nothing is moved or copied: your installed apps stay where Quiver Launcher 3 put them, and play from there. Keep your Quiver Launcher 3 folder for now, as deleting it would delete them. Once you've tried Quiver Launcher 4, you can move them into Quiver's apps folder in Settings › Apps folder.`
        : t`Nothing is moved, copied or deleted, and your Quiver Launcher 3 folder stays as it is.`}
    </p>
  );
}

/** How far an import has got, as a download's bar shows it. */
function Importing({ done, total }: Progress) {
  return (
    <div className="progress old-library-progress" role="status" aria-label={t`Adding apps`}>
      <div className="bar" style={{ width: `${total ? Math.max(4, Math.floor((done / total) * 100)) : 4}%` }} />
      <span>
        <LoaderCircle size={14} className="spinning" aria-hidden="true" /> {t`Adding ${done} of ${total}…`}
      </span>
    </div>
  );
}

const notThere = () => t`That folder doesn't have Quiver Launcher 3 in it. Choose the folder its program is in.`;

/**
 * One choice of folder, whichever the player means: where more of a library's installs are, each adding to those found
 * so far (moved by hand, or on a drive with a different letter now), else another Quiver Launcher 3 to bring over
 * instead. `onFound` hears the library. Opens in the library so far, or `from`, so the player sees where it is.
 */
function useFolderChoice(library: V3Library | null | undefined, onFound: (found: V3Library) => void, from?: string) {
  const { findOld } = useLauncher();
  const [chosen, setChosen] = useState<{ library: string; folders: string[] }>();
  const [problem, setProblem] = useState<string | null>(null);
  const choose = async () => {
    const path = await native.pickFolder(library ? t`Choose a folder to look in` : t`Choose your Quiver Launcher 3 folder`, library?.folder ?? from);
    if (!path) return;
    if (library) {
      const folders = [...(chosen?.library === library.folder ? chosen.folders : []), path];
      const more = await findOld(library.folder, folders).catch(() => null);
      if (more && installedIn(more) > installedIn(library)) {
        setChosen({ library: library.folder, folders });
        setProblem(null);
        return onFound(more);
      }
    }
    const other = await findOld(path).catch(() => null);
    if (other && other.folder !== library?.folder) {
      setProblem(null);
      return onFound(other);
    }
    setProblem(library ? t`That folder doesn't have Quiver Launcher 3 or any of these apps in it.` : notThere());
  };
  return { choose, problem };
}

/** The choice of folder once there's a library: for its installs when they look to have moved, otherwise just in case. */
function ChooseFolder({ library, onClick }: { library: V3Library; onClick: () => void }) {
  return (
    <button onClick={onClick}>
      <FolderInput size={15} aria-hidden="true" /> {looksMoved(library) ? <Trans>Choose a folder…</Trans> : <Trans>Look in another folder…</Trans>}
    </button>
  );
}

/**
 * Offers to import the Quiver Launcher 3 library the launcher found by
 * itself, once: Not now, here or in the setup, leaves it to Settings. A choice
 * of folder only shows when its installs look to have moved. Until it's
 * answered, a first run that found none asks whether there's one to find, such
 * as a portable copy's.
 */
export function OldLibrary({ onDone }: { onDone: () => void }) {
  const { oldLibrary, oldInstalls, answerOldInstalls, config, settings, setSettings } = useLauncher();
  const { state, start, reset } = useImport(() => onDone());
  // One the player pointed at from here.
  const [picked, setPicked] = useState<V3Library | null>(null);
  const library = picked ?? oldLibrary;
  const { choose, problem } = useFolderChoice(library, setPicked);
  const notNow = () => setSettings((s) => ({ ...s, oldLibraryNoticeSeen: true }));
  if (Array.isArray(state)) {
    return state.length ? (
      <div className="banner" role="status">
        <p>{notImported(state)}</p>
        <button onClick={reset}>
          <Trans>OK</Trans>
        </button>
      </div>
    ) : null;
  }
  if (!library && oldInstalls) {
    const count = Object.keys(oldInstalls).length;
    return (
      <div className="banner old-library-notice" role="status" aria-label="Quiver Launcher 3">
        <ArchiveRestore size={18} aria-hidden="true" />
        <p>{t`${plural(count, { one: "Quiver Launcher 3 already installed # app in your library. Use that copy where it is, instead of downloading it again?", other: "Quiver Launcher 3 already installed # apps in your library. Use those copies where they are, instead of downloading them again?" })}`}</p>
        <button className="primary" onClick={() => answerOldInstalls(true)}>
          {t`${plural(count, { one: "Use it", other: "Use them" })}`}
        </button>
        <button onClick={() => answerOldInstalls(false)}>
          <Trans>Not now</Trans>
        </button>
      </div>
    );
  }
  if (!library) {
    // Quiver Launcher 3 was only ever on computers.
    if (settings.oldLibraryNoticeSeen || config.os === "android" || config.os === "ios") return null;
    return (
      <div className="banner old-library-notice" role="status" aria-label="Quiver Launcher 3">
        <ArchiveRestore size={18} aria-hidden="true" />
        <p>{problem ?? t`Coming from Quiver Launcher 3? Show Quiver where its folder is, and your apps will appear here.`}</p>
        <button className="primary" onClick={choose}>
          <FolderInput size={15} aria-hidden="true" /> <Trans>Choose my Quiver Launcher 3 folder…</Trans>
        </button>
        <button onClick={notNow}>
          <Trans>Not now</Trans>
        </button>
      </div>
    );
  }
  // Starting an import answers it too, so its progress still shows here.
  if (settings.oldLibraryNoticeSeen && !importing(state)) return null;
  return (
    <div className="banner old-library-notice">
      <ArchiveRestore size={18} aria-hidden="true" />
      <p>
        <strong className="old-library-question">{t`Bring your apps over from Quiver Launcher 3?`}</strong>
        {offer(library)}
        {problem && <span className="old-library-folder">{problem}</span>}
      </p>
      {importing(state) ? (
        <Importing {...state} />
      ) : (
        <>
          <button className="primary" onClick={() => start(library)}>
            {importLabel(library.apps.length)}
          </button>
          {looksMoved(library) && <ChooseFolder library={library} onClick={choose} />}
          <button onClick={notNow}>
            <Trans>Not now</Trans>
          </button>
        </>
      )}
    </div>
  );
}

/**
 * A Quiver Launcher 3 library the launcher didn't find by itself, such as a
 * portable copy's, or one the player said Not now to: the player points at its
 * folder, sees what's in it and imports it, all in Settings.
 */
export function FindOldLibrary() {
  const { oldLibrary } = useLauncher();
  const { state, start, reset } = useImport();
  const [library, setLibrary] = useState<V3Library | null>(null);
  const { choose, problem } = useFolderChoice(
    library,
    (found) => {
      reset();
      setLibrary(found);
    },
    oldLibrary?.folder,
  );
  const done = Array.isArray(state);
  return (
    <section className="settings-section" aria-label={t`Quiver Launcher 3`}>
      <h3>
        <Trans>Coming from Quiver Launcher 3?</Trans>
      </h3>
      {(!library || done) && (
        <>
          <p className="muted">
            <Trans>Show Quiver where your Quiver Launcher 3 folder is, and your apps come over with the tags you gave them, your hidden apps, the pictures you chose and your tag filters. Anything already installed plays from where it is, so nothing downloads again, and nothing is moved or deleted.</Trans>
          </p>
          <button onClick={choose}>
            <FolderInput size={15} aria-hidden="true" /> <Trans>Choose my Quiver Launcher 3 folder…</Trans>
          </button>
        </>
      )}
      {problem && (
        <p className="job-error" role="status">
          {problem}
        </p>
      )}
      {library && !done && (
        <>
          <p role="status">{t`Found in ${library.folder}. This comes over:`}</p>
          <Brings library={library} />
          {movedHint(library) && <p className="muted">{movedHint(library)}</p>}
          <KeepNote library={library} />
          {importing(state) ? (
            <Importing {...state} />
          ) : (
            <div className="row">
              <button className="primary" onClick={() => start(library)}>
                {importLabel(library.apps.length)}
              </button>
              <ChooseFolder library={library} onClick={choose} />
            </div>
          )}
        </>
      )}
      {done && (
        <p className="muted" role="status">
          {state.length ? notImported(state) : t`Your apps are in your library.`}{" "}
          {t`Any that were installed play from where Quiver Launcher 3 put them: keep that folder until you move them, in Settings › Apps folder.`}
        </p>
      )}
    </section>
  );
}

/**
 * The first start's Quiver Launcher 3 step (setup.tsx): the library the
 * launcher found, with a choice of another folder, or the question whether
 * there's one to find; which of its apps come over installed and where it
 * found them, if the player asks; then importing it. `onOffer` says whether a
 * library waits to be imported, so the setup's own Next reads Not now,
 * `onBusy` whether it's being imported, which the setup's buttons wait for,
 * and `onImported` the names of the apps that couldn't come, once it's done:
 * the setup then shows what came and offers to move its installs (MoveOldApps).
 */
export function OldLibraryStep({
  onOffer,
  onBusy,
  onImported,
}: {
  onOffer: (offering: boolean) => void;
  onBusy: (busy: boolean) => void;
  /** With the folders of the installs that came, which the setup offers to move. */
  onImported: (missing: string[], dirs: string[]) => void;
}) {
  const { oldLibrary } = useLauncher();
  // The library being imported, for what it had installed once it's in.
  const importing_ = useRef<V3Library | null>(null);
  const { state, start: startImport } = useImport((missing) => onImported(missing, (importing_.current?.apps ?? []).flatMap((a) => (a.dir && a.inApps ? [a.dir] : []))));
  const start = (library: V3Library) => {
    importing_.current = library;
    startImport(library);
  };
  // One the player pointed at from here.
  const [picked, setPicked] = useState<V3Library | null>(null);
  const [listed, setListed] = useState(false);
  const library = picked ?? oldLibrary;
  const { choose, problem } = useFolderChoice(library, setPicked);
  const offering = Boolean(library) && !Array.isArray(state);
  const busy = importing(state);
  useEffect(() => onOffer(offering), [offering]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => onBusy(busy), [busy]); // eslint-disable-line react-hooks/exhaustive-deps
  if (Array.isArray(state)) {
    return <p role="status">{state.length ? notImported(state) : t`Your apps are in your library.`}</p>;
  }
  if (library === undefined) {
    return (
      <p className="muted" role="status">
        <Trans>Looking for Quiver Launcher 3…</Trans>
      </p>
    );
  }
  if (!library) {
    return (
      <>
        <p>
          <Trans>Show Quiver where your Quiver Launcher 3 folder is, and your apps come over with the tags you gave them, your hidden apps, the pictures you chose and your tag filters. Anything already installed plays from where it is, so nothing downloads again, and nothing is moved or deleted.</Trans>
        </p>
        <button className="add-choice" onClick={choose}>
          <FolderInput size={22} aria-hidden="true" />
          <span>
            <strong>
              <Trans>Choose my Quiver Launcher 3 folder…</Trans>
            </strong>
            <small>
              <Trans>The folder with Quiver Launcher 3's program in it. For a portable copy, that's the folder you unzipped it to.</Trans>
            </small>
          </span>
        </button>
        {problem && (
          <p className="job-error" role="status">
            {problem}
          </p>
        )}
      </>
    );
  }
  const { folder, apps } = library;
  const hint = movedHint(library);
  return (
    <>
      <p>{t`Quiver found your Quiver Launcher 3 library. This comes over:`}</p>
      <Brings library={library} />
      {hint && <p className="muted">{hint}</p>}
      <KeepNote library={library} inSetup />
      <button className="link-button old-library-list" aria-expanded={listed} onClick={() => setListed(!listed)}>
        <Trans>See which apps</Trans>
      </button>
      {listed && (
        <>
          <p className="old-library-folder">{t`Found in ${folder}`}</p>
          <ul className="old-library-apps" aria-label={t`Apps in that folder`}>
            {apps.map(({ name, dir, repository }, i) => (
              <li key={i}>
                <strong>{name}</strong>
                <small>
                  {dir
                    ? t`Installed in ${dir}`
                    : repository
                      ? t`Not installed yet`
                      : t`Can't be added: Quiver Launcher 3 doesn't know where it comes from`}
                </small>
              </li>
            ))}
          </ul>
        </>
      )}
      {problem && (
        <p className="job-error" role="status">
          {problem}
        </p>
      )}
      {busy ? (
        <Importing {...state} />
      ) : (
        <div className="row">
          <button className="primary" onClick={() => start(library)}>
            {importLabel(apps.length)}
          </button>
          <ChooseFolder library={library} onClick={choose} />
        </div>
      )}
    </>
  );
}
