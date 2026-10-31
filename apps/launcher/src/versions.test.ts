import { expect, test } from "vitest";
import type { Asset, HistoryRelease, Release, UnverifiedRelease } from "@quiverlauncher/api";
import { installedFile, insteadOf, newestUnblocked, saidOf, unverifiedFile, unverifiedIn, versionList, wayOffLabel, withdrawnAsBlocked } from "./versions";

const release = (version: string, releasedAt: number, id = `gh-${version}`): Release => ({
  id,
  version,
  releasedAt,
  prerelease: false,
  assets: [{ id: `${id}-file`, url: `https://github.com/o/r/${version}.zip`, filename: `${version}.zip`, os: "windows", architecture: "x64", format: "zip" }],
});
const unverified = (version: string, releasedAt: number, state: "unverified" | "blocked", reasons: string[]): UnverifiedRelease => ({
  releaseId: `site-${version}`,
  version,
  releasedAt,
  prerelease: false,
  state,
  reasons,
  assets: state === "blocked" ? [] : [{ id: `site-${version}-file`, url: `https://github.com/o/r/${version}.zip`, filename: `${version}.zip`, os: "windows", architecture: "x64", format: "zip", checksum: "sha256:" + "a".repeat(64) }],
});

test("marks each release verified, unverified or blocked, using the site's files for the ones it knows", () => {
  const list = versionList(
    [release("1.0", 1, "entry-1.0")],
    [unverified("1.2", 3, "unverified", ["Quiver is still checking it."]), unverified("1.1", 2, "blocked", ["A maintainer is taking a closer look."])],
    [release("v1.3", 4), release("v1.2", 3), release("v1.1", 2), release("v1.0", 1)],
  );
  expect(list.map((v) => [v.release.version, v.state, v.known, v.reasons])).toEqual([
    ["v1.3", "unverified", false, ["Quiver hasn't seen this release yet."]],
    ["1.2", "unverified", true, ["Quiver is still checking it."]],
    ["1.1", "blocked", true, ["A maintainer is taking a closer look."]],
    ["1.0", "verified", true, []],
  ]);
  // Installed against the checksum the site pinned, not GitHub's listing.
  expect(list[1].release.assets[0].checksum).toBe("sha256:" + "a".repeat(64));
  expect(list[3].release.id).toBe("entry-1.0");
});

test("keeps releases only the site has, newest first", () => {
  const list = versionList([release("1.0", 1)], [unverified("0.9", 0.5, "unverified", ["No maintainer has checked this release."])], []);
  expect(list.map((v) => [v.release.version, v.state])).toEqual([
    ["1.0", "verified"],
    ["0.9", "unverified"],
  ]);
});

test("leaves every unpublished release unverified, as GitHub has it, when the site doesn't list them", () => {
  const list = versionList([], null, [release("v1.1", 2)]);
  expect(list).toEqual([{ release: release("v1.1", 2), state: "unverified", reasons: [], known: false }]);
});

test("the site's history gives every release with its state, verified ones as the catalog publishes them", () => {
  const history: HistoryRelease[] = [
    { ...unverified("1.2", 3, "unverified", ["Quiver is still checking it."]), notes: "" },
    { ...unverified("1.1", 2, "blocked", ["A maintainer is taking a closer look."]), notes: "" },
    { ...unverified("1.0", 1, "unverified", []), releaseId: "site-1.0", notes: "", state: "verified" },
  ];
  const list = versionList([release("1.0", 1, "entry-1.0")], unverifiedIn(history), []);
  expect(list.map((v) => [v.release.version, v.state, v.known, v.reasons])).toEqual([
    ["1.2", "unverified", true, ["Quiver is still checking it."]],
    ["1.1", "blocked", true, ["A maintainer is taking a closer look."]],
    ["1.0", "verified", true, []],
  ]);
  // The id a review names, not the repository release's.
  expect(list[2].release.id).toBe("entry-1.0");
});

test("GitHub's newest releases add the ones the site hasn't seen yet to its history", () => {
  const history: HistoryRelease[] = [
    { ...unverified("1.1", 2, "unverified", ["Quiver is still checking it."]), notes: "" },
    { ...unverified("1.0", 1, "unverified", []), notes: "", state: "verified" },
  ];
  const list = versionList([release("1.0", 1, "entry-1.0")], unverifiedIn(history), [release("v1.2", 3), release("v1.1", 2), release("v1.0", 1)]);
  expect(list.map((v) => [v.release.version, v.state, v.known, v.reasons])).toEqual([
    ["v1.2", "unverified", false, ["Quiver hasn't seen this release yet."]],
    ["1.1", "unverified", true, ["Quiver is still checking it."]],
    ["1.0", "verified", true, []],
  ]);
});

const file = (filename: string, more: Partial<Asset> = {}): Asset => ({ id: filename, url: `https://github.com/o/r/${filename}`, filename, os: "windows", architecture: "x64", format: "zip", ...more });
const sha = (hex: string) => `sha256:${hex.repeat(64)}`;

test("a file of a verified release that changed since, or that Quiver has no checksum for, asks before it installs", () => {
  const verified = file("Game-Linux.tar.gz", { checksum: sha("a") });
  const changed = file("Game-Windows.zip", { unverifiedChecksum: sha("b"), checkEndsAt: 5 });
  const unchecked = file("Game-Mac.zip");
  const release = { assets: [verified, changed, unchecked] };
  expect(unverifiedFile(verified, release)).toBeUndefined();
  expect(unverifiedFile(changed, release)).toBe("changed");
  expect(unverifiedFile(unchecked, release)).toBe("unchecked");
  // A release with no checksums at all (GitLab) installs as it always has.
  expect(unverifiedFile(unchecked, { assets: [unchecked, file("Game-Linux.zip")] })).toBeUndefined();
});

test("a file installed before it was verified is verified once its release lists it, waits while it waits, and differs otherwise", () => {
  const release = { assets: [file("a.zip", { checksum: sha("a") }), file("b.zip", { unverifiedChecksum: sha("b") })] };
  expect(installedFile(sha("a"), release)).toBe("verified");
  expect(installedFile(sha("A"), release)).toBe("verified");
  expect(installedFile(sha("b"), release)).toBe("waiting");
  expect(installedFile(sha("c"), release)).toBe("differs");
  // Nothing to tell by: no checksum kept from the install, or none listed.
  expect(installedFile(undefined, release)).toBe("verified");
  expect(installedFile(sha("c"), { assets: [file("a.zip")] })).toBe("verified");
  // A file Quiver has no checksum for may be the one installed: it can't tell.
  expect(installedFile(sha("c"), { assets: [file("a.zip", { checksum: sha("a") }), file("b.zip")] })).toBe("waiting");
});

test("an app with no verified release offers its newest that isn't blocked and still has files", () => {
  const history = [
    unverified("1.3", 4, "blocked", ["Taken down as unsafe."]),
    { ...unverified("1.2", 3, "unverified", []), assets: [] },
    unverified("1.1", 2, "unverified", ["Quiver is still checking it."]),
    unverified("1.0", 1, "unverified", []),
  ];
  expect(newestUnblocked(history)?.version).toBe("1.1");
  expect(newestUnblocked(history.slice(0, 2))).toBeUndefined();
  // A withdrawn one is blocked, whatever the site still says of it.
  const pulled = withdrawnAsBlocked(history, (r) => r.version, [{ version: "v1.1", reason: "Antivirus engines flag one of its files.", at: 5 }]);
  expect(newestUnblocked(pulled)?.version).toBe("1.0");
});

test("a broken download offers the newest other verified release with a file for this computer, never the one installed", () => {
  const releases = [release("1.3", 4), release("1.2", 3), release("1.1", 2)];
  const any = () => true;
  expect(insteadOf(releases, "v1.3", undefined, any)?.version).toBe("1.2");
  expect(insteadOf(releases, "1.3", "v1.2", any)?.version).toBe("1.1");
  expect(insteadOf(releases, "1.3", undefined, (r) => r.version !== "1.2")?.version).toBe("1.1");
  expect(insteadOf(releases.slice(0, 1), "1.3", undefined, any)).toBeUndefined();
});

test("feedback counts by the version it tested, whatever its tag's v", () => {
  const said = saidOf([
    { version: "v1.0", result: "runs" },
    { version: "1.0", result: "issues" },
    { version: "v1.0", result: "runs" },
    { version: "v0.9", result: "broken" },
    { result: "runs" },
  ]);
  expect(Object.fromEntries(said)).toEqual({ "1.0": { runs: 2, issues: 1, broken: 0 }, "0.9": { runs: 0, issues: 0, broken: 1 } });
});

test("a withdrawn version is blocked, with its withdrawal as why, even when the site still calls it verified", () => {
  const list = versionList([release("v0.8.0", 3), release("v0.7.0", 2)], [], []);
  const shown = withdrawnAsBlocked(list, (v) => v.release.version, [{ version: "0.8.0", reason: "Antivirus engines flag one of its files.", at: 4 }]);
  expect(shown.map((v) => [v.release.version, v.state, v.reasons, Boolean(v.withdrawn)])).toEqual([
    ["v0.8.0", "blocked", ["Antivirus engines flag one of its files."], true],
    ["v0.7.0", "verified", [], false],
  ]);
});

test("the way off a withdrawn release says Downgrade when it goes back to an older one", () => {
  const installed = { version: "v0.8.0", releasedAt: 8 };
  expect(wayOffLabel(installed, { version: "v0.7.0", releasedAt: 7 })).toBe("Downgrade to v0.7.0");
  expect(wayOffLabel({ ...installed, updates: "pinned" }, { version: "v0.7.0", releasedAt: 7 })).toBe("Unpin and downgrade to v0.7.0");
  expect(wayOffLabel(installed, { version: "v0.8.1", releasedAt: 9 })).toBe("Install v0.8.1");
  // Without dates, by version number: 0.10 comes after 0.9.
  expect(wayOffLabel({ version: "0.10.0" }, { version: "0.9.0" })).toBe("Downgrade to v0.9.0");
  expect(wayOffLabel({ version: "0.9.0" }, { version: "0.10.0" })).toBe("Install v0.10.0");
});
