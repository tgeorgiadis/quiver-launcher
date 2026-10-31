/**
 * A stand-in for quiverlauncher.com (its catalog API, answered from the same
 * queries the site runs) and GitHub's release files, so journeys run offline
 * and the same way every time.
 */
import { createServer } from "node:http";
import { minisignKey } from "./minisign.js";
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { gunzipSync, zstdCompressSync } from "node:zlib";

/** A zip with a wrapper folder and a program that leaves a mark when run. */
function buildZip() {
  const dir = mkdtempSync(join(tmpdir(), "quiver-zip-"));
  const zip = join(dir, "port.zip");
  // PYTHON for Windows, where python3 is usually the Microsoft Store's placeholder.
  execFileSync(process.env.PYTHON ?? "python3", [
    "-c",
    `import zipfile,sys
z=zipfile.ZipFile(sys.argv[1],"w")
i=zipfile.ZipInfo("TestPort-v1.0.0/port.sh"); i.external_attr=0o755<<16
z.writestr(i,"#!/bin/sh\\ncd \\"$(dirname \\"$0\\")\\" && echo played > launched.txt\\n")
z.writestr("TestPort-v1.0.0/data/level1.dat","level")
z.close()`,
    zip,
  ]);
  return readFileSync(zip);
}

/** A zip of `files` (name → text), for mods. */
function zipOf(files) {
  const dir = mkdtempSync(join(tmpdir(), "quiver-zip-"));
  const zip = join(dir, "mod.zip");
  execFileSync(process.env.PYTHON ?? "python3", [
    "-c",
    `import zipfile,sys,json
z=zipfile.ZipFile(sys.argv[1],"w")
for name,body in json.loads(sys.argv[2]).items(): z.writestr(name,body)
z.close()`,
    zip,
    JSON.stringify(files),
  ]);
  return readFileSync(zip);
}

/** A stand-in Flatpak bundle: what matters is the ref it carries, as real bundles do. */
const flatpakBundle = (id) => Buffer.concat([Buffer.from("flatpak\0\x01"), Buffer.from(`ref\0app/${id}/x86_64/stable\0`), Buffer.alloc(64)]);

/** A zip holding one Flatpak bundle, as some releases ship them. */
function zipped(name, bytes) {
  const dir = mkdtempSync(join(tmpdir(), "quiver-zip-"));
  const zip = join(dir, "bundle.zip");
  execFileSync(process.env.PYTHON ?? "python3", ["-c", "import zipfile,sys\nz=zipfile.ZipFile(sys.argv[1],'w')\nz.writestr(sys.argv[2],open(sys.argv[3],'rb').read())\nz.close()", zip, name, writeTemp(dir, bytes)]);
  return readFileSync(zip);
}
const writeTemp = (dir, bytes) => {
  const path = join(dir, "bundle");
  writeFileSync(path, bytes);
  return path;
};

/**
 * The same program and data as a tarball packed each way a release might
 * pack one (.tar.xz, .tar.bz2, .tar.zst), by file name.
 */
function buildTarballs() {
  const dir = mkdtempSync(join(tmpdir(), "quiver-tar-"));
  execFileSync(process.env.PYTHON ?? "python3", [
    "-c",
    `import io,tarfile,sys
for mode,name in (("w","packed.tar"),("w:xz","packed.tar.xz"),("w:bz2","packed.tar.bz2")):
  t=tarfile.open(sys.argv[1]+"/"+name,mode)
  for path,body,perm in (("Packed-v1.0/port.sh",b"#!/bin/sh\\ncd \\"$(dirname \\"$0\\")\\" && echo played > launched.txt\\n",0o755),("Packed-v1.0/data/level1.dat",b"level",0o644)):
    i=tarfile.TarInfo(path); i.size=len(body); i.mode=perm; t.addfile(i,io.BytesIO(body))
  t.close()`,
    dir,
  ]);
  const plain = readFileSync(join(dir, "packed.tar"));
  return { xz: readFileSync(join(dir, "packed.tar.xz")), bz2: readFileSync(join(dir, "packed.tar.bz2")), zst: zstdCompressSync(plain) };
}

/** A 1×1 PNG, for any artwork. */
const PIXEL = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==", "base64");

const entry = (slug, name, overrides = {}) => ({
  id: `entry_${slug}`,
  slug,
  name,
  projectName: name,
  description: `${name} is a test port.`,
  games: [{ id: `game_${slug}`, slug, title: `${name} (original)` }],
  tags: ["port"],
  launcher: { folderName: slug, filesToAdd: ["portable.txt"] },
  projectType: "port",
  supportedOS: ["linux", "windows"],
  recommended: 3,
  reviewCount: 3,
  reportIssues: 0,
  reportBroken: 0,
  addedAt: Date.now(),
  verified: { version: "1.0.0", releasedAt: Date.now(), prerelease: false, pinned: true },
  ...overrides,
});

/** PostHog's request bodies: JSON, gzipped JSON, or base64 form data (a page closing). */
function decodePosthog(raw, url) {
  const zipped = url.searchParams.get("compression") === "gzip-js" || (raw[0] === 0x1f && raw[1] === 0x8b);
  const text = (zipped ? gunzipSync(raw) : raw).toString("utf8");
  const form = text.startsWith("data=") && new URLSearchParams(text).get("data");
  return form ? Buffer.from(form, "base64").toString("utf8") : text;
}

/** `pageSize` caps how many apps a catalog page has, to try paging. */
export async function startMockApi({ pageSize = Infinity } = {}) {
  const zip = buildZip();
  const sha = createHash("sha256").update(zip).digest("hex");
  const tarballs = buildTarballs();
  const digest = (bytes) => `sha256:${createHash("sha256").update(bytes).digest("hex")}`;
  const entries = [
    entry("test-port", "Test Port", {
      consoles: ["n64"],
      launcher: {
        folderName: "test-port",
        filesToAdd: ["portable.txt"],
        mods: {
          path: "mods",
          sources: [
            { provider: "thunderstore", sourceUrl: "https://thunderstore.io/c/test-port/" },
            { provider: "gamebanana", sourceUrl: "https://gamebanana.com/mods/games/9001" },
          ],
        },
      },
    }),
    entry("tampered-port", "Tampered Port", { consoles: ["snes"], tags: ["snes", "port"] }),
    // A second way to play Test Port's game, which players say doesn't run.
    entry("test-remake", "Test Remake", {
      consoles: ["n64"],
      games: [{ id: "game_test-port", slug: "test-port", title: "Test Port (original)" }],
      recommended: 0,
      reviewCount: 1,
      reportBroken: 1,
      aiLevel: "generated",
    }),
  ];
  let base = "";
  let lastQuery = new URLSearchParams();
  const catalogQueries = [];
  const downloads = [];
  const downloadProblems = [];
  // While set, downloads stop halfway until it resolves; `stalls` are the ones waiting.
  let stalled = null;
  let unstall = () => {};
  const stalls = new Set();
  // Usage data sent to the stand-in for PostHog (QUIVER_POSTHOG_HOST): events, and every request body as sent.
  const telemetry = [];
  const telemetryBodies = [];
  // The original games, and their apps in the site's (unsorted) order.
  const games = () => ({
    "test-port": {
      game: {
        id: "game_test-port",
        slug: "test-port",
        title: "Test Port (original)",
        description: "The 1996 original.\nSecond line.",
        artwork: `${base}/art/icon.png`,
        libraryArt: { capsule: `${base}/art/capsule.png`, hero: `${base}/art/hero.png` },
        originalSystems: ["n64"],
      },
      art: `${base}/art/capsule.png`,
      apps: ["test-remake", "test-port"],
    },
    "tampered-port": {
      game: { id: "game_tampered-port", slug: "tampered-port", title: "Tampered Port (original)", description: "", originalSystems: ["snes"] },
      apps: ["tampered-port"],
    },
    // Answered malformed on purpose, to see a page fail.
    "broken-game": {
      game: { id: "game_broken-game", slug: "broken-game", title: "Broken Game (original)", description: "", originalSystems: [] },
      apps: ["test-port"],
      malformed: true,
    },
  });
  /** catalog.ts on the site: every typed word starts a word of the title. */
  const words = (text) =>
    text
      .normalize("NFKD")
      .replace(/[̀-ͯ]/g, "")
      .toLowerCase()
      .split(/[^a-z0-9]+/)
      .filter(Boolean);
  const titleMatches = (title, search) => words(search).every((typed) => words(title).some((word) => word.startsWith(typed)));
  const BRANDS = ["Nintendo", "PlayStation", "Xbox", "Sega", "OtherPlatforms"];
  /** A page from `cursor` (an index here; opaque on the site). */
  const page = (items, { numItems, cursor }) => {
    const start = Number(cursor ?? 0);
    const end = start + Math.min(numItems, pageSize);
    return { page: items.slice(start, end), continueCursor: String(Math.min(end, items.length)), isDone: end >= items.length };
  };
  // The site's argument validators: an unknown, null or out-of-range argument is refused, as Convex does.
  const OS = ["windows", "linux", "macos", "android", "ios"];
  const ARGS = {
    "catalog:list": {
      paginationOpts: "page!", search: "string", os: OS, projectType: ["port", "tool", "emulator", "game"], projectTypes: "array", console: "string",
      maker: BRANDS, sort: ["name", "updated", "added", "rating"], ai: ["no-generated", "no-ai"], developer: "string", hideDevelopers: "array",
    },
    "catalog:detail": { slug: "string!" },
    "catalog:readme": { slug: "string!" },
    "catalog:releases": { slug: "string!", paginationOpts: "page!" },
    "catalog:unverifiedReleases": { slug: "string!" },
    "catalog:releaseHistory": { slug: "string!", paginationOpts: "page!" },
    "catalog:facets": {},
    "catalog:matchingGames": { search: "string!", hideDevelopers: "array" },
    "catalog:game": { slug: "string!" },
    "reviews:list": { slug: "string!", paginationOpts: "page!" },
    "sharedLists:get": { slug: "string!" },
  };
  function validate(path, args) {
    const rules = ARGS[path];
    for (const [key, rule] of Object.entries(rules))
      if (typeof rule === "string" && rule.endsWith("!") && !(key in args)) throw new Error(`ArgumentValidationError: ${key} is required`);
    for (const [key, value] of Object.entries(args)) {
      const rule = (typeof rules[key] === "string" ? rules[key].replace("!", "") : rules[key]) ?? "unknown";
      const ok =
        rule === "string" ? typeof value === "string"
        : rule === "array" ? Array.isArray(value)
        : rule === "page" ? Number.isInteger(value?.numItems) && value.numItems >= 1 && value.numItems <= 100 && (value.cursor === null || typeof value.cursor === "string")
        : Array.isArray(rule) && rule.includes(value);
      if (!ok) throw new Error(`ArgumentValidationError: ${key} = ${JSON.stringify(value)}`);
    }
  }
  /** The site's public catalog queries, which its REST routes answer from (convex/http.ts). */
  const site = {
    "catalog:list": (a) => {
      // As URL parameters, so tests read the last listing's filters the same way as before.
      lastQuery = new URLSearchParams(Object.entries(a).filter(([k, v]) => k !== "paginationOpts" && v !== undefined).map(([k, v]) => [k, String(v)]));
      const search = (a.search ?? "").toLowerCase();
      const items = entries.filter((e) => e.name.toLowerCase().includes(search) && (!a.console || e.consoles.includes(a.console)));
      return page(items.map(withVersion), a.paginationOpts);
    },
    "catalog:detail": ({ slug }) => {
      const found = entries.find((e) => e.slug === slug);
      if (!found) return null;
      const game = games()[found.games[0]?.slug]?.game ?? null;
      return {
        entry: { ...withVersion(found), developer: { key: "quiver-tester", name: "quiver-tester" } },
        project: {
          name: found.name,
          description: "",
          provider: "github",
          repository: `quiver/${slug}`,
          author: "Quiver Tester",
          aiUse: { level: "none", source: "signals", checkedAt: Date.UTC(2026, 8, 1), evidence: [{ kind: "check", detail: "No sign of AI use." }] },
        },
        game,
        games: game ? [game] : [],
        withdrawn: [],
        // Test Port has videos for its Media tab, in the order staff set.
        media:
          slug === "test-port"
            ? [
                { youtubeId: "aqz-KE-bpKQ", kind: "trailer", title: "Announcement trailer" },
                { youtubeId: "jNQXAC9IVRw", kind: "gameplay", title: "First level at 60 fps", start: 90 },
                { youtubeId: "M7lc1UVf-VE", kind: "gameplay" },
              ]
            : [],
        // Test Remake has a newer release waiting for a maintainer.
        ...(slug === "test-remake"
          ? {
              checking: {
                version: "v1.1.0", releasedAt: Date.UTC(2026, 9, 1), prerelease: false, firstSeenAt: Date.UTC(2026, 9, 1), needsReview: true,
                reasons: ["It changes how the app is built."], earlyAccess: false, upstreamUrl: "https://github.com/quiver/test-remake/releases/tag/v1.1.0",
              },
            }
          : {}),
      };
    },
    "catalog:readme": ({ slug }) =>
      slug === "test-port"
        ? { markdown: "# Test Port\n\nA **test** port. See [the guide](docs/guide.md).", rawBase: "https://raw.example/", htmlBase: "https://github.com/quiver/test-port/blob/HEAD/", fetchedAt: 1 }
        : slug === "test-remake"
          ? // Long, with a link only at its end: read through with a controller (web/controller.test.js).
            {
              markdown: `# Test Remake\n\n${Array.from({ length: 40 }, (_, i) => `Paragraph ${i + 1} of the remake's notes, long enough to wrap onto a second line at most window sizes.`).join("\n\n")}\n\nSee [the notes](docs/notes.md).`,
              rawBase: "https://raw.example/",
              htmlBase: "https://github.com/quiver/test-remake/blob/HEAD/",
              fetchedAt: 1,
            }
          : null,
    "catalog:releases": ({ slug, paginationOpts }) => page(entries.some((e) => e.slug === slug) ? [release(slug)] : [], paginationOpts),
    // Test Port's releases the site hasn't verified: one being checked, one no maintainer looked at, one a maintainer stopped.
    "catalog:unverifiedReleases": ({ slug }) => unverifiedFor(slug),
    // Every release, newest first, each with its state.
    "catalog:releaseHistory": ({ slug, paginationOpts }) => {
      if (!entries.some((e) => e.slug === slug)) return page([], paginationOpts);
      const { id, assets, ...verified } = release(slug);
      const all = [
        { ...verified, releaseId: id, notes: "Fixes a crash on start.", state: "verified", reasons: [], assets },
        ...unverifiedFor(slug).map((r) => ({ notes: "", ...r })),
      ];
      // The rest of the repository's releases on GitHub, which the site saw but nobody checked.
      const listed = new Set(all.map((r) => r.version.replace(/^v/, "")));
      if (slug === "test-port")
        all.push(
          ...portReleases()
            .filter((r) => !listed.has(r.tag_name.replace(/^v/, "")))
            .map((r) => ({
              releaseId: `rel_test-port_${r.tag_name}`, version: r.tag_name, releasedAt: Date.parse(r.published_at), notes: "", prerelease: false,
              state: "unverified", reasons: [],
              assets: r.assets.map((a) => ({ id: `asset_${a.id}`, url: a.browser_download_url, filename: a.name, os: "linux", architecture: "x64", format: "zip", size: a.size, checksum: a.digest })),
            })),
        );
      return page(all.sort((a, b) => b.releasedAt - a.releasedAt), paginationOpts);
    },
    "catalog:facets": () => ({
      total: entries.length,
      consoles: [
        { id: "n64", name: "Nintendo 64", brand: "Nintendo" },
        { id: "snes", name: "Super Nintendo Entertainment System", brand: "Nintendo" },
      ],
    }),
    "catalog:matchingGames": ({ search }) => {
      const text = search.trim();
      if (text.length < 2) return [];
      return Object.values(games())
        .filter((g) => titleMatches(g.game.title, text))
        .map((g) => ({ slug: g.game.slug, title: g.game.title, ...(g.art ? { art: g.art } : {}), apps: g.apps.length }))
        .slice(0, 4);
    },
    // What players said, newest first.
    "reviews:list": ({ slug, paginationOpts }) => page(account.feedback(slug), paginationOpts),
    // A list a player shared, as anyone sees it.
    "sharedLists:get": ({ slug }) => {
      const list = account.lists.get(slug);
      if (!list) return null;
      const items = list.apps.flatMap((a) => {
        const e = entries.find((x) => x.id === a.entryId);
        return e ? [{ kind: "entry", entry: withVersion(e) }] : [];
      });
      return {
        slug, name: list.name, ...(list.description ? { description: list.description } : {}), owner: { name: list.owner },
        items, unavailable: list.apps.length - items.length, createdAt: list.createdAt, updatedAt: list.updatedAt,
      };
    },
    "catalog:game": ({ slug }) => {
      const g = games()[slug];
      if (g?.malformed) return { game: g.game, entries: null };
      return g ? { game: g.game, entries: g.apps.map((s) => withVersion(entries.find((e) => e.slug === s))) } : null;
    },
  };
  let version = "1.0.0";
  const releasedAt = (v) => Date.UTC(2026, 0, Number(v.split(".")[1]) + 1);
  const release = (slug) => ({
    id: `release_${slug}_${version}`,
    version,
    releasedAt: releasedAt(version),
    prerelease: false,
    assets: ["linux", "windows"].map((os) => ({
      id: `asset_${slug}_${os}`,
      url: `${base}/files/${slug}-${os}.zip`,
      filename: `${slug}-${os}.zip`,
      os,
      architecture: "x64",
      format: "zip",
      size: zip.length,
      checksum: `sha256:${slug === "tampered-port" ? "0".repeat(64) : sha}`,
    })),
  });
  const unverifiedFor = (slug) =>
    slug === "test-port"
      ? unverifiedReleases().filter((r) => r.version.replace(/^v/, "") !== version)
      : slug === "test-remake"
        ? [
            {
              releaseId: "rel_test-remake_1.1.0", version: "v1.1.0", releasedAt: Date.UTC(2026, 9, 1), prerelease: false, state: "unverified",
              reasons: ["It changes how the app is built."], upstreamUrl: "https://github.com/quiver/test-remake/releases/tag/v1.1.0", assets: [],
            },
          ]
        : [];
  // Test Port's releases on GitHub: the site lists them too, so the launcher doesn't ask GitHub.
  const portReleases = () =>
    ["v1.0.0", "v0.9.0"].map((tag, i) => ({
      tag_name: tag,
      draft: false,
      prerelease: false,
      published_at: `2025-0${6 - i}-01T00:00:00Z`,
      assets: [{ id: 10 + i, name: "TestPort-linux.zip", browser_download_url: `${base}/files/old-linux.zip`, size: zip.length, digest: `sha256:${sha}` }],
    }));
  const unverifiedReleases = () => [
    {
      releaseId: "rel_test-port_1.1.0", version: "v1.1.0", releasedAt: Date.UTC(2025, 6, 1), prerelease: false, state: "unverified",
      reasons: [], checkEndsAt: Date.now() + 31 * 3_600_000, scan: { verdict: "clean", url: "https://www.virustotal.com/gui/file/1" },
      assets: ["linux", "windows"].map((os) => ({ id: `asset_1.1.0_${os}`, url: `${base}/files/test-port-v1.1.0-${os}.zip`, filename: `TestPort-${os}.zip`, os, architecture: "x64", format: "zip", checksum: `sha256:${sha}` })),
    },
    {
      releaseId: "rel_test-port_0.9.0", version: "v0.9.0", releasedAt: Date.UTC(2025, 4, 1), prerelease: false, state: "unverified",
      reasons: ["No maintainer has checked this release."],
      assets: ["linux", "windows"].map((os) => ({ id: `asset_0.9.0_${os}`, url: `${base}/files/test-port-v0.9.0-${os}.zip`, filename: `TestPort-${os}.zip`, os, architecture: "x64", format: "zip", checksum: `sha256:${sha}` })),
    },
    {
      releaseId: "rel_test-port_0.8.0", version: "v0.8.0", releasedAt: Date.UTC(2025, 3, 1), prerelease: false, state: "blocked",
      reasons: ["A maintainer is taking a closer look."], scan: { verdict: "warning", engines: "1 of 70 engines", url: "https://www.virustotal.com/gui/file/8" },
      assets: ["linux", "windows"].map((os) => ({ id: `asset_0.8.0_${os}`, url: `${base}/files/test-port-v0.8.0-${os}.zip`, filename: `TestPort-${os}.zip`, os, architecture: "x64", format: "zip", checksum: `sha256:${sha}` })),
    },
  ];
  // Test Port's mods: a Thunderstore community and a GameBanana game.
  const modFiles = {};
  const thunderstore = [];
  const publishMod = ({ owner = "Modder", name, version, description = "", dependencies = [], nsfw = false, files }) => {
    const full = `${owner}-${name}`;
    const file = `${full}-${version}.zip`;
    modFiles[file] = { "manifest.json": "{}", "icon.png": "", "README.md": `# ${name}`, ...files };
    const pkg = thunderstore.find((p) => p.full_name === full);
    const latest = { version_number: version, description, icon: `${base}/art/${name}.png`, dependencies, download_url: `${base}/mods/${file}`, downloads: 100, file_size: 1000 };
    if (pkg) {
      pkg.versions.unshift(latest);
      pkg.date_updated = new Date().toISOString();
    } else
      thunderstore.push({
        name, full_name: full, owner, package_url: `https://thunderstore.io/c/test-port/p/${owner}/${name}/`,
        date_updated: new Date(Date.UTC(2026, 0, 1)).toISOString(), rating_score: thunderstore.length + 1, is_deprecated: false, has_nsfw_content: nsfw,
        versions: [latest],
      });
  };
  const gamebanana = [
    { id: 101, name: "Greener Bushes", files: { "bushes.zip": { "Greener Bushes/bushes.pak": "bushes" } } },
    { id: 102, name: "Recolor", files: { "recolor-red.zip": { "red.pak": "red" }, "recolor-blue.zip": { "blue.pak": "blue" } } },
  ];
  for (const g of gamebanana) Object.assign(modFiles, g.files);
  const modDownloads = [];
  const account = mockAccount(entries);
  // Quiver Launcher's own releases, as GitHub lists them, signed as a release would be.
  const updateKey = minisignKey();
  const launcherReleases = [];
  const updateFiles = new Map();
  const releasePages = [];
  // Releases that are a Flatpak bundle (someone/flat-game), or one in a zip (someone/zipped-flat).
  const flatpaks = {
    "flat-game": { name: "FlatGame-x86_64.flatpak", bytes: flatpakBundle("com.example.FlatGame") },
    "zipped-flat": { name: "ZippedFlat-linux.zip", bytes: zipped("ZippedFlat.flatpak", flatpakBundle("com.example.ZippedFlat")) },
  };
  const signedInPages = [];
  const githubRequests = [];
  const server = createServer(async (req, res) => {
    const url = new URL(req.url, "http://x");
    if (url.pathname.startsWith("/account/")) return account.handle(req, res, url.pathname.slice(8));
    // The website's page the browser lands on after GitHub or Discord sign-in.
    if (url.pathname === "/site/signed-in/") {
      signedInPages.push(url.search);
      res.writeHead(200, { "Content-Type": "text/html" });
      return res.end("<title>Signed in</title>");
    }
    // PostHog: events are recorded; flags and remote config answer with nothing to change.
    if (url.pathname.startsWith("/posthog/")) {
      const headers = { "Content-Type": "application/json", "Access-Control-Allow-Origin": req.headers.origin ?? "*", "Access-Control-Allow-Headers": "*", "Access-Control-Allow-Methods": "GET, POST, OPTIONS" };
      if (req.method === "OPTIONS") {
        res.writeHead(204, headers);
        return res.end();
      }
      const path = url.pathname.slice("/posthog".length);
      if (/^\/(flags|decide)\/?$/.test(path) || path.startsWith("/array/")) {
        res.writeHead(200, headers);
        return res.end(JSON.stringify({ featureFlags: {}, featureFlagPayloads: {}, errorsWhileComputingFlags: false }));
      }
      const raw = await new Promise((resolve) => {
        const chunks = [];
        req.on("data", (c) => chunks.push(c));
        req.on("end", () => resolve(Buffer.concat(chunks)));
      });
      const text = decodePosthog(raw, url);
      telemetryBodies.push(text);
      try {
        const parsed = JSON.parse(text);
        telemetry.push(...(Array.isArray(parsed) ? parsed : (parsed.batch ?? [parsed])));
      } catch {
        // Kept in telemetryBodies, for tests to look at.
      }
      res.writeHead(200, headers);
      return res.end(JSON.stringify({ status: 1 }));
    }
    const send = (body, status = 200) => {
      res.writeHead(status, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" });
      res.end(JSON.stringify(body));
    };
    // GitHub's API answers the preflight a token's Authorization header needs, notes which requests carried a token,
    // and says how many requests are left: 60 an hour without a token, 5,000 with one, and none for a token it refuses.
    if (url.pathname.startsWith("/github/")) {
      if (req.method === "OPTIONS") {
        res.writeHead(204, { "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Headers": "authorization, accept" });
        return res.end();
      }
      githubRequests.push({ path: url.pathname, token: req.headers.authorization?.replace(/^Bearer /, "") ?? null });
      if (url.pathname === "/github/rate_limit") {
        const token = req.headers.authorization;
        if (token === "Bearer bad-token") return send({ message: "Bad credentials" }, 401);
        return send({ resources: { core: { limit: token ? 5000 : 60, remaining: token ? 4999 : 59, reset: Math.floor(Date.now() / 1000) + 1800 } } });
      }
    }
    // GitHub's API: every release of a catalog app (one the site verified, one it didn't), and READMEs.
    // And its newest, out on GitHub before the site saw it.
    if (url.pathname === "/github/repos/quiver/test-port/releases") {
      const unseen = { ...portReleases()[0], tag_name: "v1.1.5", published_at: "2025-09-01T00:00:00Z" };
      return send([unseen, ...portReleases()].slice(0, Number(url.searchParams.get("per_page") ?? 30)));
    }
    if (url.pathname === "/github/repos/someone/homebrew/readme") {
      res.writeHead(200, { "Content-Type": "text/plain" });
      return res.end("# Homebrew\n\nStraight from GitHub.");
    }
    const flat = url.pathname.match(/^\/github\/repos\/someone\/(flat-game|zipped-flat)\/releases$/);
    if (flat) {
      const { name, bytes } = flatpaks[flat[1]];
      return send([
        {
          tag_name: "v1.0",
          draft: false,
          prerelease: false,
          published_at: "2026-02-01T00:00:00Z",
          assets: [{ id: 1, name, browser_download_url: `${base}/files/flatpak/${flat[1]}`, size: bytes.length, digest: `sha256:${createHash("sha256").update(bytes).digest("hex")}` }],
        },
      ]);
    }
    // GitHub's API, for custom apps: repositories whose release is one tarball, packed as each is named (someone/packed-xz…).
    const packed = url.pathname.match(/^\/github\/repos\/someone\/packed-(xz|bz2|zst)\/releases$/);
    if (packed)
      return send([
        {
          tag_name: "v1.0",
          draft: false,
          prerelease: false,
          published_at: "2026-02-01T00:00:00Z",
          assets: [
            { id: 1, name: `Packed-linux-x64.tar.${packed[1]}`, browser_download_url: `${base}/files/packed-linux-x64.tar.${packed[1]}`, size: tarballs[packed[1]].length, digest: digest(tarballs[packed[1]]) },
          ],
        },
      ]);
    // GitHub's API, for custom apps: one repository the catalog doesn't list.
    if (url.pathname.startsWith("/github/")) {
      if (url.pathname.toLowerCase() !== "/github/repos/someone/homebrew/releases") return send({ message: "Not Found" }, 404);
      return send([
        {
          tag_name: "v2.0",
          draft: false,
          prerelease: false,
          published_at: "2026-02-01T00:00:00Z",
          assets: [
            { id: 1, name: "Homebrew-linux.zip", browser_download_url: `${base}/files/homebrew-linux.zip`, size: zip.length, digest: `sha256:${sha}` },
            { id: 2, name: "Homebrew-windows.zip", browser_download_url: `${base}/files/homebrew-windows.zip`, size: zip.length, digest: `sha256:${sha}` },
          ],
        },
      ]);
    }
    // GitLab's API, for custom apps: one repository whose release has two games for each platform.
    if (url.pathname.startsWith("/gitlab/")) {
      if (url.pathname !== "/gitlab/projects/someone%2Fcollection/releases") return send({ message: "404 Project Not Found" }, 404);
      const files = ["GameA-v1.0-windows-x64.zip", "GameB-v1.0-windows-x64.zip", "GameA-v1.0-linux-x64.zip", "GameB-v1.0-linux-x64.zip"];
      return send([
        {
          tag_name: "v1.0",
          released_at: "2026-03-01T00:00:00Z",
          // Links have titles; the file's own name is in its address.
          assets: { links: files.map((name, i) => ({ id: i + 1, name: `Game ${i + 1}`, url: `${base}/files/${name}` })) },
        },
      ]);
    }
    if (url.pathname === "/thunderstore/c/test-port/api/v1/package/") return send(thunderstore);
    if (url.pathname === "/gamebanana/apiv11/Mod/Index") {
      if (url.searchParams.get("_aFilters[Generic_Game]") !== "9001") return send({ _aRecords: [] });
      return send({
        _aMetadata: { _bIsComplete: true },
        _aRecords: gamebanana.map((g) => ({
          _idRow: g.id, _sName: g.name, _aSubmitter: { _sName: "Banana" }, _tsDateModified: 1767225600, _sVersion: "", _nLikeCount: 4, _nViewCount: 900,
          _bHasFiles: true, _sProfileUrl: `https://gamebanana.com/mods/${g.id}`,
        })),
      });
    }
    const banana = url.pathname.match(/^\/gamebanana\/apiv11\/Mod\/(\d+)$/);
    if (banana) {
      const g = gamebanana.find((g) => String(g.id) === banana[1]);
      if (!g) return send({ _sErrorCode: "NOT_FOUND" }, 404);
      return send({
        _aFiles: Object.keys(g.files).map((name, i) => ({ _idRow: g.id * 10 + i, _sFile: name, _nFilesize: 2048, _sDescription: name.includes("blue") ? "The blue one" : "", _sDownloadUrl: `${base}/mods/${name}` })),
      });
    }
    if (url.pathname.startsWith("/mods/")) {
      const name = decodeURIComponent(url.pathname.slice("/mods/".length));
      if (!modFiles[name]) return send({ message: "Not Found" }, 404);
      modDownloads.push(name);
      const body = zipOf(modFiles[name]);
      res.writeHead(200, { "Content-Type": "application/zip", "Content-Length": body.length });
      return res.end(body);
    }
    if (url.pathname === "/updates/releases") return send(launcherReleases);
    if (url.pathname.startsWith("/site/releases/")) {
      releasePages.push(url.pathname.slice("/site/releases/".length));
      res.writeHead(200, { "Content-Type": "text/html" });
      return res.end("<title>Release</title>");
    }
    if (url.pathname.startsWith("/updates/files/")) {
      const file = updateFiles.get(decodeURIComponent(url.pathname.slice("/updates/files/".length)));
      if (!file) return send({ message: "Not Found" }, 404);
      res.writeHead(200, { "Content-Type": "application/octet-stream", "Content-Length": file.length });
      return res.end(file);
    }
    if (url.pathname.startsWith("/files/flatpak/")) {
      const { bytes } = flatpaks[url.pathname.slice("/files/flatpak/".length)];
      downloads.push(url.pathname.slice("/files/".length));
      res.writeHead(200, { "Content-Type": "application/octet-stream", "Content-Length": bytes.length });
      return res.end(bytes);
    }
    const tarball = url.pathname.match(/^\/files\/packed-.*\.tar\.(xz|bz2|zst)$/);
    if (tarball) {
      downloads.push(url.pathname.slice("/files/".length));
      res.writeHead(200, { "Content-Type": "application/octet-stream", "Content-Length": tarballs[tarball[1]].length });
      return res.end(tarballs[tarball[1]]);
    }
    if (url.pathname.startsWith("/files/")) {
      downloads.push(url.pathname.slice("/files/".length));
      res.writeHead(200, { "Content-Type": "application/zip", "Content-Length": zip.length });
      // Stalled: half the file, and the rest once it's let go (or never, if the launcher hangs up).
      if (stalled) {
        res.write(zip.subarray(0, zip.length >> 1));
        stalls.add(res);
        res.on("close", () => stalls.delete(res));
        return stalled.then(() => res.end(zip.subarray(zip.length >> 1)));
      }
      return res.end(zip);
    }
    if (url.pathname.startsWith("/art/")) {
      res.writeHead(200, { "Content-Type": "image/png" });
      return res.end(PIXEL);
    }
    // A download that failed, as the launcher tells the site: noted for a file of an app it lists, as the site does.
    const problem = url.pathname.match(/^\/api\/v1\/apps\/([^/]+)\/download-problem$/);
    if (problem) {
      const headers = { "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Headers": "content-type", "Access-Control-Allow-Methods": "POST, OPTIONS" };
      if (req.method === "OPTIONS") {
        res.writeHead(204, headers);
        return res.end();
      }
      const body = await new Promise((resolve) => {
        let text = "";
        req.on("data", (c) => (text += c));
        req.on("end", () => resolve(JSON.parse(text || "{}")));
      });
      const slug = decodeURIComponent(problem[1]);
      const listed = entries.some((e) => e.slug === slug) && release(slug).assets.some((a) => a.filename === body.file);
      if (listed) downloadProblems.push({ slug, ...body });
      res.writeHead(listed ? 202 : 404, { ...headers, "Content-Type": "application/json" });
      return res.end(JSON.stringify(listed ? { status: "noted" } : { error: { code: "NOT_FOUND", message: "No such file in a release of this app" } }));
    }
    // The site's catalog API: each route runs the query the site's does, with the same arguments.
    if (url.pathname.startsWith("/api/v1/") && url.pathname !== "/api/v1/release-status") {
      const p = url.searchParams;
      const [resource, slug, child] = url.pathname.slice("/api/v1/".length).split("/").filter(Boolean).map(decodeURIComponent);
      const paginationOpts = { numItems: Number(p.get("limit") ?? 24), cursor: p.get("cursor") };
      const given = (...names) => Object.fromEntries(names.flatMap((n) => (p.get(n) ? [[n, p.get(n)]] : [])));
      const route =
        resource === "apps" && !slug
          ? ["catalog:list", { paginationOpts, ...given("search", "os", "projectType", "console", "maker", "sort", "ai"), ...(p.get("projectTypes") ? { projectTypes: p.get("projectTypes").split(",") } : {}) }]
          : resource === "apps" && !child ? ["catalog:detail", { slug }]
          : resource === "apps" && child === "readme" ? ["catalog:readme", { slug }]
          : resource === "apps" && child === "releases" ? ["catalog:releases", { slug, paginationOpts }]
          : resource === "apps" && child === "unverified-releases" ? ["catalog:unverifiedReleases", { slug }]
          : resource === "apps" && child === "release-history" ? ["catalog:releaseHistory", { slug, paginationOpts }]
          : resource === "apps" && child === "reviews" ? ["reviews:list", { slug, paginationOpts }]
          : resource === "facets" && !slug ? ["catalog:facets", {}]
          : resource === "games" && !slug && p.has("search") ? ["catalog:matchingGames", { search: p.get("search") }]
          : resource === "games" && slug && !child ? ["catalog:game", { slug }]
          : resource === "lists" && slug && !child ? ["sharedLists:get", { slug }]
          : null;
      if (!route) return send({ error: { code: "NOT_FOUND", message: "Route not found" } }, 404);
      const [path, args] = route;
      catalogQueries.push({ path, args });
      let value;
      try {
        validate(path, args);
        value = site[path](args);
      } catch (error) {
        return send({ error: { code: "INVALID_PARAMETERS", message: String(error.message) } }, 400);
      }
      if (value === null) return send({ error: { code: "NOT_FOUND", message: "Not found" } }, 404);
      if (Array.isArray(value)) return send({ items: value });
      if (value && "continueCursor" in value) return send({ items: value.page, nextCursor: value.isDone ? null : value.continueCursor, isDone: value.isDone });
      return send(value);
    }
    // The release status feed, from an internal query on the site.
    if (url.pathname === "/api/v1/release-status")
      return send({
        items: entries.map((e) => ({ id: e.id, slug: e.slug, provider: "github", repository: `quiver/${e.slug}` })),
        nextCursor: null,
        isDone: true,
      });
    send({ error: { message: "Not found" } }, 404);
  });
  // Each app's own artwork. As the site's entryDTO does, a slot an app has none of is its game's, and so is its icon.
  const ownArt = {
    "test-port": { artwork: "/art/test-port-icon.png", libraryArt: { header: "/art/test-port-header.png", capsule: "/art/test-port-capsule.png" } },
    // No box art or icon of its own: its game's.
    "test-remake": { libraryArt: { header: "/art/test-remake-header.png" } },
    // A cover only, and a game with no artwork.
    "tampered-port": { libraryArt: { header: "/art/tampered-port-header.png" } },
  };
  const withArt = (e) => {
    const own = ownArt[e.slug] ?? {};
    const game = games()[e.games[0]?.slug]?.game;
    const at = (path) => path && `${base}${path}`;
    const libraryArt = Object.fromEntries(
      ["capsule", "header", "hero", "logo"].flatMap((slot) => {
        const src = at(own.libraryArt?.[slot]) ?? game?.libraryArt?.[slot];
        return src ? [[slot, src]] : [];
      }),
    );
    const gameIcon = own.artwork ? undefined : game?.artwork;
    return {
      ...(Object.keys(libraryArt).length ? { libraryArt } : {}),
      ...(own.artwork || gameIcon ? { artwork: at(own.artwork) ?? gameIcon } : {}),
      ...(gameIcon ? { artworkFromGame: true } : {}),
    };
  };
  const withVersion = (e) => ({ ...e, ...withArt(e), verified: { ...e.verified, version, releasedAt: releasedAt(version) } });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  base = `http://127.0.0.1:${server.address().port}`;
  publishMod({ name: "Mod_Core", version: "1.0.0", description: "What other mods need.", files: { "ModCore.nrm": "core" } });
  publishMod({ name: "Big_Head", version: "1.1.0", description: "Everyone gets a big head.", dependencies: ["Modder-Mod_Core-1.0.0"], files: { "BigHead.nrm": "big head 1.1.0", "textures/head.png": "head" } });
  publishMod({ name: "Spicy", version: "1.0.0", description: "Not for everyone.", nsfw: true, files: { "Spicy.nrm": "spicy" } });
  return {
    api: `${base}/api/v1`,
    convex: `${base}/convex`,
    account: `${base}/account`,
    github: `${base}/github`,
    /** Requests to GitHub's API, oldest first: { path, token }, the token null without one. */
    githubRequests: () => githubRequests,
    gitlab: `${base}/gitlab`,
    thunderstore: `${base}/thunderstore`,
    gamebanana: `${base}/gamebanana`,
    publishMod,
    modDownloads,
    posthog: `${base}/posthog`,
    site: `${base}/site`,
    /** Visits to the website's signed-in page, by query string ("" or "?error=..."). */
    signedInPages,
    /** Usage data events received, oldest first, and every request body as text. */
    telemetry: () => telemetry,
    telemetryBodies: () => telemetryBodies,
    /** Calls to users.setAnalytics: { user, enabled }. */
    analyticsCalls: account.analyticsCalls,
    /** Turns usage data off (or on) for an account, as the website's privacy page does. */
    setAnalytics: account.setAnalytics,
    /** Saves an account's catalog filters, as "Save as my default" on the website does; undefined clears them. */
    setCatalogDefaults: account.setCatalogDefaults,
    reviews: account.reviews,
    /** Shared lists by slug. */
    lists: account.lists,
    /** The filters of the last catalog page asked for. */
    lastQuery: () => lastQuery,
    /** The site's query each catalog API request ran, oldest first: { path, args }. */
    catalogQueries: () => catalogQueries,
    /** Every release file downloaded, by name, oldest first. */
    downloads: () => downloads,
    /** Failed downloads the launcher told the site of, oldest first: { slug, version, file, problem }. */
    downloadProblems: () => downloadProblems,
    /** Downloads stop halfway while on, and finish when it's turned off. */
    stall(on) {
      if (on && !stalled) stalled = new Promise((resolve) => (unstall = resolve));
      if (!on && stalled) (unstall(), (stalled = null));
    },
    /** Downloads stopped halfway that the launcher is still waiting on. */
    stalled: () => stalls.size,
    /** Publishes a new verified release of every app. */
    release: (next) => (version = next),
    /** Quiver Launcher's releases, for updates, and the key they're signed with. */
    updates: `${base}/updates/releases`,
    updatePublicKey: updateKey.publicKey,
    /** Release pages opened in the browser, by tag. */
    releasePages: () => releasePages,
    /**
     * Publishes a Quiver Launcher release with an AppImage (a script that
     * writes its version and arguments to $QUIVER_UPDATED) and its signature;
     * `tampered` serves different bytes from the ones signed.
     */
    publishLauncher({ version: tag, prerelease = false, tampered = false }) {
      const name = `Quiver Launcher_${tag}_amd64.AppImage`;
      const image = Buffer.from(`#!/bin/sh\necho "${tag} $*" > "$QUIVER_UPDATED"\n`);
      updateFiles.set(name, tampered ? Buffer.concat([image, Buffer.from("# changed\n")]) : image);
      updateFiles.set(`${name}.sig`, Buffer.from(updateKey.sign(image)));
      const asset = (n) => ({ name: n, browser_download_url: `${base}/updates/files/${encodeURIComponent(n)}` });
      launcherReleases.unshift({
        tag_name: `v${tag}`,
        body: `What's new in ${tag}`,
        prerelease,
        draft: false,
        html_url: `${base}/site/releases/v${tag}`,
        assets: [asset(name), asset(`${name}.sig`)],
      });
    },
    close: () => server.close(),
  };
}

/**
 * The site's account functions (users.me and setAnalytics, library.list/save, reviews.save)
 * with the same contract, over HTTP: the real backend is private.
 */
function mockAccount(entries) {
  // Shared lists by slug: { owner, collectionKey, name, description, apps: [{ entryId }], createdAt, updatedAt }.
  const lists = new Map();
  const users = new Map(); // name -> { id, password, items: Map(entryId -> item), collections: Map(key -> collection) }
  const tokens = new Map(); // token -> name
  const reviews = [];
  const analyticsCalls = [];
  // One review per player and app, as reviews.list shows it; Test Port starts with two.
  const saved = new Map();
  const seed = (name, result, body, at) =>
    saved.set(`${name}:entry_test-port`, {
      id: `review_${name}_entry_test-port`, userId: `user_${name}`, author: name, entryId: "entry_test-port",
      entryReleaseId: "release_test-port_1.0.0", version: "v1.0.0", result, body, platform: "linux", createdAt: at, updatedAt: at,
    });
  seed("ada", "runs", "Smooth on my Steam Deck.", Date.UTC(2026, 8, 20));
  seed("lin", "issues", "", Date.UTC(2026, 8, 18));
  const flows = new Map(); // OAuth state -> redirectTo, then code -> state
  const read = (req) =>
    new Promise((resolve) => {
      let body = "";
      req.on("data", (c) => (body += c));
      req.on("end", () => resolve(body ? JSON.parse(body) : {}));
    });
  const send = (res, body, status = 200) => {
    res.writeHead(status, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Headers": "*" });
    res.end(JSON.stringify(body));
  };
  async function handle(req, res, path) {
    if (req.method === "OPTIONS") return send(res, {});
    const body = req.method === "POST" ? await read(req) : {};
    // GitHub/Discord: start, the provider page that sends the browser back, and redeem.
    if (path === "/oauth/start") {
      const state = `state_${Math.random()}`;
      flows.set(state, body.redirectTo);
      return send(res, { redirect: `http://${req.headers.host}/account/oauth/authorize?state=${state}`, state });
    }
    if (path === "/oauth/authorize") {
      const state = new URL(req.url, "http://x").searchParams.get("state");
      const code = `code_${Math.random()}`;
      flows.set(code, state);
      res.writeHead(302, { Location: `${flows.get(state)}?convexAuthCode=${encodeURIComponent(code)}` });
      return res.end();
    }
    if (path === "/oauth/complete") {
      if (flows.get(body.code) !== body.state) return send(res, {}, 401);
      flows.delete(body.code);
      if (!users.has("octocat")) users.set("octocat", { id: "user_octocat", items: new Map(), collections: new Map() });
      const token = `token_${Math.random()}`;
      tokens.set(token, "octocat");
      return send(res, { token });
    }
    if (path === "/signin") {
      const existing = users.get(body.username);
      if (body.create ? existing : existing?.password !== body.password) return send(res, {}, 401);
      if (body.create) users.set(body.username, { id: `user_${body.username}`, password: body.password, items: new Map(), collections: new Map() });
      const token = `token_${Math.random()}`;
      tokens.set(token, body.username);
      return send(res, { token });
    }
    const name = tokens.get((req.headers.authorization ?? "").replace("Bearer ", ""));
    const user = users.get(name);
    if (!user) return send(res, {}, 401);
    if (path === "/library")
      return send(res, {
        // As users.me: the account's usage data setting too.
        user: {
          id: user.id,
          name,
          provider: user.password ? "password" : "github",
          ...(user.analyticsOptOut ? { analyticsOptOut: true } : {}),
          // The filters saved on the website ("Save as my default").
          ...(user.catalogDefaults ? { catalogDefaults: user.catalogDefaults } : {}),
        },
        items: [...user.items.values()].map((i) => ({ ...i, ...(i.entryId ? { slug: entries.find((e) => e.id === i.entryId).slug } : {}) })),
        // As libraryCollections.list: the shared list's slug when the collection is shared.
        collections: [...user.collections.values()].map((c) => {
          const shared = [...lists.entries()].find(([, l]) => l.owner === name && l.collectionKey === c.key);
          return shared ? { ...c, shared: { slug: shared[0] } } : c;
        }),
      });
    // As libraryCollections.save: whole collections, the latest save wins.
    if (path === "/collections/save") {
      // As libraryCollections.save: only the fields it knows (anything else fails the whole call); new fields merge, null clears.
      const known = ["key", "name", "tags", "consoles", "installed", "order", "removed", "apps", "projectTypes", "ai", "follows"];
      if (body.collections.some((c) => Object.keys(c).some((k) => !known.includes(k)))) return send(res, { error: "ArgumentValidationError" }, 400);
      for (const c of body.collections) {
        const before = user.collections.get(c.key) ?? {};
        const merged = { ...c };
        for (const k of ["apps", "projectTypes", "ai", "follows"]) {
          if (c[k] === undefined && before[k] !== undefined) merged[k] = before[k];
          if (c[k] === null) delete merged[k];
        }
        user.collections.set(c.key, { ...merged, updatedAt: Date.now() });
      }
      return send(res, []);
    }
    // As sharedLists.share: sharing the same collection again updates its list.
    if (path === "/lists/share") {
      const name2 = String(body.name ?? "").trim();
      if (!name2 || name2.length > 60) return send(res, { error: "Give the list a name of 60 characters or fewer." }, 400);
      if (!Array.isArray(body.apps) || body.apps.length > 100) return send(res, { error: "A list can have up to 100 apps." }, 400);
      const now = Date.now();
      const mine = [...lists.entries()].find(([, l]) => l.owner === name && l.collectionKey === body.collectionKey);
      const slug = mine?.[0] ?? `${name2.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "").slice(0, 40)}-${Math.random().toString(36).slice(2, 10)}`;
      lists.set(slug, {
        owner: name, collectionKey: body.collectionKey, name: name2, description: body.description, apps: body.apps,
        createdAt: mine?.[1].createdAt ?? now, updatedAt: now,
      });
      return send(res, { slug, refused: [] });
    }
    if (path === "/lists/unshare") {
      if (lists.get(body.slug)?.owner !== name) return send(res, { error: "List not found" }, 400);
      lists.delete(body.slug);
      return send(res, null);
    }
    if (path === "/library/save") {
      // As on the site: an add fills only empty fields, an edit sets what it names.
      for (const c of body.changes) {
        const row = user.items.get(c.key);
        if (!row && !c.add) continue;
        const item = row ?? { key: c.key, ...(c.entryId ? { entryId: c.entryId } : { custom: c.custom }), removed: false };
        for (const key of ["name", "artUrl", "tags", "consoles"])
          if (key in c && !(c.add && item[key] !== undefined)) item[key] = c[key] ?? undefined;
        if (c.add) item.removed = false;
        if (c.removed) item.removed = true;
        user.items.set(c.key, { ...item, updatedAt: Date.now() });
      }
      return send(res, []);
    }
    // As users.setAnalytics.
    if (path === "/analytics") {
      if (typeof body.enabled !== "boolean") return send(res, { error: "ArgumentValidationError" }, 400);
      user.analyticsOptOut = body.enabled ? undefined : true;
      analyticsCalls.push({ user: name, enabled: body.enabled });
      return send(res, null);
    }
    if (path === "/reviews/own") return send(res, saved.get(`${name}:${body.entryId}`) ?? null);
    if (path === "/reviews") {
      // As reviews.save on the site checks it.
      const entry = entries.find((e) => e.id === body.entryId);
      const note = (body.body ?? "").trim();
      const refused = !entry
        ? "Published entry required"
        : !["runs", "issues", "broken"].includes(body.result) || !["windows", "linux", "macos", "android", "ios"].includes(body.platform)
          ? "Invalid review"
          : note.length > 500
            ? "Note must be 500 characters or fewer"
            : body.entryReleaseId && !body.entryReleaseId.startsWith(`release_${entry.slug}_`)
              ? "Published release required"
              : null;
      if (refused) return send(res, { error: refused }, 400);
      const key = `${name}:${body.entryId}`;
      const now = Date.now();
      saved.set(key, {
        id: `review_${name}_${body.entryId}`, userId: user.id, author: name, entryId: body.entryId,
        ...(body.entryReleaseId ? { entryReleaseId: body.entryReleaseId, version: `v${body.entryReleaseId.split("_").pop()}` } : {}),
        result: body.result, body: note, platform: body.platform, createdAt: saved.get(key)?.createdAt ?? now, updatedAt: now,
      });
      reviews.push({ ...body, user: name });
      return send(res, null);
    }
    send(res, {}, 404);
  }
  const feedback = (slug) =>
    [...saved.values()].filter((r) => r.entryId === entries.find((e) => e.slug === slug)?.id).sort((a, b) => b.createdAt - a.createdAt);
  const setAnalytics = (name, enabled) => {
    const user = users.get(name);
    if (user) user.analyticsOptOut = enabled ? undefined : true;
  };
  const setCatalogDefaults = (name, defaults) => {
    const user = users.get(name);
    if (user) user.catalogDefaults = defaults;
  };
  return { handle, reviews, feedback, lists, analyticsCalls, setAnalytics, setCatalogDefaults };
}
