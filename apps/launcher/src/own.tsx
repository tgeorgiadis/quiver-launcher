/**
 * Adding an app the catalog doesn't list: a GitHub or GitLab repository, a
 * program already on this computer, or a folder to fill. Each can take the
 * artwork and consoles of the game it plays, matched by its name.
 */
import { useEffect, useRef, useState, type ReactNode } from "react";
import { ArrowLeft, FolderPlus, GitBranch, MonitorPlay } from "lucide-react";
import type { Entry, GameMatch } from "@quiver/api";
import { Artwork, fullDate } from "@quiver/ui";
import { useLauncher, type GameArt, type RepositoryPreview } from "./store";
import { native } from "./native";
import { assetFilterFor } from "./assets";

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
    <div className="overlay" onClick={onClose}>
      <section className="detail choose add-app" role="dialog" aria-label="Add an app" onClick={(e) => e.stopPropagation()}>
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
      <h2>Add an app</h2>
      <p className="muted">For an app the catalog doesn't have. It's yours alone; to add one for everyone, suggest it on quiverlauncher.com.</p>
      {option("repository", <GitBranch size={22} />, "From GitHub or GitLab", "Installs from the repository's releases, and updates like other apps.")}
      {option("program", <MonitorPlay size={22} />, "A program on this computer", "Starts it where it is. Nothing is downloaded, moved or deleted.")}
      {option("folder", <FolderPlus size={22} />, "A folder you fill yourself", "Makes a folder in your apps folder to put the app in.")}
    </>
  );
}

function Heading({ title, onBack }: { title: string; onBack: () => void }) {
  return (
    <>
      <button type="button" className="back-link" onClick={onBack}>
        <ArrowLeft size={15} /> Other ways to add
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
  if (!preview)
    return (
      <form
        onSubmit={(e) => {
          e.preventDefault();
          setBusy(true);
          setError(null);
          lookUpRepository(input).then((r) => {
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
        <Heading title="From GitHub or GitLab" onBack={onBack} />
        <input autoFocus aria-label="Repository" placeholder="owner/name, or a github.com or gitlab.com address" value={input} onChange={(e) => setInput(e.target.value)} />
        {error && <p className="job-error">{error}</p>}
        <button className="primary" disabled={busy || !input.trim()}>
          {busy ? "Looking it up…" : "Look it up"}
        </button>
      </form>
    );
  const { latest, files } = preview;
  const filenames = files.map((f) => f.filename);
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
        Name
        <input aria-label="Name" maxLength={200} value={name} onChange={(e) => setName(e.target.value)} />
      </label>
      <p className="muted">
        {latest ? `Latest release ${latest.version}${latest.releasedAt ? ` · ${fullDate(latest.releasedAt)}` : ""}` : "This repository has no releases yet."}
        {" · Quiver hasn't checked this app."}
      </p>
      {latest && files.length === 0 && <p className="job-error">None of this release's files are for this computer.</p>}
      {files.length === 1 && <p className="muted">Downloads {files[0].filename}</p>}
      {files.length > 1 && (
        <fieldset className="pick-file">
          <legend>This release has several downloads for this computer. Which app is this?</legend>
          {filenames.map((f) => (
            <label key={f}>
              <input type="radio" name="file" value={f} checked={file === f} onChange={() => setFile(f)} /> {f}
            </label>
          ))}
        </fieldset>
      )}
      <ArtMatch name={name} value={art} onChange={setArt} />
      <button className="primary" disabled={!files.length || !name.trim()}>
        Add and get
      </button>
    </form>
  );
}

const PROGRAMS: Record<string, string> = {
  windows: "a program (.exe) or a shortcut to one",
  linux: "an AppImage or another program",
  macos: "an app",
};

function FromProgram({ onBack, onDone }: Done) {
  const { config, addLocalApp } = useLauncher();
  const [path, setPath] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [art, setArt] = useState<GameArt | null>(null);
  const [error, setError] = useState<string | null>(null);
  const choose = () =>
    native.pickProgram().then((picked) => {
      if (!picked) return;
      setPath(picked);
      // "Super Game.exe" is "Super Game".
      setName(picked.split(/[\\/]/).pop()!.replace(/\.(exe|lnk|bat|cmd|appimage|app|sh|x86_64)$/i, ""));
    }, (e) => setError(String(e)));
  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        if (!path) return;
        addLocalApp({ kind: "program", path, name: name.trim() }, art ?? undefined).then(onDone, (e) => setError(String(e)));
      }}
    >
      <Heading title="A program on this computer" onBack={onBack} />
      <p className="muted">Choose {PROGRAMS[config.os] ?? "the program"} that starts the app. It starts where it is, and removing it from your library leaves it there.</p>
      <div className="row">
        <button type="button" onClick={choose}>
          {path ? "Choose another program…" : "Choose the program…"}
        </button>
      </div>
      {path && (
        <>
          <p className="muted picked-path">{path}</p>
          <label className="field">
            Name
            <input aria-label="Name" maxLength={200} value={name} onChange={(e) => setName(e.target.value)} />
          </label>
          <ArtMatch name={name} value={art} onChange={setArt} />
        </>
      )}
      {error && <p className="job-error">{error}</p>}
      <button className="primary" disabled={!path || !name.trim()}>
        Add
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
  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        setBusy(true);
        setError(null);
        addLocalApp({ kind: "folder", name: name.trim() }, art ?? undefined).then(onDone, (e) => (setError(String(e)), setBusy(false)));
      }}
    >
      <Heading title="A folder you fill yourself" onBack={onBack} />
      <p className="muted">
        Quiver makes a folder for it in {config.appsDir} and opens it. Put the app's files there; Play starts the first program in it.
      </p>
      <label className="field">
        Name
        <input autoFocus aria-label="Name" maxLength={100} value={name} onChange={(e) => setName(e.target.value)} />
      </label>
      <ArtMatch name={name} value={art} onChange={setArt} />
      {error && <p className="job-error">{error}</p>}
      <button className="primary" disabled={busy || !name.trim()}>
        Make the folder
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
  const [picked, setPicked] = useState<string | null | undefined>(undefined);
  const latest = useRef(0);
  function choose(slug: string | null) {
    const request = ++latest.current;
    if (!slug) return onChange(null);
    client.game(slug).then(
      (d) =>
        request === latest.current &&
        onChange(d ? { game: { id: d.game.id, slug, title: d.game.title }, artwork: d.game.artwork, libraryArt: d.game.libraryArt, consoles: d.game.originalSystems } : null),
      () => {},
    );
  }
  useEffect(() => {
    let live = true;
    const timer = setTimeout(
      () =>
        client.matchingGames(name).then(
          (found) => {
            if (!live) return;
            setMatches(found);
            if (picked === undefined) choose(found[0]?.slug ?? null);
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
  const pick = (slug: string | null) => (setPicked(slug), choose(slug));
  // A pick from an earlier name stays on offer.
  const shown = value && !matches.some((m) => m.slug === value.game.slug) ? [{ slug: value.game.slug, title: value.game.title, art: value.libraryArt?.capsule, apps: 0 }, ...matches] : matches;
  return (
    <fieldset className="art-match">
      <legend>Artwork and consoles from the game</legend>
      {shown.map((g) => (
        <button type="button" key={g.slug} data-game={g.slug} aria-pressed={value?.game.slug === g.slug} onClick={() => pick(g.slug)}>
          <Artwork src={g.art} name={g.title} className="cover-photo" />
          <span>{g.title}</span>
        </button>
      ))}
      <button type="button" aria-pressed={!value} onClick={() => pick(null)}>
        <span>None</span>
      </button>
    </fieldset>
  );
}
