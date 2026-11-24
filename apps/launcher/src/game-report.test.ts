import { expect, test } from "vitest";
import { codeText, endOf, failureLine, issueUrl, reportText, type GameFailure, type ReportDetails } from "./game-report";

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

test("the developer's report opens as a new issue with the report filled in, where the host takes one", () => {
  const github = new URL(issueUrl({ provider: "github", url: "https://github.com/someone/port/" }, details(early), "linux"));
  expect(github.origin + github.pathname).toBe("https://github.com/someone/port/issues/new");
  expect(github.searchParams.get("title")).toBe("Closed right after starting (exit code 2) (v1.2.0)");
  expect(github.searchParams.get("body")).toBe(reportText(details(early), "linux"));

  const gitlab = new URL(issueUrl({ provider: "gitlab", url: "https://gitlab.com/someone/port" }, details(early), "linux"));
  expect(gitlab.pathname).toBe("/someone/port/-/issues/new");
  expect(gitlab.searchParams.get("issue[description]")).toBe(reportText(details(early), "linux"));

  expect(issueUrl({ provider: "bitbucket", url: "https://bitbucket.org/someone/port" }, details(early), "linux")).toBe("https://bitbucket.org/someone/port/issues");
});

test("a long output is cut from its start until the address is one a browser takes", () => {
  const log = Array.from({ length: 400 }, (_, i) => `line ${i}: something went wrong & it said so`).join("\n");
  const url = issueUrl({ provider: "github", url: "https://github.com/someone/port" }, details(early, log), "linux");
  expect(url.length).toBeLessThanOrEqual(7000);
  const body = new URL(url).searchParams.get("body")!;
  expect(body).toContain("line 399");
  expect(body).not.toContain("line 0:");
});

test("with no room for any of the output, none of it is put back", () => {
  expect(endOf("one\ntwo", 0)).toBe("");
  const long = { ...details(early, "x".repeat(5000)), failure: { ...early, line: "é".repeat(1200) } };
  const url = issueUrl({ provider: "github", url: "https://github.com/someone/port" }, long, "linux");
  expect(new URL(url).searchParams.get("body")).not.toContain("xxxx");
});
