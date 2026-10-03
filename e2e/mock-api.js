/**
 * A stand-in for quiverlauncher.com's /api/v1 and GitHub's release files,
 * so journeys run offline and the same way every time.
 */
import { createServer } from "node:http";
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

/** A zip with a wrapper folder and a program that leaves a mark when run. */
function buildZip() {
  const dir = mkdtempSync(join(tmpdir(), "quiver-zip-"));
  const zip = join(dir, "port.zip");
  execFileSync("python3", [
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

export async function startMockApi() {
  const zip = buildZip();
  const sha = createHash("sha256").update(zip).digest("hex");
  const entries = [entry("test-port", "Test Port"), entry("tampered-port", "Tampered Port")];
  let base = "";
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
  const account = mockAccount(entries);
  const server = createServer(async (req, res) => {
    const url = new URL(req.url, "http://x");
    if (url.pathname.startsWith("/account/")) return account.handle(req, res, url.pathname.slice(8));
    const send = (body, status = 200) => {
      res.writeHead(status, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" });
      res.end(JSON.stringify(body));
    };
    const [, api, apiVersion, resource, slug, child] = url.pathname.split("/");
    if (url.pathname.startsWith("/files/")) {
      res.writeHead(200, { "Content-Type": "application/zip", "Content-Length": zip.length });
      return res.end(zip);
    }
    if (url.pathname === "/api/v1/release-status")
      return send({
        items: entries.map((e) => ({ id: e.id, slug: e.slug, provider: "github", repository: `quiver/${e.slug}` })),
        nextCursor: null,
        isDone: true,
      });
    if (api !== "api" || apiVersion !== "v1" || resource !== "apps") return send({ error: { message: "Not found" } }, 404);
    if (!slug) {
      const search = url.searchParams.get("search")?.toLowerCase() ?? "";
      const items = entries.filter((e) => e.name.toLowerCase().includes(search));
      return send({ items: items.map(withVersion), nextCursor: null, isDone: true });
    }
    const found = entries.find((e) => e.slug === slug);
    if (!found) return send({ error: { message: "App not found" } }, 404);
    if (child === "releases") return send({ items: [release(slug)], nextCursor: null, isDone: true });
    return send({ entry: withVersion(found), project: { name: found.name, description: "", provider: "github" }, withdrawn: [] });
  });
  const withVersion = (e) => ({ ...e, verified: { ...e.verified, version, releasedAt: releasedAt(version) } });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  base = `http://127.0.0.1:${server.address().port}`;
  return {
    api: `${base}/api/v1`,
    account: `${base}/account`,
    reviews: account.reviews,
    /** Publishes a new verified release of every app. */
    release: (next) => (version = next),
    close: () => server.close(),
  };
}

/**
 * The site's account functions (users.me, library.list/save, reviews.save)
 * with the same contract, over HTTP: the real backend is private.
 */
function mockAccount(entries) {
  const users = new Map(); // name -> { id, password, items: Map(entryId -> item) }
  const tokens = new Map(); // token -> name
  const reviews = [];
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
      if (!users.has("octocat")) users.set("octocat", { id: "user_octocat", items: new Map() });
      const token = `token_${Math.random()}`;
      tokens.set(token, "octocat");
      return send(res, { token });
    }
    if (path === "/signin") {
      const existing = users.get(body.username);
      if (body.create ? existing : existing?.password !== body.password) return send(res, {}, 401);
      if (body.create) users.set(body.username, { id: `user_${body.username}`, password: body.password, items: new Map() });
      const token = `token_${Math.random()}`;
      tokens.set(token, body.username);
      return send(res, { token });
    }
    const name = tokens.get((req.headers.authorization ?? "").replace("Bearer ", ""));
    const user = users.get(name);
    if (!user) return send(res, {}, 401);
    if (path === "/library")
      return send(res, {
        user: { id: user.id, name },
        items: [...user.items.values()].map((i) => ({ ...i, slug: entries.find((e) => e.id === i.entryId).slug })),
      });
    if (path === "/library/save") {
      for (const c of body.changes) {
        const item = user.items.get(c.entryId) ?? { entryId: c.entryId };
        if (!user.items.has(c.entryId) && c.removed) continue;
        for (const key of ["name", "artwork", "tags"]) if (key in c) item[key] = c[key] ?? undefined;
        user.items.set(c.entryId, { ...item, removed: c.removed ?? false, updatedAt: Date.now() });
      }
      return send(res, null);
    }
    if (path === "/reviews") {
      reviews.push({ ...body, user: name });
      return send(res, null);
    }
    send(res, {}, 404);
  }
  return { handle, reviews };
}
