/**
 * Adding an app the catalog doesn't list: a GitHub or GitLab repository, a
 * program already on this computer, or a folder to fill. Each can take the
 * artwork and consoles of the game it plays, matched by its name.
 */
import { useEffect, useRef, useState, type ReactNode } from "react";
import { ArrowLeft, FolderPlus, GitBranch, MonitorPlay } from "lucide-react";
import { msg, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { MessageDescriptor } from "@lingui/core";
import { i18n } from "@quiverlauncher/i18n";
import type { Entry, GameMatch } from "@quiverlauncher/api";
import { Artwork, fullDate } from "@quiverlauncher/ui";
import { useLauncher, type GameArt, type RepositoryPreview } from "./store";
import { native } from "./native";
import { assetFilterFor } from "./assets";
import { gameArt, gamesNamed } from "./custom";
import { closesOutside } from "./overlay";

type Step = "choose" | "repository" | "program" | "folder";

export function AddApp({ onClose, onOpen }: { onClose: () => void; onOpen: (entry: Entry) => void }) {
  const [step, setStep] = useState<Step>("choose");
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);
  const done = (entry: Entry) => (onClose(), onOpen(entry));
  const back = () => setStep("choose");
  return (
    <div className="overlay" {...closesOutside(onClose)}>
      <section className="detail choose add-app" role="dialog" aria-label={t`Add an app`} onClick={(e) => e.stopPropagation()}>
        <div className="detail-body">
          {step === "choose" && <Choose onPick={setStep} />}
          {step === "repository" && <FromRepository onBack={back} onDone={done} />}
          {step === "program" && <FromProgram onBack={back} onDone={done} />}
          {step === "folder" && <FromFolder onBack={back} onDone={done} />}
        </div>
      </section>
    </div>
  );
}

function Choose({ onPick }: { onPick: (step: Step) => void }) {
  const option = (step: Step, icon: ReactNode, title: string, text: string) => (
    <button type="button" className="add-choice" onClick={() => onPick(step)}>
      {icon}
      <span>
        <strong>{title}</strong>
        <small>{text}</small>
      </span>
    </button>
  );
  return (
    <>
      <h2>
        <Trans>Add an app</Trans>
      </h2>
      <p className="muted">
        <Trans>For an app the catalog doesn't have. It's yours alone; to add one for everyone, suggest it on quiverlauncher.com.</Trans>
      </p>
      {option("repository", <GitBranch size={22} />, t`From GitHub or GitLab`, t`Installs from the repository's releases, and updates like other apps.`)}
      {option("program", <MonitorPlay size={22} />, t`A program on this computer`, t`Starts it where it is. Nothing is downloaded, moved or deleted.`)}
      {option("folder", <FolderPlus size={22} />, t`A folder you fill yourself`, t`Makes a folder in your apps folder to put the app in.`)}
    </>
  );
}

function Heading({ title, onBack }: { title: string; onBack: () => void }) {
  return (
    <>
      <button type="button" className="back-link" onClick={onBack}>
        <ArrowLeft size={15} /> <Trans>Other ways to add</Trans>
      </button>
      <h2>{title}</h2>
    </>
  );
}

type Done = { onBack: () => void; onDone: (entry: Entry) => void };

function FromRepository({ onBack, onDone }: Done) {
  const { lookUpRepository, addCustomApp } = useLauncher();
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [preview, setPreview] = useState<RepositoryPreview | null>(null);
  const [name, setName] = useState("");
  const [file, setFile] = useState<string | undefined>();
  const [art, setArt] = useState<GameArt | null>(null);
  // A lookup that ends after the player went back or closed the dialog is dropped.
  // Set on mount as well: in development, React's StrictMode unmounts and mounts it again.
  const live = useRef(true);
  useEffect(() => {
    live.current = true;
    return () => {
      live.current = false;
    };
  }, []);
  if (!preview)
    return (
      <form
        onSubmit={(e) => {
          e.preventDefault();
          setBusy(true);
          setError(null);
          lookUpRepository(input).then((r) => {
            if (!live.current) return;
            setBusy(false);
            if ("error" in r) return setError(r.error);
            // The catalog lists it: its own page, with checked releases.
            if ("listed" in r) return onDone(r.listed);
            setPreview(r.preview);
            setName(r.preview.app.name);
            setFile(r.preview.files[0]?.filename);
          });
        }}
      >
        <Heading title={t`From GitHub or GitLab`} onBack={onBack} />
        <input autoFocus aria-label={t`Repository`} placeholder={t`owner/name, or a github.com or gitlab.com address`} value={input} onChange={(e) => setInput(e.target.value)} />
        {error && <p className="job-error">{error}</p>}
        <button className="primary" disabled={busy || !input.trim()}>
          {busy ? t`Looking it up…` : t`Look it up`}
        </button>
      </form>
    );
  const { latest, files } = preview;
  const filenames = files.map((f) => f.filename);
  const only = files[0]?.filename;
  const version = latest?.version ?? "";
  const date = latest?.releasedAt ? fullDate(latest.releasedAt) : "";
  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        // Several apps in one release: keep to the one picked, in later releases too.
        const assetFilter = files.length > 1 && file ? assetFilterFor(file, filenames) : undefined;
        onDone(addCustomApp({ ...preview.app, name: name.trim(), ...(assetFilter ? { assetFilter } : {}) }, latest, art ?? undefined));
      }}
    >
      <Heading title={`${preview.app.provider === "gitlab" ? "gitlab.com" : "github.com"}/${preview.app.repository}`} onBack={onBack} />
      <label className="field">
        <Trans comment="The app's name, a form field">Name</Trans>
        <input aria-label={t({ message: "Name", comment: "The app's name, a form field" })} maxLength={200} value={name} onChange={(e) => setName(e.target.value)} />
      </label>
      <p className="muted">
        {latest ? (date ? t`Latest release ${version} · ${date}` : t`Latest release ${version}`) : t`This repository has no releases yet.`}
        {" · "}
        <Trans>Quiver hasn't checked this app.</Trans>
      </p>
      {latest && files.length === 0 && (
        <p className="job-error">
          <Trans>None of this release's files are for this computer.</Trans>
        </p>
      )}
      {files.length === 1 && (
        <p className="muted">
          <Trans comment="Followed by a file name: the file that gets downloaded">Downloads {only}</Trans>
        </p>
      )}
      {files.length > 1 && (
        <fieldset className="pick-file">
          <legend>
            <Trans>This release has several downloads for this computer. Which app is this?</Trans>
          </legend>
          {filenames.map((f) => (
            <label key={f}>
              <input type="radio" name="file" value={f} checked={file === f} onChange={() => setFile(f)} /> {f}
            </label>
          ))}
        </fieldset>
      )}
      <ArtMatch name={name} value={art} onChange={setArt} />
      <button className="primary" disabled={!files.length || !name.trim()}>
        <Trans>Add and get</Trans>
      </button>
    </form>
  );
}

// What to pick on each system, put in the language when shown, not here, where the module loads before any is picked.
const PROGRAMS: Record<string, MessageDescriptor> = {
  windows: msg`Choose a program (.exe) that starts the app.`,
  linux: msg`Choose an AppImage or another program that starts the app.`,
  macos: msg`Choose an app that starts the app.`,
};
const ANY_PROGRAM = msg`Choose the program that starts the app.`;

function FromProgram({ onBack, onDone }: Done) {
  const { config, addLocalApp } = useLauncher();
  const [path, setPath] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [art, setArt] = useState<GameArt | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [picking, setPicking] = useState(false);
  const choose = () => {
    setPicking(true);
    native.pickProgram().then((picked) => {
      setPicking(false);
      if (!picked) return;
      setPath(picked);
      // "Super Game.exe" is "Super Game".
      setName(picked.split(/[\\/]/).pop()!.replace(/\.(exe|lnk|bat|cmd|appimage|app|sh|x86_64)$/i, ""));
    }, (e) => (setPicking(false), setError(String(e))));
  };
  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        if (!path) return;
        addLocalApp({ kind: "program", path, name: name.trim() }, art ?? undefined).then(onDone, (e) => setError(String(e)));
      }}
    >
      <Heading title={t`A program on this computer`} onBack={onBack} />
      <p className="muted">
        {i18n._(PROGRAMS[config.os] ?? ANY_PROGRAM)} <Trans>It starts where it is, and removing it from your library leaves it there.</Trans>
      </p>
      <div className="row">
        <button type="button" onClick={choose} disabled={picking}>
          {path ? t`Choose another program…` : t`Choose the program…`}
        </button>
      </div>
      {path && (
        <>
          <p className="muted picked-path">{path}</p>
          <label className="field">
            <Trans comment="The app's name, a form field">Name</Trans>
            <input aria-label={t({ message: "Name", comment: "The app's name, a form field" })} maxLength={200} value={name} onChange={(e) => setName(e.target.value)} />
          </label>
          <ArtMatch name={name} value={art} onChange={setArt} />
        </>
      )}
      {error && <p className="job-error">{error}</p>}
      <button className="primary" disabled={!path || !name.trim()}>
        <Trans comment="Button: add the app to the library">Add</Trans>
      </button>
    </form>
  );
}

function FromFolder({ onBack, onDone }: Done) {
  const { config, addLocalApp } = useLauncher();
  const [name, setName] = useState("");
  const [art, setArt] = useState<GameArt | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const dir = config.appsDir;
  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        setBusy(true);
        setError(null);
        addLocalApp({ kind: "folder", name: name.trim() }, art ?? undefined).then(onDone, (e) => (setError(String(e)), setBusy(false)));
      }}
    >
      <Heading title={t`A folder you fill yourself`} onBack={onBack} />
      <p className="muted">
        <Trans>Quiver makes a folder for it in {dir} and opens it. Put the app's files there; Play starts the first program in it.</Trans>
      </p>
      <label className="field">
        <Trans comment="The app's name, a form field">Name</Trans>
        <input autoFocus aria-label={t({ message: "Name", comment: "The app's name, a form field" })} maxLength={100} value={name} onChange={(e) => setName(e.target.value)} />
      </label>
      <ArtMatch name={name} value={art} onChange={setArt} />
      {error && <p className="job-error">{error}</p>}
      <button className="primary" disabled={busy || !name.trim()}>
        <Trans>Make the folder</Trans>
      </button>
    </form>
  );
}

/**
 * The artwork and consoles of the game an app plays, found by its name, as
 * catalog apps have. The best match is picked for the player; they can pick
 * another or none.
 */
function ArtMatch({ name, value, onChange }: { name: string; value: GameArt | null; onChange: (art: GameArt | null) => void }) {
  const { client } = useLauncher();
  const [matches, setMatches] = useState<GameMatch[]>([]);
  // Undefined until the player picks: until then the best match is picked for them.
  const picked = useRef<string | null | undefined>(undefined);
  const latest = useRef(0);
  function choose(slug: string | null) {
    const request = ++latest.current;
    if (!slug) return onChange(null);
    client.game(slug).then(
      (d) => request === latest.current && onChange(d ? gameArt(slug, d) : null),
      () => {},
    );
  }
  useEffect(() => {
    let live = true;
    const timer = setTimeout(
      () =>
        gamesNamed(client, name).then(
          (found) => {
            if (!live) return;
            setMatches(found);
            if (picked.current === undefined) choose(found[0]?.slug ?? null);
          },
          () => {},
        ),
      300,
    );
    return () => {
      live = false;
      clearTimeout(timer);
    };
  }, [name, client]); // eslint-disable-line react-hooks/exhaustive-deps
  if (!matches.length && !value) return null;
  const pick = (slug: string | null) => ((picked.current = slug), choose(slug));
  // A pick from an earlier name stays on offer.
  const shown = value && !matches.some((m) => m.slug === value.game.slug) ? [{ slug: value.game.slug, title: value.game.title, art: value.libraryArt?.capsule, apps: 0 }, ...matches] : matches;
  return (
    <fieldset className="art-match">
      <legend>
        <Trans>Artwork and consoles from the game</Trans>
      </legend>
      {shown.map((g) => (
        <button type="button" key={g.slug} data-game={g.slug} aria-pressed={value?.game.slug === g.slug} onClick={() => pick(g.slug)}>
          <Artwork src={g.art} name={g.title} className="cover-photo" />
          <span>{g.title}</span>
        </button>
      ))}
      <button type="button" aria-pressed={!value} onClick={() => pick(null)}>
        <span>
          <Trans comment="No game's artwork and consoles">None</Trans>
        </span>
      </button>
    </fieldset>
  );
}
