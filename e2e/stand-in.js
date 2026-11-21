/**
 * The launcher's Rust side, stood in for in the page (before it starts) when only its web side runs: phone.js's
 * screens and web/'s controller tests. It answers native.ts's calls with what's in `state`, the saved files (library,
 * installs, settings…), kept in memory; and carries events (listen and emit) between the page and a test, in the order
 * sent, as Tauri's event plugin does. `config` changes the computer it says it is (phone.js's says Android). Anything else (set_theme, log_error…) does nothing, as there's nothing to see of it.
 *
 * Passed to Playwright's addInitScript, so it can use nothing from outside itself but its argument.
 */
export function standInForRust({ api, state = {}, config: more = {}, maximized = false }) {
  // The first start's setup (setup.tsx) is taken as done, unless a test's settings say `setupDone: false`; how apps
  // update (app-updates.tsx) as answered, so its banner isn't over every test.
  const files = { library: [], installs: {}, catalog: {}, collections: [], ...state, settings: { setupDone: true, appUpdatesAsked: true, ...state.settings } };
  const config = {
    api,
    // Nothing listens at either: no account, and no usage data sent anywhere.
    convex: "http://127.0.0.1:1/convex",
    usageHost: "http://127.0.0.1:1/usage",
    githubApi: "https://api.github.com",
    gitlabApi: "https://gitlab.com/api/v4",
    returnTo: "",
    os: "linux",
    arch: "x64",
    appsDir: "/home/player/Games/Quiver",
    cacheDir: "/home/player/.local/share/quiver/cache",
    version: "4.0.0",
    home: "/home/player",
    user: null,
    ...more,
  };
  let next = 0;
  const callbacks = new Map();
  // Each event's listeners: their callbacks, by the listener's id.
  const listeners = new Map();
  // Pages the launcher asked the system's browser to open, and every call to the Rust side but its events'.
  window.__opened = [];
  window.__calls = [];
  // Downloads under way, by app: `resolve({})` finishes one, as installed.
  window.__installs = new Map();
  // The download cache (cache.rs): what tests put in it, by hash, and what was fetched into it.
  window.__cache = { files: [], fetched: [] };
  const answers = {
    open_url: ({ url }) => void window.__opened.push(url),
    cache_status: () => ({ files: window.__cache.files.length, bytes: window.__cache.files.reduce((n, f) => n + f.size, 0), entries: window.__cache.files }),
    cache_has: ({ checksums }) => checksums.map((c) => window.__cache.files.some((f) => c.endsWith(f.hash))),
    // A test can make a fetch fail or wait (`window.__cache.onFetch`), as a blocked file or a slow download would.
    cache_fetch: async ({ request }) => {
      window.__cache.fetched.push(request);
      await window.__cache.onFetch?.(request, window.__cache.fetched.length);
    },
    cache_remove: ({ hashes }) => void (window.__cache.files = hashes ? window.__cache.files.filter((f) => !hashes.includes(f.hash)) : []),
    cache_clean: () => 0,
    // A README kept for an app (window.__cache.readmes, by app id), and those kept since.
    cache_readme_save: ({ app, readme, images }) => void (window.__cache.saved ??= []).push({ app, readme, images }) || true,
    cache_readme_read: ({ app }) => window.__cache.readmes?.[app] ?? null,
    cache_open_app: ({ app }) => void window.__opened.push(`cache:${app}`),
    config: () => config,
    // The computer's PC specs (specs.rs), unless a test's config says otherwise (`pcSpecs`).
    pc_specs: () => more.pcSpecs ?? { cpu: "AMD Ryzen 5 5600", gpu: "AMD Radeon RX 6600", ramGb: 16 },
    // Every apps folder is there, with room to spare.
    apps_folder_states: ({ paths }) => paths.map(() => ({ exists: true, free: 500e9 })),
    move_app: ({ to, folder }) => ({ dir: [to, folder].join("\\"), left: null }),
    // A move of apps into an apps folder: there's room, and every one of them gets there.
    apps_folder_refusal: () => null,
    move_apps_folder: ({ to, apps }) => ({ appsDir: to, inside: apps.map((a) => a.id), left: [], failed: [] }),
    read_state: ({ name }) => files[name] ?? null,
    write_state: ({ name, value }) => void (files[name] = value),
    // Damaged files, if `state` says so (stateProblems), or the Rust side not answering (stateFailure).
    state_problems: () => {
      if (files.stateFailure) throw files.stateFailure;
      return files.stateProblems ?? [];
    },
    list_snapshots: () => [],
    // A Quiver Launcher 3 library found by itself, and its apps folders, if `state` says so (v3Library, v3AppsFolders).
    find_v3_library: () => files.v3Library ?? null,
    v3_apps_folders: () => files.v3AppsFolders ?? [],
    controllers: () => [],
    list_protons: () => [],
    automatic_runner: () => null,
    installed_mods: () => [],
    mod_packages: () => [],
    // A newer Quiver Launcher, if `state` says so (launcherUpdate).
    check_update: () => files.launcherUpdate ?? null,
    // The library's background: what the picker answers (pickBackground), and a picture for any file but a missing one.
    pick_background: () => files.pickBackground ?? null,
    // A picture for an app: the copy's name, or a refusal when a test says so.
    pick_artwork: ({ slot }) => {
      if (files.artworkProblem) throw files.artworkProblem;
      return files.pickArtwork ? `${slot}-1.png` : null;
    },
    // A picture brought over from Quiver Launcher 3: the copy's name, for its kind.
    import_artwork: ({ slot }) => `${slot}-v3.png`,
    read_background: ({ path }) => {
      if (path.includes("missing")) throw new Error("os error 2");
      const png = "iVBORw0KGgoAAAANSUhEUgAAABAAAAAJCAIAAAC0SDtlAAAAFklEQVR4nGP8z8DAwMDAxMDAwMAAAAwuAQ8pwYnLAAAAAElFTkSuQmCC";
      return Uint8Array.from(atob(png), (c) => c.charCodeAt(0)).buffer;
    },
    // A download that stays under way till a test finishes it (window.__installs) or Cancel stops it: what an install
    // does is the real app's to show (the tests one folder up).
    install: ({ request }) => {
      (window.__installRequests ??= []).push(request);
      return new Promise((resolve, reject) => window.__installs.set(request.id, { resolve, reject }));
    },
    cancel_install: ({ id }) => {
      window.__installs.get(id)?.reject("cancelled");
      window.__installs.delete(id);
    },
    // An app's saves (saves-tab.tsx): none known and no backups; making and restoring them is the real app's to show.
    saves_info: () => ({ places: [], from: "none", backups: [] }),
    saves_choose: () => ({ places: [], from: "none", backups: [] }),
    saves_to_report: () => ({ os: "linux", places: [] }),
    saves_restore_plan: () => [],
    // The keychain: a test's own `secrets`, such as a GitHub token; none else.
    secret_get: ({ key }) => files.secrets?.[key] ?? null,
    // GitLab's request limit, as the real Rust side reads it: the page can't see these headers itself.
    gitlab_limit: async ({ url, token }) => {
      const response = await fetch(url, { headers: token ? { "PRIVATE-TOKEN": token } : {} });
      const header = (name) => {
        const value = response.headers.get(name);
        return value == null ? null : Number(value);
      };
      return {
        status: response.status,
        limit: header("ratelimit-limit"),
        remaining: header("ratelimit-remaining"),
        reset: header("ratelimit-reset"),
        retryAfter: header("retry-after"),
      };
    },
    // A repository host's API answer, as the real Rust side fetches it for a host without CORS.
    provider_http: async ({ request }) => {
      const response = await fetch(request.url, { headers: request.headers });
      const headers = {};
      response.headers.forEach((value, name) => {
        if (/^(content-type|etag|last-modified|link|retry-after|x-ratelimit-|ratelimit-)/i.test(name)) headers[name] = value;
      });
      return { status: response.status, headers, body: await response.text() };
    },
    // The player's own copy of a game (own-copy-setup.tsx): none found, and one set up stays where it was put.
    find_copies: () => files.foundCopies ?? [],
    copy_present: () => true,
    "plugin:event|listen": ({ event, handler }) => {
      const id = ++next;
      if (!listeners.has(event)) listeners.set(event, new Map());
      listeners.get(event).set(id, handler);
      return id;
    },
    "plugin:event|unlisten": ({ event, eventId }) => void listeners.get(event)?.delete(eventId),
    // To every listener, as Tauri does: after the call returns, in the order sent.
    "plugin:event|emit": ({ event, payload }) => {
      const now = [...(listeners.get(event) ?? [])];
      setTimeout(() => now.forEach(([id, handler]) => callbacks.get(handler)?.({ event, id, payload })));
    },
    "plugin:window|is_maximized": () => maximized,
    "plugin:window|is_fullscreen": () => false,
  };
  window.__TAURI_INTERNALS__ = {
    metadata: { currentWindow: { label: "main" }, currentWebview: { windowLabel: "main", label: "main" } },
    transformCallback: (callback) => {
      const id = ++next;
      callbacks.set(id, callback);
      return id;
    },
    unregisterCallback: (id) => void callbacks.delete(id),
    // The player's kept pictures (custom-art.tsx): a picture for any name.
    convertFileSrc: (path, protocol) => (protocol === "quiverart" ? "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABAAAAAJCAIAAAC0SDtlAAAAFklEQVR4nGP8z8DAwMDAxMDAwMAAAAwuAQ8pwYnLAAAAAElFTkSuQmCC" : path),
    invoke: async (cmd, args) => {
      if (!cmd.startsWith("plugin:event|")) window.__calls.push({ cmd, args });
      return (await answers[cmd]?.(args ?? {})) ?? null;
    },
  };
  window.__TAURI_EVENT_PLUGIN_INTERNALS__ = { unregisterListener: (event, id) => void listeners.get(event)?.delete(id) };
  // What the files hold now, for a test to read (the page's own copy: the launcher keeps them).
  window.__files = files;
}
