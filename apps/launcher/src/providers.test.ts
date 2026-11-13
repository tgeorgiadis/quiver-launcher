import { expect, test } from "vitest";
import { bitbucketProvider, bitbucketVersion, createProviders, detectProvider, feedProvider, giteaProvider, parseRepository, toRef } from "@quiverlauncher/api";
import { customKey, fromKey } from "./custom";

type Answer = { status?: number; json?: unknown; text?: string; headers?: Record<string, string> };
type Asked = { url: string; headers: Headers };

/** A fetch that answers each URL from `answers`, recording what was asked and with which headers. */
function fakeFetch(answers: Record<string, Answer>) {
  const asked: Asked[] = [];
  const fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === "string" ? input : input instanceof URL ? input.toString() : input.url;
    asked.push({ url, headers: new Headers(init?.headers) });
    const answer = answers[url];
    if (!answer) return new Response("not found", { status: 404 });
    return new Response(answer.text ?? JSON.stringify(answer.json ?? null), { status: answer.status ?? 200, headers: answer.headers });
  }) as typeof globalThis.fetch;
  return { fetch, asked };
}

test("Gitea releases map their files, put stable ones first and skip drafts", async () => {
  const base = "https://codeberg.org/api/v1/repos/forgejo/forgejo/releases";
  const { fetch } = fakeFetch({
    [`${base}?limit=30&page=1`]: {
      json: [
        { tag_name: "v2.0.0-rc.1", draft: false, prerelease: true, published_at: "2026-02-01T00:00:00Z", assets: [] },
        {
          tag_name: "v1.2.3",
          draft: false,
          prerelease: false,
          published_at: "2026-01-01T00:00:00Z",
          body: "Notes",
          assets: [{ id: 7, name: "app-win64.zip", size: 123, browser_download_url: "https://codeberg.org/o/r/releases/download/v1.2.3/app-win64.zip" }],
        },
        { tag_name: "v1.3.0", draft: true, prerelease: false, published_at: "2026-01-02T00:00:00Z", assets: [] },
      ],
    },
  });
  const releases = await giteaProvider.releases({ provider: "gitea", host: "codeberg.org", repository: "forgejo/forgejo" }, { fetch });
  expect(releases.map((r) => r.version)).toEqual(["v1.2.3", "v2.0.0-rc.1"]);
  expect(releases[0].notes).toBe("Notes");
  expect(releases[0].assets[0]).toMatchObject({ filename: "app-win64.zip", os: "windows", architecture: "x64", size: 123 });
});

test("Gitea says which host didn't have the repository, and reads its README raw", async () => {
  const missing = fakeFetch({});
  await expect(
    giteaProvider.releases({ provider: "gitea", host: "git.example.com", repository: "o/r" }, { fetch: missing.fetch }),
  ).rejects.toThrow("That repository wasn't found on git.example.com.");

  const readme = fakeFetch({ "https://codeberg.org/api/v1/repos/o/r/raw/README.md": { text: "# A port" } });
  expect(await giteaProvider.readme!({ provider: "gitea", host: "codeberg.org", repository: "o/r" }, { fetch: readme.fetch })).toEqual({
    markdown: "# A port",
    rawBase: "https://codeberg.org/o/r/raw/HEAD/",
    htmlBase: "https://codeberg.org/o/r/src/HEAD/",
  });
});

test("Gitea sends the player's token only as its own Authorization header", async () => {
  const { fetch, asked } = fakeFetch({ "https://codeberg.org/api/v1/repos/o/r/releases?limit=30&page=1": { json: [] } });
  const providers = createProviders({ tokens: { "codeberg.org": "tok" }, fetch });
  await providers.releases({ provider: "gitea", host: "codeberg.org", repository: "o/r" });
  expect(asked[0].headers.get("authorization")).toBe("token tok");
});

test("Bitbucket's flat downloads become releases by the version in their names", async () => {
  const url = "https://api.bitbucket.org/2.0/repositories/ws/proj/downloads?pagelen=100";
  const { fetch } = fakeFetch({
    [url]: {
      json: {
        values: [
          {
            name: "app-1.2.3-win64.zip",
            size: 10,
            created_on: "2026-01-01T00:00:00Z",
            links: { self: { href: "https://api.bitbucket.org/2.0/repositories/ws/proj/downloads/app-1.2.3-win64.zip" } },
          },
          {
            name: "app-1.2.3-linux-x64.tar.gz",
            size: 20,
            created_on: "2026-01-01T00:00:00Z",
            links: { self: { href: "https://api.bitbucket.org/2.0/repositories/ws/proj/downloads/app-1.2.3-linux-x64.tar.gz" } },
          },
          {
            name: "app-1.2.2-win64.zip",
            size: 10,
            created_on: "2025-12-01T00:00:00Z",
            links: { self: { href: "https://api.bitbucket.org/2.0/repositories/ws/proj/downloads/app-1.2.2-win64.zip" } },
          },
        ],
      },
    },
  });
  const releases = await bitbucketProvider.releases({ provider: "bitbucket", host: "bitbucket.org", repository: "ws/proj" }, { fetch });
  expect(releases.map((r) => r.version)).toEqual(["1.2.3", "1.2.2"]);
  expect(releases[0].assets.map((a) => a.os).sort()).toEqual(["linux", "windows"]);
  expect(bitbucketVersion("app-2.0.0-rc1-macos.zip")).toBe("2.0.0-rc1");
});

test("a release feed maps its files and their checksums", async () => {
  const sha = "a".repeat(64);
  const { fetch } = fakeFetch({
    "https://example.com/releases.json": {
      json: {
        releases: [
          {
            version: "1.2.3",
            releasedAt: "2026-01-01T00:00:00Z",
            assets: [{ name: "app-linux-x64.zip", url: "https://example.com/app.zip", size: 5, sha256: sha }],
          },
        ],
      },
    },
  });
  const releases = await feedProvider.releases({ provider: "feed", host: "example.com", repository: "releases.json" }, { fetch });
  expect(releases[0].assets[0]).toMatchObject({ filename: "app-linux-x64.zip", os: "linux", architecture: "x64", checksum: `sha256:${sha}` });
});

test("an unknown host is named by what answers its API", async () => {
  const gitea = fakeFetch({ "https://gitea.example/api/v1/version": { json: { version: "1.22.0" } } });
  expect(await detectProvider("gitea.example", gitea.fetch)).toBe("gitea");

  const gitlab = fakeFetch({ "https://gitlab.example/api/v4/projects?per_page=1": { json: [] } });
  expect(await detectProvider("gitlab.example", gitlab.fetch)).toBe("gitlab");

  const github = fakeFetch({ "https://github.example/api/v3/meta": { json: { installed_version: "3.12.0" } } });
  expect(await detectProvider("github.example", github.fetch)).toBe("github");

  const neither = fakeFetch({});
  expect(await detectProvider("unknown.example", neither.fetch)).toBeNull();
});

test("a parsed ref leaves a default host out, so library keys keep their shape", () => {
  expect(parseRepository("owner/name")).toEqual({ provider: "github", host: "github.com", repository: "owner/name" });
  expect(toRef({ provider: "github", host: "github.com", repository: "owner/name" })).toEqual({ provider: "github", repository: "owner/name" });
  expect(toRef({ provider: "gitea", host: "codeberg.org", repository: "owner/name" })).toEqual({ provider: "gitea", host: "codeberg.org", repository: "owner/name" });
});

test("custom keys round-trip GitHub, GitLab and a self-hosted Gitea", () => {
  expect(customKey("github", "Owner/Repo")).toBe("github:owner/repo");
  expect(customKey("gitlab", "Group/Project")).toBe("gitlab:group/project");
  expect(customKey("gitea", "Owner/Repo", "codeberg.org")).toBe("gitea:codeberg.org/owner/repo");
  expect(fromKey("github:owner/repo")).toEqual({ provider: "github", repository: "owner/repo", name: "repo" });
  expect(fromKey("gitea:codeberg.org/owner/repo")).toEqual({ provider: "gitea", host: "codeberg.org", repository: "owner/repo", name: "repo" });
});
