import { describe, expect, it } from "vitest";
import type { Entry } from "@quiverlauncher/api";
import { downloadsLine, downloadsOf, sizeLabel } from "./downloads";
import type { Install, Job } from "./store";

const entry = (id: string, version: string, releasedAt: number) => ({ id, verified: { version, releasedAt } }) as unknown as Entry;
const install = (version: string, releasedAt: number, updates?: Install["updates"]): Install => ({ version, releasedAt, folder: "x", executables: [], ...(updates ? { updates } : {}) });
const name = (id: string) => ({ a: "Alpha", b: "Beta", c: "Gamma" })[id] ?? id;

describe("downloadsOf", () => {
  const library = [{ id: "a" }, { id: "b" }, { id: "c" }];
  const catalog = { a: entry("a", "2.0", 20), b: entry("b", "1.1", 20), c: entry("c", "3.0", 20) };

  it("lists apps with a newer verified release, not pinned, hidden or downloading ones", () => {
    const installs = { a: install("1.0", 10), b: install("1.0", 10, "pinned"), c: install("1.0", 10) };
    expect(downloadsOf(library, {}, installs, catalog).updates).toEqual(["a", "c"]);
    expect(downloadsOf(library, {}, installs, catalog, ["c"]).updates).toEqual(["a"]);
    const jobs: Record<string, Job> = { a: { id: "a", phase: "downloading", received: 0, total: null } };
    expect(downloadsOf(library, jobs, installs, catalog).updates).toEqual(["c"]);
  });

  it("splits running downloads from failed ones", () => {
    const jobs: Record<string, Job> = { a: { id: "a", phase: "downloading", received: 1, total: 4 }, b: { error: "No connection", download: true }, c: { error: "It didn't start" } };
    const d = downloadsOf(library, jobs, {}, catalog);
    expect(d.active).toEqual(["a"]);
    // An app that failed to start isn't a download.
    expect(d.failed).toEqual(["b"]);
  });

  it("counts how far along the downloads of known size are, an installing one as done", () => {
    const jobs: Record<string, Job> = {
      a: { id: "a", phase: "downloading", received: 25, total: 100 },
      b: { id: "b", phase: "installing", received: 100, total: 100 },
      c: { id: "c", phase: "downloading", received: 5, total: null },
    };
    expect(downloadsOf(library, jobs, {}, catalog).percent).toBe(62);
    expect(downloadsOf(library, { c: jobs.c }, {}, catalog).percent).toBeNull();
  });
});

describe("downloadsLine", () => {
  it("names one download, counts several, and adds the updates waiting", () => {
    const one: Record<string, Job> = { a: { id: "a", phase: "downloading", received: 45, total: 100 } };
    expect(downloadsLine({ active: ["a"], failed: [], updates: ["c"], percent: 45 }, one, name)).toEqual(["Downloading Alpha, 45%.", "1 update available."]);
    const two: Record<string, Job> = { ...one, b: { id: "b", phase: "downloading", received: 0, total: null } };
    expect(downloadsLine({ active: ["a", "b"], failed: [], updates: [], percent: null }, two, name)).toEqual(["Downloading 2 apps."]);
  });

  it("says when an app is installing, or didn't finish", () => {
    const jobs: Record<string, Job> = { a: { id: "a", phase: "installing", received: 1, total: 1 }, b: { error: "x", download: true } };
    expect(downloadsLine({ active: ["a"], failed: ["b"], updates: ["c"], percent: 100 }, jobs, name, false)).toEqual(["Installing Alpha.", "Beta didn't finish downloading."]);
  });
});

describe("sizeLabel", () => {
  it("reads in KB, MB and GB", () => {
    expect(sizeLabel(300)).toBe("1 KB");
    expect(sizeLabel(820 * 1024)).toBe("820 KB");
    expect(sizeLabel(12.34 * 1024 * 1024)).toBe("12.3 MB");
    expect(sizeLabel(1.4 * 1024 ** 3)).toBe("1.4 GB");
  });
});
