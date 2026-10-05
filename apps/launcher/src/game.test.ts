import { describe, expect, it } from "vitest";
import type { Entry } from "@quiverlauncher/api";
import { byVersion, gameVersionLabel } from "./game";

const names = { ps1: "PlayStation", n64: "Nintendo 64" };
const app = (id: string, basedOn?: Entry["basedOn"]) => ({ id, ...(basedOn ? { basedOn } : {}) }) as Entry;

describe("game versions", () => {
  it("names a version as the website does", () => {
    expect(gameVersionLabel({ console: "ps1" }, names)).toBe("PlayStation version");
    expect(gameVersionLabel({ console: "ps1", edition: "Director's Cut" }, names)).toBe("PlayStation · Director's Cut");
    expect(gameVersionLabel({ console: "gcn" }, names)).toBe("GCN version");
  });

  it("groups apps by version only when two are known", () => {
    const a = app("a", { console: "ps1" });
    const b = app("b");
    const c = app("c", { console: "n64" });
    const d = app("d", { console: "ps1" });
    expect(byVersion([a, b, d])).toEqual([{ version: undefined, entries: [a, b, d] }]);
    expect(byVersion([a, b, c, d]).map((g) => g.entries.map((e) => e.id))).toEqual([["a", "d"], ["c"], ["b"]]);
  });
});
