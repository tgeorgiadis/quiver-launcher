/**
 * The player's library and what's installed, kept on this device.
 *
 * A library item only points at its catalog entry (plus the player's own
 * overrides); names, artwork and files always come from the catalog. The
 * last catalog data seen is cached so the library shows instantly offline.
 */
import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { createClient, createGithub, createGitlab, githubRepository, parseRepository, type Asset, type Client, type Console, type Entry, type Github, type Release, type Withdrawn } from "@quiver/api";
import { native, type Config, type OldApp, type Progress } from "./native";
import { bestAssets } from "./assets";
import { reasonOf, track } from "./telemetry";
import { AccountProvider, useAccount } from "./account";
import { applyServer, applyServerCollections, changesFrom, hasFilters, joinAccount, type Collection } from "./sync";
import { setBindings, type Bindings } from "./spatial";
import { customEntry, customKey, folderNameFor, isCustom, isLocal, localEntry, localKey, type CustomApp, type GameArt, type LocalApp } from "./custom";
import { createConvexClient } from "./catalog";

export type { CustomApp, Collection, GameArt, LocalApp };

export type LibraryItem = {
  /** The catalog entry's id, "github:owner/repo" or "gitlab:…" for a custom app, "local:…" for a local one. */
  id: string;
  slug: string;
  /** A repository the catalog doesn't list. */
  custom?: CustomApp;
  /** On this computer only: a program already here, or a folder the player fills. Never synced. */
  local?: LocalApp;
  /** A game's artwork and consoles the player matched to an app they added; this device only. */
  art?: GameArt;
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
  /** The player's own files (a local app): never downloaded, updated or deleted. */
  local?: true;
  /** A program the player picked, started exactly where it is. */
  program?: string;
};
/** A repository the catalog doesn't list, before it's added: its latest release and the files for this computer. */
export type RepositoryPreview = { app: CustomApp; latest?: Release; files: Asset[] };
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
  /** Keeps catalog data for apps that aren't in the library, such as those on a copy of a shared playlist. */
  cache: (entries: Entry[]) => void;
  /** Adds the app to the library and installs it: its newest release, or the one given. */
  get: (entry: Entry, release?: Release) => Promise<void>;
  play: (id: string) => Promise<void>;
  /** Uninstalls the app and takes it out of the library. */
  remove: (id: string) => Promise<void>;
  /** A GitHub or GitLab repository: the catalog's app when it lists it, else what adding it would install. */
  lookUpRepository: (input: string) => Promise<{ error: string } | { listed: Entry } | { preview: RepositoryPreview }>;
  /** Adds a repository the catalog doesn't list and installs it. */
  addCustomApp: (app: CustomApp, latest: Release | undefined, art?: GameArt) => Entry;
  /** Adds a program on this computer, or makes a folder in the apps folder to fill; this device only. */
  addLocalApp: (app: { kind: "program"; path: string; name: string } | { kind: "folder"; name: string }, art?: GameArt) => Promise<Entry>;
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
  /** Signs out, taking the library along: installs and apps on this computer only stay. */
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

/** The release a player gets: the newest that isn't a pre-release, else the newest. */
const latestOf = (releases: Release[]) => releases.find((r) => !r.prerelease) ?? releases[0];
/** A custom app's repository from its key, for one not in the library; GitHub and GitLab don't mind its lower case. */
const fromKey = (id: string): CustomApp => {
  const repository = id.slice(id.indexOf(":") + 1);
  return { provider: id.startsWith("gitlab:") ? "gitlab" : "github", repository, name: repository.split("/").at(-1)! };
};

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
  /** Anonymous usage data turned off here (telemetry.ts); signed in, the account's setting wins. */
  telemetryOff?: boolean;
  /** The first-run notice about usage data was answered. */
  telemetryNoticeSeen?: boolean;
  /** Quiver Launcher has started here before. */
  launched?: boolean;
};

/** An app as usage data names it: a catalog app by its slug; one the player added only by where it's from. */
export const appRef = (id: string, slug: string) => ({
  slug: isCustom(id) || isLocal(id) ? null : slug,
  source: isLocal(id) ? "local" : isCustom(id) ? "custom" : "catalog",
});

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
  // The catalog comes from the site's Convex queries, as on the website; REST only for the release status feed.
  const client = useMemo(() => createConvexClient(config.convex, createClient(config.api)), [config.api, config.convex]);
  const github = useMemo(() => createGithub(config.githubApi), [config.githubApi]);
  const gitlab = useMemo(() => createGitlab(config.gitlabApi), [config.gitlabApi]);
  /** A custom app's releases, newest first. */
  const releasesOf = (app: Pick<CustomApp, "provider" | "repository">) =>
    app.provider === "gitlab" ? gitlab.releases(app.repository) : github.releases(app.repository);
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
  useEffect(() => {
    setBindings(settings);
  }, [settings]);

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
      const { custom, local, art } = item;
      // Nothing to look up for a local app.
      if (local) {
        setCatalog((c) => (c[item.id] ? c : { ...c, [item.id]: localEntry(item.id, local, art) }));
        continue;
      }
      if (custom) {
        releasesOf(custom).then(
          (releases) => setCatalog((c) => ({ ...c, [item.id]: customEntry(item.id, custom, latestOf(releases), art) })),
          () => setCatalog((c) => (c[item.id] ? c : { ...c, [item.id]: customEntry(item.id, custom, undefined, art) })),
        );
        continue;
      }
      client.app(item.slug).then(
        (d) => setCatalog((c) => ({ ...c, [item.id]: { ...d.entry, withdrawn: d.withdrawn } })),
        () => {},
      );
    }
  }, [library, client, github, gitlab, round]); // eslint-disable-line react-hooks/exhaustive-deps

  // Signed in: fold the account's library in, live.
  const userId = account.user?.id;
  useEffect(() => {
    if (!userId || !account.items) return;
    const server = account.items;
    setLibrary((local) => {
      const joined = joinAccount(local, userId);
      const next = applyServer(joined, server, userId);
      // Apps on this computer only don't go to the account, so they aren't counted.
      const fromDevice = local.filter((i) => !i.account && !i.local).length;
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

  /** `app` is the custom app's repository, when it isn't in the library yet. */
  async function installEntry(entry: Entry, chosen?: Release, app?: CustomApp) {
    // The player's own files are never downloaded over.
    if (isLocal(entry.id) || installs[entry.id]?.local) return;
    setJobs((j) => ({ ...j, [entry.id]: { id: entry.id, phase: "downloading", received: 0, total: null } }));
    const ref = appRef(entry.id, entry.slug);
    const from = installs[entry.id]?.version;
    let version = chosen?.version ?? entry.verified?.version;
    track("app_install_started", { ...ref, version: version ?? null, update: Boolean(from) });
    try {
      const custom = isCustom(entry.id) ? (app ?? allItems.find((i) => i.id === entry.id)?.custom ?? fromKey(entry.id)) : null;
      const release =
        chosen ??
        (custom
          ? latestOf(await releasesOf(custom))
          : (await client.releases(entry.slug).catch(() => {
              throw new Error("Couldn't reach quiverlauncher.com. Check your connection and try again.");
            })).items[0]);
      if (!release) throw new Error(custom ? "This repository has no releases yet." : "This app has no approved release yet.");
      version = release.version;
      const settings = release.installationOverride ?? entry.launcher;
      let candidates = bestAssets(release.assets, config.os, config.arch, settings.releaseAssetFilter);
      // A file the player picked that a later release names differently: they pick again, even from one.
      const missed = !candidates.length && Boolean(custom && settings.releaseAssetFilter);
      if (missed) candidates = bestAssets(release.assets, config.os, config.arch);
      let [asset, ...others] = candidates;
      if (!asset) throw new Error(`This release has no download for your computer.`);
      if (others.length || missed) {
        const picked = await new Promise<Asset | null>((resolve) =>
          setChoice({ entry, version: release.version, assets: [asset, ...others], resolve }),
        );
        setChoice(null);
        if (!picked) {
          setJobs(({ [entry.id]: _, ...rest }) => rest);
          track("app_install_cancelled", { ...ref, version });
          return;
        }
        asset = picked;
      }
      const folder = settings.folderName?.trim() || entry.slug;
      // One folder, one app: uninstalling either would take the other's files.
      const shared = Object.entries(installs).some(([id, i]) => id !== entry.id && !i.dir && i.folder.toLowerCase() === folder.toLowerCase());
      if (shared) throw new Error(`Another app in your library is in the folder ${folder}, so this one can't go there.`);
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
      if (from) track("app_updated", { ...ref, from, to: release.version, auto: !chosen && installs[entry.id]?.updates === "auto" });
      else track("app_installed", { ...ref, version: release.version });
    } catch (error) {
      fail(entry.id, error);
      track("app_install_failed", { ...ref, version: version ?? null, update: Boolean(from), reason: reasonOf(error) });
    }
  }

  function add(entry: Entry, own: Pick<LibraryItem, "custom" | "local" | "art"> = {}) {
    setLibrary((l) => {
      const mine = l.find((i) => i.id === entry.id);
      const added = { pending: { ...mine?.pending, added: true }, account: userId ?? mine?.account, removed: undefined };
      if (mine) return mine.removed ? l.map((i) => (i === mine ? { ...i, ...added } : i)) : l;
      // A local app stays on this computer, out of the account.
      return [...l, { id: entry.id, slug: entry.slug, addedAt: Date.now(), ...own, ...(userId && !own.local ? added : {}) }];
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
  function addCustom(custom: CustomApp, latest?: Release, art?: GameArt) {
    const entry = customEntry(customKey(custom.provider, custom.repository), custom, latest, art);
    setCatalog((c) => ({ ...c, [entry.id]: entry }));
    add(entry, { custom, art });
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
    saveCollection: (c) => {
      if (!c.removed && !allCollections.some((x) => x.key === c.key))
        track(c.follows ? "playlist_followed" : "playlist_created", c.follows ? undefined : { has_filters: hasFilters(c), apps: c.apps?.length ?? 0 });
      setCollections((all) => {
        const synced = c.account ?? userId;
        const next = { ...c, account: synced, pending: synced ? true : undefined };
        // Never synced: a removal just deletes it.
        const keep = c.removed && !synced ? [] : [next];
        return all.some((x) => x.key === c.key) ? all.flatMap((x) => (x.key === c.key ? keep : [x])) : [...all, ...keep];
      });
    },
    setTags: (id, tags) =>
      setLibrary((l) => l.map((i) => (i.id !== id ? i : { ...i, overrides: { ...i.overrides, tags: tags.length ? tags : undefined }, pending: i.account ? { ...i.pending, tags: true } : i.pending }))),
    library,
    notice,
    setNotice,
    add: (entry) => add(entry),
    setUpdates: (id, updates) => setInstalls((i) => (i[id] ? { ...i, [id]: { ...i[id], updates } } : i)),
    async lookUpRepository(input) {
      const repository = parseRepository(input);
      if (!repository) return { error: "Enter a GitHub or GitLab repository: owner/name, or its github.com or gitlab.com address." };
      // Already added: its page.
      const key = customKey(repository.provider, repository.repository);
      if (library.some((i) => i.id === key) && catalog[key]) return { listed: catalog[key] };
      try {
        const listed = (await catalogRepositories().catch(() => new Map())).get(`${repository.provider}:${repository.repository}`.toLowerCase());
        const detail = listed && (await client.app(listed.slug).catch(() => null));
        if (detail) return { listed: detail.entry };
        const latest = latestOf(await releasesOf(repository));
        const app: CustomApp = { ...repository, name: repository.repository.split("/").at(-1)! };
        return { preview: { app, latest, files: latest ? bestAssets(latest.assets, config.os, config.arch) : [] } };
      } catch (e) {
        return { error: e instanceof Error ? e.message : String(e) };
      }
    },
    addCustomApp(app, latest, art) {
      const entry = addCustom(app, latest, art);
      track("app_added", { kind: "repository", host: app.provider });
      void installEntry(entry, undefined, app);
      return entry;
    },
    async addLocalApp(app, art) {
      const id = localKey();
      let local: LocalApp;
      let install: Install;
      if (app.kind === "program") {
        // It starts where it is, from its own folder.
        const cut = Math.max(app.path.lastIndexOf("/"), app.path.lastIndexOf("\\")) + 1;
        const file = app.path.slice(cut);
        local = { kind: "program", path: app.path, name: app.name };
        // A Windows program on Linux runs through Wine.
        const wine = config.os === "linux" && /\.(exe|bat|cmd)$/i.test(file);
        install = { local: true, version: "", dir: app.path.slice(0, cut), folder: file, executables: [], program: app.path, ...(wine ? { wine } : {}) };
      } else {
        const folder = await native.createAppFolder(folderNameFor(app.name));
        local = { kind: "folder", folder, name: app.name };
        install = { local: true, version: "", folder, executables: [] };
      }
      const entry = localEntry(id, local, art);
      setCatalog((c) => ({ ...c, [id]: entry }));
      add(entry, { local, art });
      setInstalls((i) => ({ ...i, [id]: install }));
      track("app_added", { kind: app.kind, host: null });
      return entry;
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
      track("signed_out");
      await account.signOut();
      setLibrary((l) => l.filter((i) => i.local));
      setCollections([]);
    },
    installs,
    catalog,
    jobs,
    choice,
    cache: (entries) => setCatalog((c) => ({ ...c, ...Object.fromEntries(entries.map((e) => [e.id, { ...c[e.id], ...e }])) })),
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
      const ref = appRef(id, catalog[id]?.slug ?? allItems.find((i) => i.id === id)?.slug ?? "");
      await native.launch(install.folder, install.dir, install.executables, Boolean(install.wine), install.program).then(
        () => track("app_launched", ref),
        (e) => (fail(id, e), track("app_launch_failed", { ...ref, reason: reasonOf(e) })),
      );
    },
    async remove(id) {
      const install = installs[id];
      // A local app's files are the player's own: it only leaves the library.
      if (install && !install.local) {
        await native.uninstall(install.folder, install.dir).catch(() => {});
        track("app_uninstalled", appRef(id, catalog[id]?.slug ?? allItems.find((i) => i.id === id)?.slug ?? ""));
      }
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
        } else if (old.repository && ((old.provider === "github" && githubRepository(old.repository)) || old.provider === "gitlab")) {
          // Not in the catalog: it comes over as a custom app.
          const custom: CustomApp = { provider: old.provider, repository: old.repository, name: old.name };
          const releases = await releasesOf(custom).catch(() => null);
          if (!releases) {
            missing.push(old.name);
            continue;
          }
          entry = addCustom(custom, latestOf(releases));
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
