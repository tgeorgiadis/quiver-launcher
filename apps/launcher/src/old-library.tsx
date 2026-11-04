/**
 * Importing a Quiver Launcher 3 library, installs and all: a banner for one
 * the launcher found by itself, Settings for one the player points at, and
 * the first start's setup. Installed apps aren't moved or copied: they're
 * played from where 3.x put them.
 */
import { useEffect, useState } from "react";
import { ArchiveRestore, FolderInput, LoaderCircle } from "lucide-react";
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

const notImported = (missing: string[]) => {
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
            <Trans>Show Quiver where your Quiver Launcher 3 folder is, and your apps will appear here. Anything already installed stays where it is, so nothing needs to download again.</Trans>
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
          <p role="status">{offer(library)}</p>
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
          {state.length ? notImported(state) : t`Your apps are in your library.`}
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
 * and `onImported` the names of the apps that couldn't come, once it's done.
 */
export function OldLibraryStep({ onOffer, onBusy, onImported }: { onOffer: (offering: boolean) => void; onBusy: (busy: boolean) => void; onImported: (missing: string[]) => void }) {
  const { oldLibrary } = useLauncher();
  const { state, start } = useImport(onImported);
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
          <Trans>Show Quiver where your Quiver Launcher 3 folder is, and your apps will appear here. Anything already installed stays where it is, so nothing needs to download again.</Trans>
        </p>
        <button className="add-choice" onClick={choose}>
          <FolderInput size={22} aria-hidden="true" />
          <span>
            <strong>
              <Trans>Choose my Quiver Launcher 3 folder…</Trans>
            </strong>
            <small>
              <Trans>The folder with Quiver Launcher 3's program in it.</Trans>
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
  return (
    <>
      <p>{offer(library)}</p>
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
