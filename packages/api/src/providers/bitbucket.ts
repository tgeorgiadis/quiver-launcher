import { ApiError } from "../errors";
import { inferPlatform } from "../platform";
import type { Readme, Release } from "../index";
import { refusal } from "./refusal";
import type { RepositoryProvider, RepositoryRef } from "./types";

type RawDownload = { name: string; size?: number; created_on?: string; links?: { self?: { href?: string } } };
type RawPage = { values?: RawDownload[]; next?: string };

const API = "https://api.bitbucket.org/2.0";

/** The version a download's name carries: "app-1.2.3-win64.zip" is 1.2.3. */
export function bitbucketVersion(name: string): string {
  const base = name.replace(/\.(zip|7z|tar\.gz|tgz|tar\.xz|txz|tar\.zst|tzst|tar\.bz2|tbz2?|tar|rar|appimage|flatpak|exe|apk|msi)$/i, "");
  const match = base.match(/(?:^|[-_.])v?(\d+(?:\.\d+){1,3}(?:[-_.]?(?:alpha|beta|rc|pre|dev)\d*)?)(?:[-_.]|$)/i);
  return match ? match[1] : base;
}

const bitbucketError = (response: Response, token: boolean) => refusal(response, token, "Bitbucket", "x-ratelimit");

/**
 * Bitbucket Cloud has no releases API: it has a flat Downloads list. Each
 * version found in a file's name becomes a release, with that file as its
 * asset. Updates compare versions, as there are no release dates. This is
 * best effort; Bitbucket is experimental.
 */
export const bitbucketProvider: RepositoryProvider = {
  id: "bitbucket",
  name: "Bitbucket",
  defaultHost: "bitbucket.org",
  hosts: ["bitbucket.org"],
  apiBase: () => API,
  async releases(ref, ctx, count = 30) {
    const headers = { Accept: "application/json", ...authOf(ctx.token) };
    const fail = (response: Response) => bitbucketError(response, Boolean(ctx.token?.trim()));
    const downloads: RawDownload[] = [];
    let next: string | undefined = `${API}/repositories/${encodeURIComponent(ref.repository)}/downloads?pagelen=100`;
    while (next) {
      const response = await ctx.fetch(next, { headers });
      if (response.status === 404) throw new ApiError(404, "That repository wasn't found on Bitbucket.");
      if (!response.ok) throw fail(response);
      const page = (await response.json()) as RawPage;
      downloads.push(...(page.values ?? []));
      next = page.next;
    }
    const byVersion = new Map<string, RawDownload[]>();
    for (const download of downloads) {
      if (!download.links?.self?.href) continue;
      const version = bitbucketVersion(download.name);
      byVersion.set(version, [...(byVersion.get(version) ?? []), download]);
    }
    return [...byVersion.entries()]
      .map(([version, files]): Release => {
        const releasedAt = Math.max(...files.map((f) => Date.parse(f.created_on ?? "") || 0));
        return {
          id: version,
          version,
          releasedAt,
          prerelease: /(?:^|[-_.])(?:alpha|beta|rc|pre|dev)/i.test(version),
          assets: files.map((f, i) => ({
            id: `${version}-${i}`,
            url: f.links!.self!.href!,
            filename: f.name,
            ...inferPlatform(f.name),
            format: f.name.split(".").pop() ?? "unknown",
            ...(f.size ? { size: f.size } : {}),
          })),
        };
      })
      .sort((a, b) => b.releasedAt - a.releasedAt || b.version.localeCompare(a.version, undefined, { numeric: true }))
      .slice(0, count);
  },
  async readme(ref, ctx) {
    const headers = { Accept: "text/plain", ...authOf(ctx.token) };
    const fail = (response: Response) => bitbucketError(response, Boolean(ctx.token?.trim()));
    for (const file of ["README.md", "README.rst", "README", "README.txt", "readme.md"]) {
      const response = await ctx.fetch(`${API}/repositories/${encodeURIComponent(ref.repository)}/src/HEAD/${encodeURIComponent(file)}`, { headers });
      if (response.ok) {
        return {
          markdown: await response.text(),
          rawBase: `https://bitbucket.org/${ref.repository}/raw/HEAD/`,
          htmlBase: `https://bitbucket.org/${ref.repository}/src/HEAD/`,
        };
      }
      if (response.status !== 404) throw fail(response);
    }
    return null;
  },
  webUrl: (ref) => `https://bitbucket.org/${ref.repository}`,
  token: {
    auth: "basic",
    label: "Bitbucket app password",
    createUrl: () => "https://bitbucket.org/account/settings/app-passwords/new",
  },
};

function hostOf(ref: RepositoryRef): string {
  return ref.host ?? "bitbucket.org";
}

function authOf(token?: string): Record<string, string> {
  return token?.trim() ? { Authorization: `Basic ${btoa(token.trim())}` } : {};
}
