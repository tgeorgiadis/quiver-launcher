import { expect, test } from "vitest";
import type { Release, UnverifiedRelease } from "@quiverlauncher/api";
import { versionList } from "./versions";

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
