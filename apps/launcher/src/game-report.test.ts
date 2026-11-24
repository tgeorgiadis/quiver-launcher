import { expect, test } from "vitest";
import { codeText, endOf, failureLine, reportText, type GameFailure, type ReportDetails } from "./game-report";

const early: GameFailure = { id: "port", folder: "Test Port", kind: "early", code: 2, what: null, line: "usage: port ROM" };
const crashed: GameFailure = { id: "port", folder: "Test Port", kind: "crashed", code: -1073741819, what: "access violation", line: null };
const details = (failure: GameFailure, log = "Loading\nusage: port ROM"): ReportDetails => ({
  name: "Test Port",
  version: "v1.2.0",
  launcher: "4.0.0",
  system: "Windows 11 (x86_64)",
  wine: false,
  failure,
  log,
});

test("a Windows crash's status reads the way Windows writes it, and an exit code as it is", () => {
  expect(codeText(-1073741819, "windows")).toBe("0xC0000005");
  expect(codeText(2, "windows")).toBe("2");
  expect(codeText(-1, "linux")).toBe("-1");
});

test("what happened is one line that's the same for the same failure", () => {
  expect(failureLine(early, "linux")).toBe("Closed right after starting (exit code 2)");
  expect(failureLine(crashed, "windows")).toBe("Crashed: access violation (0xC0000005)");
  expect(failureLine({ ...crashed, code: null, what: "segmentation fault" }, "linux")).toBe("Crashed: segmentation fault");
});

test("a long output keeps its end, from the start of a line", () => {
  expect(endOf("one\ntwo\nthree", 100)).toBe("one\ntwo\nthree");
  expect(endOf("one\ntwo\nthree", 8)).toBe("three");
});

test("a report says the app, what happened, the system and what the game said", () => {
  expect(reportText(details(early), "windows")).toBe(
    [
      "**Test Port** v1.2.0",
      "",
      "- What happened: Closed right after starting (exit code 2)",
      "- Last message: usage: port ROM",
      "- System: Windows 11 (x86_64)",
      "- Started from Quiver Launcher 4.0.0",
      "",
      "The end of the game's output:",
      "",
      "```",
      "Loading",
      "usage: port ROM",
      "```",
    ].join("\n"),
  );
  expect(reportText({ ...details(crashed, ""), wine: true }, "windows")).toBe(
    ["**Test Port** v1.2.0", "", "- What happened: Crashed: access violation (0xC0000005)", "- System: Windows 11 (x86_64), through Wine or Proton", "- Started from Quiver Launcher 4.0.0"].join("\n"),
  );
});

test("with no room for any of the output, none of it is put in", () => {
  expect(endOf("one\ntwo", 0)).toBe("");
  expect(reportText(details(early), "linux", 0)).not.toContain("```");
});
