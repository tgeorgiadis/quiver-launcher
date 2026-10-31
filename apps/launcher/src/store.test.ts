import { describe, expect, it } from "vitest";
import type { Entry } from "@quiverlauncher/api";
import { adoptedInstall, hasUpdate } from "./store";

const entry = {
  id: "port",
  slug: "test-port",
  launcher: { folderName: "TestPort", preferredExecutables: { linux: ["port.sh"] } },
  verified: { version: "v1.2.0", releasedAt: 20 },
} as unknown as Entry;

describe("adoptedInstall", () => {
  it("keeps a Quiver Launcher 3 install where it is", () => {
    expect(adoptedInstall({ name: "Test Port", provider: "github", dir: "/old/Apps/TestPort", version: "v1.2.0" }, entry, "linux")).toEqual({
      dir: "/old/Apps/TestPort",
      version: "v1.2.0",
      folder: "TestPort",
      executables: ["port.sh"],
    });
  });

  it("brings one 3.x didn't know the version of up to date", () => {
    const install = adoptedInstall({ name: "Test Port", provider: "github", dir: "/old/Apps/TestPort", version: "0.0.0" }, entry, "linux");
    expect(install?.version).toBe("0.0.0");
    expect(hasUpdate(entry, install)).toBe(true);
  });

  it("starts the program a Windows installer put on the computer", () => {
    const old = { name: "Test Port", provider: "github", dir: "C:\\Old\\Apps\\TestPort", version: "v1.0", program: "C:\\Program Files\\Port\\port.exe" };
    expect(adoptedInstall(old, entry, "windows")).toMatchObject({ installer: "msi", program: old.program, executables: [] });
  });

  it("has nothing to keep for an app 3.x hadn't installed", () => {
    expect(adoptedInstall({ name: "Test Port", provider: "github" }, entry, "linux")).toBeUndefined();
  });
});
