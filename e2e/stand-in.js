/**
 * The launcher's Rust side, stood in for in the page (before it starts) when only its web side runs: phone.js's
 * screens and web/'s controller tests. It answers native.ts's calls with what's in `state`, the saved files (library,
 * installs, settings…), kept in memory; and carries events (listen and emit) between the page and a test, in the order
 * sent, as Tauri's event plugin does. `config` changes the computer it says it is (phone.js's says Android). Anything else (set_theme, log_error…) does nothing, as there's nothing to see of it.
 *
 * Passed to Playwright's addInitScript, so it can use nothing from outside itself but its argument.
 */
export function standInForRust({ api, state = {}, config: more = {}, maximized = false }) {
  // The first start's setup (setup.tsx) is taken as done, unless a test's settings say `setupDone: false`.
  const files = { library: [], installs: {}, catalog: {}, collections: [], ...state, settings: { setupDone: true, ...state.settings } };
  const config = {
    api,
    // Nothing listens at either: no account, and no usage data sent anywhere.
    convex: "http://127.0.0.1:1/convex",
    posthogHost: "http://127.0.0.1:1/posthog",
    githubApi: "https://api.github.com",
    gitlabApi: "https://gitlab.com/api/v4",
    returnTo: "",
    os: "linux",
    arch: "x86_64",
    appsDir: "/home/player/Games/Quiver",
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
  const answers = {
    open_url: ({ url }) => void window.__opened.push(url),
    config: () => config,
    read_state: ({ name }) => files[name] ?? null,
    write_state: ({ name, value }) => void (files[name] = value),
    state_problems: () => [],
    list_snapshots: () => [],
    find_v3_library: () => null,
    controllers: () => [],
    list_protons: () => [],
    automatic_runner: () => null,
    installed_mods: () => [],
    mod_packages: () => [],
    check_update: () => null,
    // A download that stays under way till a test finishes it (window.__installs) or Cancel stops it: what an install
    // does is the real app's to show (the tests one folder up).
    install: ({ request }) => new Promise((resolve, reject) => window.__installs.set(request.id, { resolve, reject })),
    cancel_install: ({ id }) => {
      window.__installs.get(id)?.reject("cancelled");
      window.__installs.delete(id);
    },
    secret_get: () => null,
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
    convertFileSrc: (path) => path,
    invoke: async (cmd, args) => {
      if (!cmd.startsWith("plugin:event|")) window.__calls.push({ cmd, args });
      return (await answers[cmd]?.(args ?? {})) ?? null;
    },
  };
  window.__TAURI_EVENT_PLUGIN_INTERNALS__ = { unregisterListener: (event, id) => void listeners.get(event)?.delete(id) };
  // What the files hold now, for a test to read (the page's own copy: the launcher keeps them).
  window.__files = files;
}
