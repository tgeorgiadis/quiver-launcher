/**
 * The player's library and what's installed, kept on this device.
 *
 * A library item only points at its catalog entry (plus the player's own
 * overrides); names, artwork and files always come from the catalog. The
 * last catalog data seen is cached so the library shows instantly offline.
 */
import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { createClient, type Asset, type Client, type Entry } from "@quiver/api";
import { native, type Config, type Progress } from "./native";
import { bestAssets } from "./assets";

export type LibraryItem = {
  /** The catalog entry's id. */
  id: string;
  slug: string;
  addedAt: number;
  overrides?: { name?: string; cover?: string };
};
export type Install = { version: string; folder: string; executables: string[] };
export type Job = Progress | { error: string };
/** Several files suit this computer: the player picks one. */
export type Choice = { entry: Entry; version: string; assets: Asset[]; resolve: (asset: Asset | null) => void };

type Launcher = {
  config: Config;
  client: Client;
  library: LibraryItem[];
  installs: Record<string, Install>;
  catalog: Record<string, Entry>;
  jobs: Record<string, Job>;
  choice: Choice | null;
  remember: (entries: Entry[]) => void;
  /** Adds the app to the library and installs it. */
  get: (entry: Entry) => Promise<void>;
  play: (id: string) => Promise<void>;
  remove: (id: string) => Promise<void>;
  dismiss: (id: string) => void;
};

const LauncherContext = createContext<Launcher | null>(null);

export const useLauncher = () => useContext(LauncherContext)!;

export function hasUpdate(entry: Entry | undefined, install: Install | undefined) {
  return Boolean(entry?.verified && install && install.version !== entry.verified.version);
}

export function LauncherProvider({ children }: { children: ReactNode }) {
  const [loaded, setLoaded] = useState<{
    config: Config;
    library: LibraryItem[];
    installs: Record<string, Install>;
    catalog: Record<string, Entry>;
  } | null>(null);
  const [library, setLibrary] = useState<LibraryItem[]>([]);
  const [installs, setInstalls] = useState<Record<string, Install>>({});
  const [catalog, setCatalog] = useState<Record<string, Entry>>({});
  const [jobs, setJobs] = useState<Record<string, Job>>({});
  const [choice, setChoice] = useState<Choice | null>(null);

  useEffect(() => {
    Promise.all([
      native.config(),
      native.readState<LibraryItem[]>("library"),
      native.readState<Record<string, Install>>("installs"),
      native.readState<Record<string, Entry>>("catalog"),
    ]).then(([config, library, installs, catalog]) => {
      setLibrary(library ?? []);
      setInstalls(installs ?? {});
      setCatalog(catalog ?? {});
      setLoaded({ config, library: library ?? [], installs: installs ?? {}, catalog: catalog ?? {} });
    });
    const off = native.onProgress((p) => setJobs((j) => ({ ...j, [p.id]: p })));
    return () => void off.then((stop) => stop());
  }, []);

  // Save each file when it changes, after the first load.
  usePersist("library", library, loaded?.library);
  usePersist("installs", installs, loaded?.installs);
  usePersist("catalog", catalog, loaded?.catalog);

  const client = useMemo(() => createClient(loaded?.config.api), [loaded?.config.api]);

  // Refresh the library's catalog data (new versions, art) once per start.
  useEffect(() => {
    if (!loaded) return;
    for (const item of loaded.library)
      client.app(item.slug).then(
        (d) => setCatalog((c) => ({ ...c, [item.id]: d.entry })),
        () => {},
      );
  }, [loaded, client]);

  if (!loaded) return null;
  const { config } = loaded;

  const fail = (id: string, error: unknown) =>
    setJobs((j) => ({ ...j, [id]: { error: error instanceof Error ? error.message : String(error) } }));

  async function installEntry(entry: Entry) {
    setJobs((j) => ({ ...j, [entry.id]: { id: entry.id, phase: "downloading", received: 0, total: null } }));
    try {
      const release = (await client.releases(entry.slug).catch(() => {
        throw new Error("Couldn't reach quiverlauncher.com. Check your connection and try again.");
      })).items[0];
      if (!release) throw new Error("This app has no approved release yet.");
      const settings = release.installationOverride ?? entry.launcher;
      let [asset, ...others] = bestAssets(release.assets, config.os, config.arch, settings.releaseAssetFilter);
      if (!asset) throw new Error(`This release has no download for your computer.`);
      if (others.length) {
        const picked = await new Promise<Asset | null>((resolve) =>
          setChoice({ entry, version: release.version, assets: [asset, ...others], resolve }),
        );
        setChoice(null);
        if (!picked) {
          setJobs(({ [entry.id]: _, ...rest }) => rest);
          return;
        }
        asset = picked;
      }
      const folder = settings.folderName?.trim() || entry.slug;
      await native.install({
        id: entry.id,
        url: asset.url,
        filename: asset.filename,
        checksum: asset.checksum,
        folder,
        filesToAdd: settings.filesToAdd ?? [],
        version: release.version,
      });
      const executables = settings.preferredExecutables?.[config.os as "windows" | "linux" | "macos"] ?? [];
      setInstalls((i) => ({ ...i, [entry.id]: { version: release.version, folder, executables } }));
      setJobs(({ [entry.id]: _, ...rest }) => rest);
    } catch (error) {
      fail(entry.id, error);
    }
  }

  const value: Launcher = {
    config,
    client,
    library,
    installs,
    catalog,
    jobs,
    choice,
    remember: (entries) => {
      const known = new Set(library.map((i) => i.id));
      const fresh = entries.filter((e) => known.has(e.id));
      if (fresh.length) setCatalog((c) => ({ ...c, ...Object.fromEntries(fresh.map((e) => [e.id, e])) }));
    },
    async get(entry) {
      if (jobs[entry.id] && !("error" in jobs[entry.id])) return;
      setCatalog((c) => ({ ...c, [entry.id]: entry }));
      setLibrary((l) => (l.some((i) => i.id === entry.id) ? l : [...l, { id: entry.id, slug: entry.slug, addedAt: Date.now() }]));
      await installEntry(entry);
    },
    async play(id) {
      const install = installs[id];
      if (install) await native.launch(install.folder, install.executables).catch((e) => fail(id, e));
    },
    async remove(id) {
      const install = installs[id];
      if (install) await native.uninstall(install.folder).catch(() => {});
      setInstalls(({ [id]: _, ...rest }) => rest);
      setLibrary((l) => l.filter((i) => i.id !== id));
      setJobs(({ [id]: _, ...rest }) => rest);
    },
    dismiss: (id) => setJobs(({ [id]: _, ...rest }) => rest),
  };
  return <LauncherContext.Provider value={value}>{children}</LauncherContext.Provider>;
}

function usePersist(name: "library" | "installs" | "catalog", value: unknown, initial: unknown) {
  const first = useRef(true);
  useEffect(() => {
    if (initial === undefined) return;
    if (first.current) {
      first.current = false;
      return;
    }
    native.writeState(name, value);
  }, [name, value, initial]);
}
