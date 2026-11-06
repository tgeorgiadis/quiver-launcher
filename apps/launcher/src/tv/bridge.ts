/**
 * The only way Big Picture reaches upstream (spec §4): a view of the store's
 * library, installs, jobs and catalog as one list of apps, and its actions
 * (get, play, remove) behind a queue that installs one app at a time, so two
 * installs never both wait on the store's single release-file choice.
 */
import type { LibraryArt, ProjectType } from "@quiverlauncher/api";
import { hasUpdate, type CatalogEntry, type Install, type Job, type LibraryItem } from "../store";

export type TvState = "notInstalled" | "queued" | "downloading" | "installed" | "updateAvailable" | "updating" | "error";

export type TvApp = {
  /** The catalog entry's id (or "github:…" / "local:…"): the key for the sidecar and focus memory. */
  id: string;
  slug: string;
  /** What the library card shows: the project's name, or the player's own name for it. */
  title: string;
  /** The game it plays, such as "The Legend of Zelda: Majora's Mask". */
  gameTitle: string;
  entry: CatalogEntry;
  inLibrary: boolean;
  state: TvState;
  installedVersion?: string;
  latestVersion?: string;
  /** 0–1 while downloading with a known size; null otherwise. */
  progress: number | null;
  phase?: "downloading" | "installing";
  error?: string;
  /** Upstream's per-app update choice: undefined asks. */
  updates?: Install["updates"];
  lastPlayed?: number;
  addedAt?: number;
  consoles: string[];
  basedOn?: string;
  projectType: ProjectType;
  tags: string[];
  art: LibraryArt & { icon?: string };
  /** Got from Discover during this visit and now installed: shown greyed with a tick until Discover is left. */
  fresh?: boolean;
};

export type BridgeInput = {
  library: LibraryItem[];
  installs: Record<string, Install>;
  jobs: Record<string, Job>;
  catalog: Record<string, CatalogEntry>;
  /** Ids waiting in Big Picture's install queue. */
  queued?: ReadonlySet<string>;
};

function stateOf(entry: CatalogEntry, install: Install | undefined, job: Job | undefined, queued: boolean): TvState {
  if (job && "error" in job) return "error";
  if (job) return install ? "updating" : "downloading";
  if (queued) return "queued";
  if (!install) return "notInstalled";
  if (!install.local && hasUpdate(entry, install)) return "updateAvailable";
  return "installed";
}

/** One app as Big Picture shows it, with the player's own name and cover over the catalog's (as upstream's library does). */
export function toTvApp(entry: CatalogEntry, input: BridgeInput, item?: LibraryItem): TvApp {
  const install = input.installs[entry.id];
  const job = input.jobs[entry.id];
  const cover = item?.overrides?.cover;
  const art = { ...entry.libraryArt, ...(cover ? { header: cover, hero: cover } : {}), icon: entry.artwork };
  const progress = job && !("error" in job) && job.total ? Math.min(1, job.received / job.total) : null;
  return {
    id: entry.id,
    slug: entry.slug,
    title: item?.overrides?.name || entry.projectName || entry.name,
    gameTitle: entry.name,
    entry,
    inLibrary: Boolean(item),
    state: stateOf(entry, install, job, input.queued?.has(entry.id) ?? false),
    installedVersion: install?.version,
    latestVersion: entry.verified?.version,
    progress,
    phase: job && !("error" in job) ? job.phase : undefined,
    error: job && "error" in job ? job.error : undefined,
    updates: install?.updates,
    lastPlayed: install?.lastPlayed,
    addedAt: item?.addedAt,
    consoles: entry.consoles ?? [],
    basedOn: entry.basedOn?.console,
    projectType: entry.projectType,
    tags: [...entry.tags, ...(item?.overrides?.tags ?? [])],
    art,
  };
}

/** The library as Big Picture shows it: removed apps and ones whose catalog entry hasn't loaded yet are left out. */
export function libraryApps(input: BridgeInput): TvApp[] {
  return input.library.flatMap((item) => {
    const entry = input.catalog[item.id];
    return !item.removed && entry ? [toTvApp(entry, input, item)] : [];
  });
}

/**
 * Installed (including updating, with an update, or one whose launch or update
 * just failed): what console tabs and All show. A failed launch must not hide
 * the game.
 */
export const isInstalled = (app: TvApp) =>
  app.state === "installed" || app.state === "updateAvailable" || app.state === "updating" || (app.state === "error" && Boolean(app.installedVersion));

/**
 * A download or update has just finished: it was in progress and now isn't. Not while it's still
 * "updating", which counts as installed: noting it then changed the sidecar, which made new apps,
 * which noted it again, until React gave up (reinstalling the installed version froze Big Picture).
 */
export const justInstalled = (before: TvState | undefined, app: TvApp) =>
  (before === "downloading" || before === "updating") && app.state !== "downloading" && app.state !== "updating" && isInstalled(app);

/** `done` holds everyone waiting on this install: who asked first, and anyone who asked again meanwhile. */
/** What the library's tabs show: installed games, and ones in the library downloading for the first time (as Steam does). */
export const inLibraryView = (app: TvApp) => isInstalled(app) || (app.inLibrary && (app.state === "downloading" || app.state === "queued"));

type Task = { id: string; run: () => Promise<void>; done: (() => void)[] };

/** Installs one app at a time, in the order asked. */
export class InstallQueue {
  private waiting: Task[] = [];
  private current: Task | null = null;
  private readonly listeners = new Set<() => void>();
  private ids: ReadonlySet<string> = new Set();

  /**
   * Resolves once the app's install has finished (or failed, or was taken out of the queue).
   * Asking again for one already waiting or installing doesn't install it twice: it waits for that one.
   */
  enqueue(id: string, run: () => Promise<void>): Promise<void> {
    const existing = this.current?.id === id ? this.current : this.waiting.find((t) => t.id === id);
    if (existing) return new Promise<void>((done) => existing.done.push(done));
    return new Promise<void>((done) => {
      this.waiting.push({ id, run, done: [done] });
      this.changed();
      this.next();
    });
  }

  /** Takes an app out of the queue before it starts; one already installing carries on. */
  cancel(id: string): boolean {
    const task = this.waiting.find((t) => t.id === id);
    if (!task) return false;
    this.waiting = this.waiting.filter((t) => t !== task);
    task.done.forEach((done) => done());
    this.changed();
    return true;
  }

  /** Apps waiting their turn (not the one installing). */
  get queued(): ReadonlySet<string> {
    return this.ids;
  }

  get active(): string | null {
    return this.current?.id ?? null;
  }

  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => void this.listeners.delete(listener);
  };

  private next() {
    if (this.current || !this.waiting.length) return;
    const task = this.waiting.shift()!;
    this.current = task;
    this.changed();
    // The store reports failures itself (jobs[id].error); the queue only moves on.
    void task
      .run()
      .catch(() => {})
      .finally(() => {
        this.current = null;
        task.done.forEach((done) => done());
        this.changed();
        this.next();
      });
  }

  private changed() {
    this.ids = new Set(this.waiting.map((t) => t.id));
    for (const listener of this.listeners) listener();
  }
}
