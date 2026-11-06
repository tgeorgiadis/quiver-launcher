/** The bridge over the live store, for Big Picture's views. */
import { useMemo, useRef, useSyncExternalStore } from "react";
import type { AppQuery, Entry, Release } from "@quiverlauncher/api";
import { useLauncher } from "../store";
import { InstallQueue, libraryApps, toTvApp, type BridgeInput } from "./bridge";

/** One queue for the one store. */
const queue = new InstallQueue();

/** A turn of the event loop, so React has applied the last install's changes before the next one reads them. */
const settle = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

export function useBridge() {
  const launcher = useLauncher();
  // Queued installs run later: they must use the store as it is then, not as it was when they were queued.
  const latest = useRef(launcher);
  latest.current = launcher;
  const queued = useSyncExternalStore(queue.subscribe, () => queue.queued);
  const { library, installs, jobs, catalog } = launcher;
  const input: BridgeInput = useMemo(() => ({ library, installs, jobs, catalog, queued }), [library, installs, jobs, catalog, queued]);
  const apps = useMemo(() => libraryApps(input), [input]);

  return {
    apps,
    input,
    config: launcher.config,
    consoles: launcher.consoles,
    consoleNames: launcher.consoleNames,
    /** The release file the player must pick, when several fit. */
    choice: launcher.choice,
    activeInstall: queue.active,
    /** An app from the catalog (Discover) as Big Picture shows it. */
    view: (entry: Entry) => toTvApp(entry, input, library.find((i) => i.id === entry.id && !i.removed)),
    play: (id: string) => launcher.play(id),
    /** Adds the app to the library and installs it (or the given release). */
    install: (entry: Entry, release?: Release) => {
      // In the library at once, so it shows there (and in its console's tab) while it waits its turn.
      launcher.cache([entry]);
      launcher.add(entry);
      return queue.enqueue(entry.id, async () => {
        await settle();
        await latest.current.get(entry, release);
      });
    },
    /** Installs the newest verified release over the installed one. */
    update: (id: string) =>
      queue.enqueue(id, async () => {
        await settle();
        const entry = latest.current.catalog[id];
        if (entry) await latest.current.get(entry);
      }),
    /** Updates and waits for it; resolves to why it failed, or null when it worked. */
    updateAndWait: async (id: string): Promise<string | null> => {
      await queue.enqueue(id, async () => {
        await settle();
        const entry = latest.current.catalog[id];
        if (entry) await latest.current.get(entry);
      });
      await settle();
      const job = latest.current.jobs[id];
      return job && "error" in job ? job.error : null;
    },
    cancelQueued: (id: string) => queue.cancel(id),
    /** The apps of a Quiver Launcher 3 library on this computer, to bring over (the launcher finds it). */
    oldApps: launcher.oldLibrary?.apps ?? [],
    /** Brings it over: installed apps stay where they are. Resolves to the names that couldn't come. */
    importOld: () => (launcher.oldLibrary ? launcher.importOld(launcher.oldLibrary) : Promise.resolve([])),
    /** Uninstalls and removes from the library, as upstream's Remove does. */
    uninstall: (id: string) => launcher.remove(id),
    releases: (slug: string, limit?: number) => launcher.client.releases(slug, limit),
    discover: (query: AppQuery) => launcher.client.apps({ os: launcher.config.os, ...query }),
  };
}

export type Bridge = ReturnType<typeof useBridge>;
