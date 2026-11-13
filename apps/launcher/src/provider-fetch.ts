/**
 * The transport for repository APIs. GitHub, GitLab and Codeberg send CORS
 * headers, so the page asks them directly; other hosts go through the Rust
 * side, which isn't held to CORS and can read their rate-limit headers.
 */
import { native } from "./native";

const BROWSER_HOSTS = new Set(["api.github.com", "github.com", "gitlab.com", "codeberg.org"]);

export const providerFetch: typeof fetch = async (input, init) => {
  const url = typeof input === "string" ? input : input instanceof URL ? input.toString() : input.url;
  const method = (init?.method ?? "GET").toUpperCase();
  if (method !== "GET") throw new Error("Repository hosts are only read.");
  const host = new URL(url).host;
  if (BROWSER_HOSTS.has(host)) {
    try {
      return await fetch(input, init);
    } catch {
      // A host that blocks CORS or the network: the Rust side can still ask.
    }
  }
  const headers = Object.fromEntries(new Headers(init?.headers).entries());
  const response = await native.providerHttp(url, headers);
  return new Response(response.body, { status: response.status, headers: response.headers });
};
