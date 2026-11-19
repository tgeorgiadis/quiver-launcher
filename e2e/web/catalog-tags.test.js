/**
 * The catalog's tags a player would rather not see: unticked on one app (its Tags menu, its page), hidden on every app
 * in their window from Settings (searched, filtered, many at once), and hidden as the catalog adds them. A hidden tag
 * leaves the card and the library's tag filter.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

const state = {
  // The cards show their tags.
  settings: { library: { details: ["subtitle", "kinds"] } },
  library: [
    { id: "entry_test-port", slug: "test-port", addedAt: 1 },
    { id: "entry_tampered-port", slug: "tampered-port", addedAt: 2 },
  ],
  installs: {},
};
let s, page;
const tags = { "test-port": ["recomp", "nintendo", "n64"], "tampered-port": ["recomp", "snes"] };
const prepare = (api) => Object.entries(tags).forEach(([slug, list]) => api.setEntry(slug, { tags: list }));

before(async () => {
  s = await openApp({ state, prepare });
  ({ page } = s);
  await page.locator("article[data-slug='test-port'] .tile-kinds").waitFor();
});

after(async () => {
  await s?.close();
  await stop();
});

const kinds = async (slug) => ((await page.locator(`article[data-slug='${slug}'] .tile-kinds`).count()) ? (await page.locator(`article[data-slug='${slug}'] .tile-kinds`).innerText()).trim().toUpperCase() : "");
const saved = () => s.app.execute(() => window.__calls.findLast((c) => c.cmd === "write_state" && c.args.name === "settings")?.args.value.hiddenTags);
const until = (check) => s.until(check);
const openSettings = async () => {
  await page.locator(".topbar button[aria-label='Settings']").click();
  await page.locator(".settings-nav button", { hasText: "Library" }).first().click();
  await page.getByRole("region", { name: "Catalog tags" }).waitFor();
};
const section = () => page.getByRole("region", { name: "Catalog tags" });
const tagWindow = () => page.getByRole("dialog", { name: "Catalog tags" });
const tagBox = (name) => tagWindow().getByRole("checkbox", { name: new RegExp(`^${name}\\b`) });
const openTags = async () => {
  await section().getByRole("button", { name: "Manage catalog tags" }).click();
  await tagWindow().waitFor();
};
const listed = () => tagWindow().locator(".catalog-tag-name").allInnerTexts();
const toLibrary = async () => {
  await page.keyboard.press("Escape");
  await page.locator("article[data-slug='test-port']").waitFor();
};

test("an app's Tags menu lists the catalog's tags, and unticking one takes it off that app alone", async () => {
  assert.equal(await kinds("test-port"), "RECOMP · NINTENDO");
  await page.locator("article[data-slug='test-port']").click({ button: "right" });
  await page.getByRole("menuitem", { name: "Tags", exact: true }).click();
  const menu = page.getByRole("menu", { name: "Tags" });
  await menu.getByText("From the catalog").waitFor();
  assert.deepEqual(await menu.getByRole("menuitemcheckbox").allInnerTexts(), ["Recomp", "Nintendo"], "not its console");
  assert.equal(await menu.getByRole("menuitemcheckbox", { name: "Nintendo" }).getAttribute("aria-checked"), "true");
  await menu.getByRole("menuitemcheckbox", { name: "Nintendo" }).click();
  await until(async () => (await kinds("test-port")) === "RECOMP");
  assert.equal(await menu.getByRole("menuitemcheckbox", { name: "Nintendo" }).getAttribute("aria-checked"), "false", "still there, to tick again");
  assert.equal(await kinds("tampered-port"), "RECOMP");
  await page.keyboard.press("Escape");
  await page.keyboard.press("Escape");
});

test("Settings sums the catalog's tags up, and their window hides one on every app, and shows it again", async () => {
  await openSettings();
  assert.match(await section().locator(".catalog-tags-summary").innerText(), /2 tags\s+all showing/);
  assert.equal(await section().locator("li").count(), 0, "the list isn't in Settings itself");
  await openTags();
  assert.deepEqual(await listed(), ["Nintendo", "Recomp"]);
  assert.match(await tagWindow().locator("li", { hasText: "Recomp" }).innerText(), /2 apps/);
  assert.equal(await page.evaluate(() => document.activeElement?.getAttribute("aria-label")), "Search tags", "ready to type");
  await tagBox("Recomp").uncheck();
  await tagWindow().locator(".catalog-tag.off", { hasText: "Recomp" }).waitFor();
  await page.keyboard.press("Escape");
  await tagWindow().waitFor({ state: "detached" });
  assert.match(await section().locator(".catalog-tags-summary").innerText(), /2 tags\s+1 hidden/);
  await toLibrary();
  assert.equal(await kinds("test-port"), "", "Nintendo unticked on it, Recomp hidden");
  assert.equal(await kinds("tampered-port"), "");
  // Not in the app's menu while it's hidden everywhere.
  await page.locator("article[data-slug='tampered-port']").click({ button: "right" });
  await page.getByRole("menuitem", { name: "Tags", exact: true }).click();
  assert.equal(await page.getByRole("menu", { name: "Tags" }).getByRole("menuitemcheckbox").count(), 0);
  await page.keyboard.press("Escape");
  await page.keyboard.press("Escape");
  await openSettings();
  await openTags();
  await tagBox("Recomp").check();
  await tagWindow().getByRole("button", { name: "Close" }).click();
  await toLibrary();
  assert.equal(await kinds("tampered-port"), "RECOMP");
});

test("the window searches the tags, lists the shown or hidden ones, and hides or shows as many as it lists at once", async () => {
  await openSettings();
  await openTags();
  await tagWindow().getByRole("searchbox", { name: "Search tags" }).fill("nin");
  assert.deepEqual(await listed(), ["Nintendo"]);
  await tagWindow().getByRole("button", { name: "Hide these" }).click();
  await until(async () => (await saved())?.everywhere?.includes("nintendo"));
  assert.deepEqual((await saved()).everywhere, ["nintendo"], "only the one listed");
  await tagWindow().getByRole("searchbox", { name: "Search tags" }).fill("");
  await tagWindow().getByRole("button", { name: /^Hidden/ }).click();
  assert.deepEqual(await listed(), ["Nintendo"]);
  await tagWindow().getByRole("button", { name: /^Showing/ }).click();
  assert.deepEqual(await listed(), ["Recomp"]);
  await tagWindow().getByRole("button", { name: /^All/ }).click();
  await tagWindow().getByRole("combobox", { name: "Sort tags" }).selectOption("apps");
  assert.deepEqual(await listed(), ["Recomp", "Nintendo"], "the one on the most apps first");
  await tagWindow().getByRole("button", { name: "Hide all" }).click();
  await until(async () => (await saved())?.everywhere?.length === 2);
  assert.ok(await tagWindow().getByRole("button", { name: "Hide all" }).isDisabled());
  await tagWindow().getByRole("button", { name: "Show all" }).click();
  await until(async () => (await saved())?.everywhere?.length === 0);
  await tagWindow().getByRole("searchbox", { name: "Search tags" }).fill("zzz");
  await tagWindow().getByText("No tags match your search.").waitFor();
  await page.keyboard.press("Escape");
  await toLibrary();
  assert.deepEqual(s.errors, []);
});

test("tags the catalog adds later are hidden when the player asked, until they show one", async () => {
  await openSettings();
  await section().getByRole("checkbox", { name: "Hide tags the catalog adds from now on" }).check();
  await until(async () => (await saved())?.hideNew === true);
  const shown = (await saved()).shown;
  assert.ok(["recomp", "nintendo"].every((tag) => shown.includes(tag)), "the ones known now keep showing");
  assert.ok(!shown.includes("n64") && !shown.includes("snes"), "a console's isn't one of them");
  await toLibrary();
  assert.equal(await kinds("tampered-port"), "RECOMP");
  assert.deepEqual(s.errors, []);
  // Later, the catalog has given an app a new tag.
  const later = await openApp({
    state: { ...state, settings: { ...state.settings, hiddenTags: { hideNew: true, shown: ["recomp", "nintendo"] } } },
    prepare: (api) => (prepare(api), api.setEntry("tampered-port", { tags: ["recomp", "widescreen", "snes"] })),
  });
  try {
    page = later.page;
    await page.locator("article[data-slug='tampered-port'] .tile-kinds").waitFor();
    assert.equal(await kinds("tampered-port"), "RECOMP", "the new one is hidden");
    await openSettings();
    await openTags();
    await tagWindow().getByRole("button", { name: /^Hidden/ }).click();
    assert.deepEqual(await listed(), ["Widescreen"]);
    await tagBox("Widescreen").check();
    assert.deepEqual(await listed(), ["Widescreen"], "still listed, ticked now, until the list is searched or filtered again");
    await page.keyboard.press("Escape");
    await toLibrary();
    await later.until(async () => (await kinds("tampered-port")) === "RECOMP · WIDESCREEN");
    assert.deepEqual(later.errors, []);
  } finally {
    await later.close();
  }
});
