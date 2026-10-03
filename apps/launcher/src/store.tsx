/**
 * The player's library and what's installed, kept on this device.
 *
 * A library item only points at its catalog entry (plus the player's own
 * overrides); names, artwork and files always come from the catalog. The
 * last catalog data seen is cached so the library shows instantly offline.
 */
import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { createClient, createGithub, createGitlab, githubRepository, type Asset, type Client, type Console, type Entry, type Github, type Release, type Withdrawn } from "@quiver/api";
import { native, type Config, type OldApp, type Progress } from "./native";
import { bestAssets } from "./assets";
import { AccountProvider, useAccount } from "./account";
import { applyServer, applyServerCollections, changesFrom, joinAccount, type Collection } from "./sync";
import { setBindings, type Bindings } from "./spatial";
import { customEntry, customKey, isCustom, type CustomApp } from "./custom";

export type { CustomApp, Collection };

export type LibraryItem = {
  /** The catalog entry's id, or "github:owner/repo" for a custom app. */
  id: string;
  slug: string;
  /** A repository the catalog doesn't list. */
  custom?: CustomApp;
  addedAt: number;
  /** The player's own choices; empty shows the catalog's. */
  overrides?: { name?: string; cover?: string; tags?: string[] };
  /** Changed here and not yet saved to the account. */
  pending?: { added?: boolean; name?: boolean; cover?: boolean; tags?: boolean; removed?: boolean };
  /** The account this item last synced with. */
  account?: string;
  /** Removed here; kept until the removal reaches its account. */
  removed?: boolean;
};
/** Catalog data for a library app, with releases pulled since. */
export type CatalogEntry = Entry & { withdrawn?: Withdrawn[] };
export type Install = {
  /** Set for an install adopted from Quiver Launcher 3, which stays where it was. */
  dir?: string;
  version: string; releasedAt?: number; releaseId?: string; folder: string; executables: string[]; wine?: boolean;
  lastPlayed?: number;
  /** Install new releases without asking, or stay on this one. Unset: offer them. */
  updates?: "auto" | "pinned";
};
export type Job = Progress | { error: string };
/** Several files suit this computer: the player picks one. */
export type Choice = { entry: Entry; version: string; assets: Asset[]; resolve: (asset: Asset | null) => void };

type Launcher = {
  config: Config;
  client: Client;
  github: Github;
  gitlab: ReturnType<typeof createGitlab>;
  /** The catalog's consoles, and their names by id. */
  consoles: Console[];
  consoleNames: Record<string, string>;
  library: LibraryItem[];
  installs: Record<string, Install>;
  catalog: Record<string, CatalogEntry>;
  jobs: Record<string, Job>;
  choice: Choice | null;
  /** Apps in a Quiver Launcher 3 library on this computer, until they're brought over. */
  oldApps: OldApp[];
  importOld: () => Promise<string[]>;
  remember: (entries: Entry[]) => void;
  /** Adds the app to the library and installs it: its newest release, or the one given. */
  get: (entry: Entry, release?: Release) => Promise<void>;
  play: (id: string) => Promise<void>;
  /** Uninstalls the app and takes it out of the library. */
  remove: (id: string) => Promise<void>;
  /** Adds and installs an app from a GitHub repository; opens the catalog's app if it lists it. Resolves to an error. */
  addRepository: (input: string) => Promise<{ error: string } | { entry: Entry }>;
  /** Adds an app without installing it, such as one installed but not in the library. */
  add: (entry: Entry) => void;
  setUpdates: (id: string, updates: Install["updates"]) => void;
  /** Sets the player's own name and artwork; undefined shows the catalog's. */
  customize: (id: string, overrides: { name?: string; cover?: string }) => void;
  /** The player's own tags on an app; hand-picked collections are tags. */
  setTags: (id: string, tags: string[]) => void;
  /** Saved library filters, in order. */
  collections: Collection[];
  /** Adds or changes a collection; `removed` deletes it. */
  saveCollection: (collection: Collection) => void;
  /** Changes made here that the account doesn't have yet. */
  unsynced: number;
  /** Signs out, taking the library along: installs stay, the library starts empty. */
  signOut: () => Promise<void>;
  settings: Settings;
  setSettings: (settings: Settings) => void;
  /** A short note for the player, such as what signing in brought over. */
  notice: string | null;
  setNotice: (notice: string | null) => void;
  dismiss: (id: string) => void;
};

const LauncherContext = createContext<Launcher | null>(null);

export const useLauncher = () => useContext(LauncherContext)!;

/** Whether this computer can install the app. */
export const availableOn = (entry: Entry, os: string) =>
  entry.supportedOS.some((o) => o === os || o === "unknown" || (os === "linux" && o === "windows"));

/** "v1.2" and "1.2" are the same release. */
const bare = (version: string) => version.trim().replace(/^v/i, "");

export function hasUpdate(entry: Entry | undefined, install: Install | undefined) {
  // Only newer releases: a pulled one rolls back through the withdrawn warning instead.
  return Boolean(
    entry?.verified &&
      install &&
      install.updates !== "pinned" &&
      bare(install.version) !== bare(entry.verified.version) &&
      entry.verified.releasedAt > (install.releasedAt ?? 0),
  );
}

/** This computer's preferences; they don't sync. */
export type Settings = {
  fullscreen?: boolean;
  scale?: number;
  /** Apps hidden from the library here. */
  hidden?: string[];
  /** Keyboard and controller bindings; unset uses the defaults. */
  keys?: Bindings;
  pad?: Bindings;
  /** Controllers don't move around the launcher. */
  padOff?: boolean;
  /** The library in sections by original console. */
  byConsole?: boolean;
};

type Saved = {
  settings: Settings;
  config: Config;
  library: LibraryItem[];
  installs: Record<string, Install>;
  catalog: Record<string, CatalogEntry>;
  collections: Collection[];
};

export function LauncherProvider({ children }: { children: ReactNode }) {
  const [saved, setSaved] = useState<Saved | null>(null);
  useEffect(() => {
    Promise.all([
      native.config(),
      native.readState<LibraryItem[]>("library"),
      native.readState<Record<string, Install>>("installs"),
      native.readState<Record<string, CatalogEntry>>("catalog"),
      native.readState<Settings>("settings"),
      native.readState<Collection[]>("collections"),
    ]).then(([config, library, installs, catalog, settings, collections]) =>
      setSaved({ config, library: library ?? [], installs: installs ?? {}, catalog: catalog ?? {}, settings: settings ?? {}, collections: collections ?? [] }),
    );
  }, []);
  if (!saved) return null;
  return (
    <AccountProvider config={saved.config}>
      <LauncherState saved={saved}>{children}</LauncherState>
    </AccountProvider>
  );
}

function LauncherState({ saved, children }: { saved: Saved; children: ReactNode }) {
  const { config } = saved;
  const account = useAccount();
  const [allItems, setLibrary] = useState(saved.library);
  const [installs, setInstalls] = useState(saved.installs);
  const [catalog, setCatalog] = useState(saved.catalog);
  const [jobs, setJobs] = useState<Record<string, Job>>({});
  const [choice, setChoice] = useState<Choice | null>(null);
  const [oldApps, setOldApps] = useState<OldApp[]>([]);
  const [notice, setNotice] = useState<string | null>(null);
  const library = useMemo(() => allItems.filter((i) => !i.removed), [allItems]);
  const client = useMemo(() => createClient(config.api), [config.api]);
  const github = useMemo(() => createGithub(config.githubApi), [config.githubApi]);
  const gitlab = useMemo(() => createGitlab(), []);
  const [consoles, setConsoles] = useState<Console[]>([]);
  useEffect(() => void client.facets().then((f) => setConsoles(f.consoles), () => {}), [client]);
  const consoleNames = useMemo(() => Object.fromEntries(consoles.map((c) => [c.id, c.name])), [consoles]);
  const [allCollections, setCollections] = useState(saved.collections);
  const collections = useMemo(() => allCollections.filter((c) => !c.removed).sort((a, b) => a.order - b.order), [allCollections]);
  usePersist("collections", allCollections);

  useEffect(() => {
    if (!saved.library.length) native.findV3Library().then(setOldApps);
    const off = native.onProgress((p) => setJobs((j) => ({ ...j, [p.id]: p })));
    return () => void off.then((stop) => stop());
  }, [saved]);

  usePersist("library", allItems);
  usePersist("installs", installs);
  usePersist("catalog", catalog);
  const [settings, setSettings] = useState(saved.settings);
  usePersist("settings", settings);
  useEffect(() => void document.documentElement.style.setProperty("zoom", String(settings.scale ?? 1)), [settings.scale]);
  useEffect(() => setBindings(settings), [settings]);

  // Catalog data for library apps: refreshed at start and every so often, fetched for apps another device added.
  const fetched = useRef(new Set<string>());
  // Every half hour, look again for new releases.
  const [round, setRound] = useState(0);
  useEffect(() => {
    const timer = setInterval(() => (fetched.current.clear(), setRound((r) => r + 1)), 30 * 60_000);
    return () => clearInterval(timer);
  }, []);
  useEffect(() => {
    for (const item of library) {
      if (fetched.current.has(item.id)) continue;
      fetched.current.add(item.id);
      const custom = item.custom;
      if (custom) {
        github.releases(custom.repository).then(
          ([latest]) => setCatalog((c) => ({ ...c, [item.id]: customEntry(item.id, custom, latest) })),
          () => setCatalog((c) => (c[item.id] ? c : { ...c, [item.id]: customEntry(item.id, custom) })),
        );
        continue;
      }
      client.app(item.slug).then(
        (d) => setCatalog((c) => ({ ...c, [item.id]: { ...d.entry, withdrawn: d.withdrawn } })),
        () => {},
      );
    }
  }, [library, client, github, round]);

  // Signed in: fold the account's library in, live.
  const userId = account.user?.id;
  useEffect(() => {
    if (!userId || !account.items) return;
    const server = account.items;
    setLibrary((local) => {
      const joined = joinAccount(local, userId);
      const next = applyServer(joined, server, userId);
      const fromDevice = local.filter((i) => !i.account).length;
      const fromAccount = next.filter((i) => !local.some((l) => l.id === i.id)).length;
      if (fromDevice || fromAccount)
        setNotice(`Library synced: ${fromDevice} ${fromDevice === 1 ? "app" : "apps"} from this computer, ${fromAccount} from your account.`);
      return JSON.stringify(next) === JSON.stringify(local) ? local : next;
    });
  }, [userId, account.items]); // eslint-disable-line react-hooks/exhaustive-deps

  // Signed in: save what changed here.
  const pushing = useRef(false);
  useEffect(() => {
    const changes = userId ? changesFrom(allItems, userId) : [];
    if (!userId || !changes.length || pushing.current) return;
    pushing.current = true;
    const sent = new Set(allItems.filter((i) => i.pending && i.account === userId));
    (async () => {
      // Refused changes come back; they can't succeed by retrying, so they count as done too.
      for (let i = 0; i < changes.length; i += 100) await account.save(changes.slice(i, i + 100));
    })()
      .then(() =>
        // Untouched since sending: done. A removal that reached its account is gone.
        setLibrary((l) => l.flatMap((i) => (!sent.has(i) ? [i] : i.removed ? [] : [{ ...i, pending: undefined }]))),
      )
      .catch(() => {})
      .finally(() => (pushing.current = false));
  }, [allItems, userId]); // eslint-disable-line react-hooks/exhaustive-deps

  // Signed in: collections follow the account, and changes made here are saved to it.
  useEffect(() => {
    if (!userId || !account.collections) return;
    const server = account.collections;
    setCollections((local) => {
      const next = applyServerCollections(local, server, userId);
      return JSON.stringify(next) === JSON.stringify(local) ? local : next;
    });
  }, [userId, account.collections]); // eslint-disable-line react-hooks/exhaustive-deps
  const pushingCollections = useRef(false);
  useEffect(() => {
    const changed = allCollections.filter((c) => c.pending && c.account === userId);
    // Until the site has collections, they stay on this device.
    if (!userId || !account.collections || !changed.length || pushingCollections.current) return;
    pushingCollections.current = true;
    const sent = new Set(changed);
    account
      .saveCollections(changed)
      .then(() => setCollections((all) => all.flatMap((c) => (!sent.has(c) ? [c] : c.removed ? [] : [{ ...c, pending: undefined }]))))
      .catch(() => {})
      .finally(() => (pushingCollections.current = false));
  }, [allCollections, userId, account.collections]); // eslint-disable-line react-hooks/exhaustive-deps

  // Apps set to update by themselves.
  useEffect(() => {
    for (const item of library) {
      const entry = catalog[item.id];
      if (entry && installs[item.id]?.updates === "auto" && !jobs[item.id] && hasUpdate(entry, installs[item.id])) void installEntry(entry);
    }
  }, [catalog]); // eslint-disable-line react-hooks/exhaustive-deps

  const fail = (id: string, error: unknown) =>
    setJobs((j) => ({ ...j, [id]: { error: error instanceof Error ? error.message : String(error) } }));

  async function installEntry(entry: Entry, chosen?: Release) {
    setJobs((j) => ({ ...j, [entry.id]: { id: entry.id, phase: "downloading", received: 0, total: null } }));
    try {
      // GitHub doesn't mind the key's lower case.
      const custom = isCustom(entry.id) ? entry.id.slice("github:".length) : null;
      const release =
        chosen ??
        (custom
          ? (await github.releases(custom))[0]
          : (await client.releases(entry.slug).catch(() => {
              throw new Error("Couldn't reach quiverlauncher.com. Check your connection and try again.");
            })).items[0]);
      if (!release) throw new Error(custom ? "This repository has no releases yet." : "This app has no approved release yet.");
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
        dir: installs[entry.id]?.dir,
        filesToAdd: settings.filesToAdd ?? [],
        version: release.version,
      });
      const runs = asset.os === "windows" ? "windows" : (config.os as "windows" | "linux" | "macos");
      const executables = settings.preferredExecutables?.[runs] ?? [];
      // Picking another version keeps the app on it until the player says otherwise.
      const updates = chosen ? "pinned" : installs[entry.id]?.updates;
      setInstalls((i) => ({ ...i, [entry.id]: { dir: i[entry.id]?.dir, lastPlayed: i[entry.id]?.lastPlayed, ...(updates ? { updates } : {}), version: release.version, releasedAt: release.releasedAt, releaseId: release.id, folder, executables, ...(asset.os === "windows" && config.os === "linux" ? { wine: true } : {}) } }));
      setJobs(({ [entry.id]: _, ...rest }) => rest);
    } catch (error) {
      fail(entry.id, error);
    }
  }

  function add(entry: Entry, custom?: CustomApp) {
    setLibrary((l) => {
      const mine = l.find((i) => i.id === entry.id);
      const added = { pending: { ...mine?.pending, added: true }, account: userId ?? mine?.account, removed: undefined };
      if (mine) return mine.removed ? l.map((i) => (i === mine ? { ...i, ...added } : i)) : l;
      return [...l, { id: entry.id, slug: entry.slug, addedAt: Date.now(), ...(custom ? { custom } : {}), ...(userId ? added : {}) }];
    });
  }

  /** Repositories the catalog lists, to send them to the catalog's app instead. */
  async function catalogRepositories() {
    const ids = new Map<string, { id: string; slug: string }>();
    for (let cursor: string | null = null, done = false; !done; ) {
      const page = await client.releaseStatus(cursor);
      for (const s of page.items) if (s.repository) ids.set(`${s.provider}:${s.repository}`.toLowerCase(), s);
      [cursor, done] = [page.nextCursor, page.isDone];
    }
    return ids;
  }

  /** Adds a custom app for a repository; the caller installs it. */
  async function addCustom(repository: string, name: string) {
    const releases = await github.releases(repository);
    const custom: CustomApp = { provider: "github", repository, name };
    const entry = customEntry(customKey(repository), custom, releases[0]);
    setCatalog((c) => ({ ...c, [entry.id]: entry }));
    add(entry, custom);
    return entry;
  }

  const value: Launcher = {
    config,
    client,
    github,
    gitlab,
    consoles,
    consoleNames,
    collections,
    saveCollection: (c) =>
      setCollections((all) => {
        const synced = c.account ?? userId;
        const next = { ...c, account: synced, pending: synced ? true : undefined };
        // Never synced: a removal just deletes it.
        const keep = c.removed && !synced ? [] : [next];
        return all.some((x) => x.key === c.key) ? all.flatMap((x) => (x.key === c.key ? keep : [x])) : [...all, ...keep];
      }),
    setTags: (id, tags) =>
      setLibrary((l) => l.map((i) => (i.id !== id ? i : { ...i, overrides: { ...i.overrides, tags: tags.length ? tags : undefined }, pending: i.account ? { ...i.pending, tags: true } : i.pending }))),
    library,
    notice,
    setNotice,
    add: (entry) => add(entry),
    setUpdates: (id, updates) => setInstalls((i) => (i[id] ? { ...i, [id]: { ...i[id], updates } } : i)),
    async addRepository(input) {
      const repository = githubRepository(input);
      if (!repository) return { error: "Enter a GitHub repository, like owner/name or its github.com address." };
      try {
        const listed = (await catalogRepositories().catch(() => new Map())).get(`github:${repository}`.toLowerCase());
        const detail = listed && (await client.app(listed.slug).catch(() => null));
        if (detail) return { entry: detail.entry };
        const entry = await addCustom(repository, repository.split("/")[1]);
        void installEntry(entry);
        return { entry };
      } catch (e) {
        return { error: e instanceof Error ? e.message : String(e) };
      }
    },
    settings,
    setSettings,
    customize: (id, { name, cover }) =>
      setLibrary((l) =>
        l.map((i) => {
          if (i.id !== id) return i;
          const was = i.overrides ?? {};
          const pending = i.account
            ? { ...i.pending, ...(name !== was.name ? { name: true } : {}), ...(cover !== was.cover ? { cover: true } : {}) }
            : i.pending;
          return { ...i, overrides: { ...was, name, cover }, pending };
        }),
      ),
    unsynced: userId ? changesFrom(allItems, userId).length : 0,
    async signOut() {
      await account.signOut();
      setLibrary([]);
      setCollections([]);
    },
    installs,
    catalog,
    jobs,
    choice,
    remember: (entries) => {
      const known = new Set(library.map((i) => i.id));
      const fresh = entries.filter((e) => known.has(e.id));
      if (fresh.length) setCatalog((c) => ({ ...c, ...Object.fromEntries(fresh.map((e) => [e.id, { ...c[e.id], ...e }])) }));
    },
    async get(entry, release) {
      if (jobs[entry.id] && !("error" in jobs[entry.id])) return;
      setCatalog((c) => ({ ...c, [entry.id]: { ...c[entry.id], ...entry } }));
      add(entry);
      await installEntry(entry, release);
    },
    async play(id) {
      const install = installs[id];
      if (!install) return;
      setInstalls((i) => ({ ...i, [id]: { ...i[id], lastPlayed: Date.now() } }));
      await native.launch(install.folder, install.dir, install.executables, Boolean(install.wine)).catch((e) => fail(id, e));
    },
    async remove(id) {
      const install = installs[id];
      if (install) await native.uninstall(install.folder, install.dir).catch(() => {});
      setInstalls(({ [id]: _, ...rest }) => rest);
      // A synced app is removed from its account too; one that never synced just goes.
      setLibrary((l) =>
        l.flatMap((i) =>
          i.id !== id ? [i] : i.account ? [{ ...i, removed: true, pending: { removed: true } }] : [],
        ),
      );
      setJobs(({ [id]: _, ...rest }) => rest);
    },
    oldApps,
    /** Adds the 3.x apps the catalog lists, keeping their installs; returns the names it couldn't match. */
    async importOld() {
      const ids = await catalogRepositories();
      const missing: string[] = [];
      for (const old of oldApps) {
        const match = old.repository && ids.get(`${old.provider}:${old.repository}`.toLowerCase());
        const detail = match && (await client.app(match.slug).catch(() => null));
        let entry: Entry;
        if (detail) {
          entry = detail.entry;
          setCatalog((c) => ({ ...c, [entry.id]: { ...entry, withdrawn: detail.withdrawn } }));
          add(entry);
        } else if (old.provider === "github" && old.repository && githubRepository(old.repository)) {
          // Not in the catalog: it comes over as a custom app.
          const added = await addCustom(old.repository, old.name).catch(() => null);
          if (!added) {
            missing.push(old.name);
            continue;
          }
          entry = added;
        } else {
          missing.push(old.name);
          continue;
        }
        if (old.dir && old.version) {
          const executables = entry.launcher.preferredExecutables?.[config.os as "windows" | "linux" | "macos"] ?? [];
          const install: Install = { dir: old.dir, version: old.version, folder: entry.launcher.folderName || entry.slug, executables };
          setInstalls((i) => ({ ...i, [entry.id]: install }));
        }
      }
      setOldApps([]);
      return missing;
    },
    dismiss: (id) => setJobs(({ [id]: _, ...rest }) => rest),
  };
  return <LauncherContext.Provider value={value}>{children}</LauncherContext.Provider>;
}

/** Saves a state file whenever it changes after the first render. */
function usePersist(name: "library" | "installs" | "catalog" | "settings" | "collections", value: unknown) {
  const first = useRef(true);
  useEffect(() => {
    if (first.current) first.current = false;
    else native.writeState(name, value);
  }, [name, value]);
}
