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
import { useLauncher } from "./store";
import { native, type OldApp, type V3Library } from "./native";

type Progress = { done: number; total: number };
/** Importing a library's apps: idle, under way (how many are done), or done with the names of the apps that couldn't come. */
type Import = "idle" | Progress | string[];

const importing = (state: Import): state is Progress => typeof state === "object" && !Array.isArray(state);

/** `onDone` hears the names of the apps that couldn't come, once it's done. */
function useImport(onDone?: (missing: string[]) => void) {
  const { importOld, settings, setSettings } = useLauncher();
  const [state, setState] = useState<Import>("idle");
  const start = (apps: OldApp[]) => {
    const total = apps.length;
    setState({ done: 0, total });
    // A library came over: the first run's banner has nothing more to ask.
    setSettings({ ...settings, oldLibraryNoticeSeen: true });
    importOld(apps, (done) => setState({ done, total })).then(
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

const onThisComputer = (count: number) =>
  t`Quiver Launcher 3 is on this computer, with ${plural(count, { one: "# app. Add it here?", other: "# apps. Add them here?" })} Anything already installed stays where it is, so nothing needs to download again.`;

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

/** Opens in `from`, the library found so far, so the player sees where it is and can confirm it or look elsewhere. */
const pickOldLibrary = (from?: string) => native.pickFolder(t`Choose your Quiver Launcher 3 folder`, from);

const notThere = () => t`That folder doesn't have Quiver Launcher 3 in it. Choose the folder its program is in.`;

/**
 * Offers to import the Quiver Launcher 3 library the launcher found by
 * itself, saying where it is, with a choice of another folder in case it's
 * the wrong one. Until it's answered, a first run that found none asks
 * whether there's one to find, such as a portable copy's.
 */
export function OldLibrary({ onDone }: { onDone: () => void }) {
  const { oldLibrary, findOld, config, settings, setSettings } = useLauncher();
  const { state, start, reset } = useImport(() => onDone());
  // One the player pointed at from here.
  const [picked, setPicked] = useState<V3Library | null>(null);
  const [notFound, setNotFound] = useState(false);
  const library = picked ?? oldLibrary;
  const choose = async () => {
    const path = await pickOldLibrary(library?.folder);
    if (!path) return;
    const found = await findOld(path).catch(() => null);
    // A folder without one leaves the library found before on offer.
    if (found) setPicked(found);
    setNotFound(!found);
  };
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
  if (!library) {
    // Quiver Launcher 3 was only ever on computers.
    if (settings.oldLibraryNoticeSeen || config.os === "android" || config.os === "ios") return null;
    return (
      <div className="banner old-library-notice" role="status" aria-label="Quiver Launcher 3">
        <ArchiveRestore size={18} aria-hidden="true" />
        <p>{notFound ? notThere() : t`Coming from Quiver Launcher 3? Show Quiver where its folder is, and your apps will appear here.`}</p>
        <button className="primary" onClick={choose}>
          <FolderInput size={15} aria-hidden="true" /> <Trans>Choose my Quiver Launcher 3 folder…</Trans>
        </button>
        <button onClick={() => setSettings({ ...settings, oldLibraryNoticeSeen: true })}>
          <Trans>Not now</Trans>
        </button>
      </div>
    );
  }
  const { folder, apps } = library;
  const where = t`Found in ${folder}`;
  return (
    <div className="banner old-library-notice">
      <ArchiveRestore size={18} aria-hidden="true" />
      <p>
        {onThisComputer(apps.length)}
        <span className="old-library-folder">{where}</span>
        {notFound && <span className="old-library-folder">{notThere()}</span>}
      </p>
      {importing(state) ? (
        <Importing {...state} />
      ) : (
        <>
          <button className="primary" onClick={() => start(apps)}>
            {importLabel(apps.length)}
          </button>
          <button onClick={choose}>
            <FolderInput size={15} aria-hidden="true" /> <Trans>Choose a different folder…</Trans>
          </button>
        </>
      )}
    </div>
  );
}

/**
 * A Quiver Launcher 3 library the launcher didn't find by itself, such as a
 * portable copy's: the player points at its folder, sees what's in it and
 * imports it, all in Settings.
 */
export function FindOldLibrary() {
  const { findOld, oldLibrary } = useLauncher();
  const { state, start, reset } = useImport();
  const [library, setLibrary] = useState<V3Library | null>(null);
  const found = library?.apps ?? [];
  const [notFound, setNotFound] = useState(false);
  const count = found.length;
  const installed = found.filter((a) => a.dir).length;
  return (
    <section className="settings-section" aria-label={t`Quiver Launcher 3`}>
      <h3>
        <Trans>Coming from Quiver Launcher 3?</Trans>
      </h3>
      <p className="muted">
        <Trans>Show Quiver where your Quiver Launcher 3 folder is, and your apps will appear here. Anything already installed stays where it is, so nothing needs to download again.</Trans>
      </p>
      <button
        disabled={importing(state)}
        onClick={async () => {
          // Where the last one was, or the one the launcher found by itself.
          const path = await pickOldLibrary((library ?? oldLibrary)?.folder);
          if (!path) return;
          const picked = await findOld(path).catch(() => null);
          reset();
          setLibrary(picked);
          setNotFound(!picked);
        }}
      >
        <FolderInput size={15} aria-hidden="true" /> <Trans>Choose my Quiver Launcher 3 folder…</Trans>
      </button>
      {notFound && (
        <p className="job-error" role="status">
          {notThere()}
        </p>
      )}
      {count > 0 && !Array.isArray(state) && (
        <>
          <p role="status">
            {installed
              ? t`That folder has ${plural(count, { one: "# app", other: "# apps" })}, ${plural(installed, { one: "# installed", other: "# installed" })}. Add them here? Anything already installed stays where it is, so nothing needs to download again.`
              : t`That folder has ${plural(count, { one: "# app", other: "# apps" })}, none installed. Add them here?`}
          </p>
          {importing(state) ? (
            <Importing {...state} />
          ) : (
            <button className="primary" onClick={() => start(found)}>
              {importLabel(count)}
            </button>
          )}
        </>
      )}
      {Array.isArray(state) && (
        <p className="muted" role="status">
          {state.length ? notImported(state) : t`Your apps are in your library.`}
        </p>
      )}
    </section>
  );
}

/**
 * The first start's Quiver Launcher 3 step (setup.tsx): the library the
 * launcher found, with where it is and a choice of another folder, or the
 * question whether there's one to find; which of its apps come over
 * installed and which don't; then importing it. `onOffer` says whether a
 * library waits to be imported, so the setup's own Next reads Not now,
 * `onBusy` whether it's being imported, which the setup's buttons wait for,
 * and `onImported` the names of the apps that couldn't come, once it's done.
 */
export function OldLibraryStep({ onOffer, onBusy, onImported }: { onOffer: (offering: boolean) => void; onBusy: (busy: boolean) => void; onImported: (missing: string[]) => void }) {
  const { oldLibrary, findOld } = useLauncher();
  const { state, start } = useImport(onImported);
  // One the player pointed at from here.
  const [picked, setPicked] = useState<V3Library | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const library = picked ?? oldLibrary;
  const offering = Boolean(library) && !Array.isArray(state);
  const busy = importing(state);
  useEffect(() => onOffer(offering), [offering]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => onBusy(busy), [busy]); // eslint-disable-line react-hooks/exhaustive-deps
  const choose = async () => {
    const path = await pickOldLibrary(library?.folder);
    if (!path) return;
    const found = await findOld(path).catch(() => null);
    // A folder without one leaves the library found before on offer.
    if (found) setPicked(found);
    setProblem(found ? null : notThere());
  };
  const chooseApps = async (folder: string) => {
    const path = await native.pickFolder(t`Choose Quiver Launcher 3's Apps folder`, folder);
    if (!path) return;
    const found = await findOld(folder, path).catch(() => null);
    const some = found?.apps.some((a) => a.dir);
    if (some) setPicked(found);
    setProblem(some ? null : t`None of these apps are installed in that folder.`);
  };
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
  const installed = apps.filter((a) => a.dir).length;
  return (
    <>
      <p>
        {onThisComputer(apps.length)}
        <span className="old-library-folder">{t`Found in ${folder}`}</span>
      </p>
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
      {!installed && (
        <p className="muted">
          <Trans>None of these show as installed. If you moved Quiver Launcher 3's Apps folder, or its drive has a different letter now, show Quiver where it is.</Trans>
        </p>
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
          <button className="primary" onClick={() => start(apps)}>
            {importLabel(apps.length)}
          </button>
          <button onClick={choose}>
            <FolderInput size={15} aria-hidden="true" /> <Trans>Choose a different folder…</Trans>
          </button>
          {!installed && (
            <button onClick={() => chooseApps(folder)}>
              <FolderInput size={15} aria-hidden="true" /> <Trans>Choose the Apps folder…</Trans>
            </button>
          )}
        </div>
      )}
    </>
  );
}
