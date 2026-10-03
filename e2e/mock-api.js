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
  const release = (slug) => ({
    version: "1.0.0",
    releasedAt: Date.now(),
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
  const server = createServer((req, res) => {
    const url = new URL(req.url, "http://x");
    const send = (body, status = 200) => {
      res.writeHead(status, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" });
      res.end(JSON.stringify(body));
    };
    const [, api, version, resource, slug, child] = url.pathname.split("/");
    if (url.pathname.startsWith("/files/")) {
      res.writeHead(200, { "Content-Type": "application/zip", "Content-Length": zip.length });
      return res.end(zip);
    }
    if (api !== "api" || version !== "v1" || resource !== "apps") return send({ error: { message: "Not found" } }, 404);
    if (!slug) {
      const search = url.searchParams.get("search")?.toLowerCase() ?? "";
      const items = entries.filter((e) => e.name.toLowerCase().includes(search));
      return send({ items, nextCursor: null, isDone: true });
    }
    const found = entries.find((e) => e.slug === slug);
    if (!found) return send({ error: { message: "App not found" } }, 404);
    if (child === "releases") return send({ items: [release(slug)], nextCursor: null, isDone: true });
    return send({ entry: found, project: { name: found.name, description: "", provider: "github" }, withdrawn: [] });
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  base = `http://127.0.0.1:${server.address().port}`;
  return { api: `${base}/api/v1`, close: () => server.close() };
}
