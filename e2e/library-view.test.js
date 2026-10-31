/**
 * How the library looks, as each player likes it: one toolbar over the
 * library, filters as a slim row of pills, and a View menu (the same options
 * are in Settings) for cover, box art or icon cards, three
 * card sizes, names or not, a card for each game or each app, and sections by
 * console. The choice is kept on this computer.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync, rmSync } from "node:fs";
import { join } from "node:path";
import { launch } from "./session.js";
import { startMockApi } from "./mock-api.js";

let api, s;

before(async () => {
  api = await startMockApi();
  s = await launch({ api });
  // Three apps in the library: Tampered Port's download is refused, so it stays not installed.
  for (const slug of ["test-port", "test-remake", "tampered-port"]) {
    const card = await s.card(slug);
    await card.waitForDisplayed({ timeout: 20000 });
    await (await card.$("button=Get")).click();
    await s.until(async () => (await card.$("button*=Play").isExisting()) || (await card.$('[role="alert"]').isExisting()), 30000);
  }
  await (await s.app.$("button*=Library")).click();
});

after(async () => {
  await s?.close();
  api?.close();
});

const library = "main > div:not(.page-layer)";
/** The library's cards, by slug. */
const shown = () => s.app.execute((library) => [...document.querySelectorAll(`${library} article`)].map((e) => e.dataset.slug).sort(), library);
/** Each card's picture: the image's file name and how it sits (fill, icon, letterbox; "cover" for the catalog's cover). */
const pictures = () =>
  s.app.execute(
    (library) =>
      Object.fromEntries(
        [...document.querySelectorAll(`${library} article`)].map((a) => {
          const img = a.querySelector(".card-cover img:not([aria-hidden])");
          return [a.dataset.slug, [img?.getAttribute("src")?.split("/art/").pop() ?? null, a.querySelector(".card-art")?.dataset.fit ?? "cover"]];
        }),
      ),
    library,
  );
/** A card's picture frame and the card, in pixels. */
const frame = (slug) =>
  s.app.execute((slug) => {
    const card = document.querySelector(`main > div:not(.page-layer) article[data-slug="${slug}"]`);
    const r = card.querySelector(".card-cover").getBoundingClientRect();
    const c = card.getBoundingClientRect();
    return { width: Math.round(r.width), height: Math.round(r.height), card: Math.round(c.width) };
  }, slug);
const settings = () => JSON.parse(readFileSync(join(s.data, "settings.json"), "utf8"));
const panel = () => s.app.$(".view-panel");
/** Picks an option in the View menu, opening it first. */
async function choose(group, option) {
  if (!(await (await panel()).isExisting())) await (await s.app.$("button[aria-label='View options']")).click();
  await (await (await (await panel()).$(`[aria-label='${group}']`)).$(`button=${option}`)).click();
}
const closeView = async () => {
  if (await (await panel()).isExisting()) await s.app.keys("Escape");
  await s.until(async () => !(await (await panel()).isExisting()));
};

test("the library's tools are one row, all one height, beside the playlist tabs", async () => {
  await s.until(async () => (await shown()).length === 3);
  const tools = await s.app.execute(() =>
    [...document.querySelector(".library-tools").children].map((el) => {
      const name = el.querySelector("input, select")?.getAttribute("aria-label") ?? el.getAttribute("aria-label") ?? el.textContent.trim();
      return [name, Math.round(el.getBoundingClientRect().height)];
    }),
  );
  assert.deepEqual(
    tools.map(([name]) => name),
    ["Search your library", "Sort", "Group by", "Filters", "View options", "Add an app"],
  );
  assert.equal(new Set(tools.map(([, height]) => height)).size, 1, `one height: ${JSON.stringify(tools)}`);
  // At the default window size the tabs share the row.
  const [tabs, bar] = await s.app.execute(() => [".playlists", ".library-tools"].map((q) => document.querySelector(q).getBoundingClientRect()));
  assert.ok(Math.abs(tabs.top + tabs.height / 2 - (bar.top + bar.height / 2)) < 8, "tabs and tools on one row");
  // No filter row until it's asked for, and no sections until they're picked.
  assert.ok(!(await s.app.$(".filter-bar").isExisting()));
  assert.ok(!(await s.app.$(".console-section").isExisting()));
});

test("tabs that don't fit beside the tools fold into a Show menu at their start, rather than take a row", async () => {
  const folded = () => s.app.execute(() => !document.querySelector(".playlists") && Boolean(document.querySelector(".library-show select")));
  await s.app.setWindowSize(900, 700);
  await s.until(folded);
  const [menu, search] = await s.app.execute(() => [".library-show", ".library-search"].map((q) => document.querySelector(q).getBoundingClientRect().top));
  assert.ok(Math.abs(menu - search) < 2, "the menu is on the search's row");
  const choices = await s.app.execute(() => [...document.querySelector(".library-show select").options].map((o) => o.label));
  assert.deepEqual(choices.slice(0, 3), ["All", "Installed", "Not installed"]);
  // Picking one is picking its tab.
  await (await s.app.$(".library-show select")).selectByVisibleText("Not installed");
  await s.until(async () => (await shown()).join() === "tampered-port");
  await (await s.app.$(".library-show select")).selectByVisibleText("All");
  await s.until(async () => (await shown()).length === 3);
  // Wide again, the tabs are back.
  await s.app.setWindowSize(1280, 800);
  await s.until(async () => !(await folded()));
  assert.ok(await s.app.$("nav[aria-label=Playlists]").isDisplayed());
});

test("arrow keys and controllers move along the tools and into the View menu", async () => {
  const focused = () => s.app.execute(() => document.activeElement?.getAttribute("aria-label") ?? document.activeElement?.textContent.trim());
  await s.app.execute(() => document.querySelector("select[aria-label=Sort]").focus());
  await s.app.keys("ArrowRight");
  assert.equal(await focused(), "Group by");
  await s.app.keys("ArrowRight");
  assert.equal(await focused(), "Filters");
  await s.app.keys("ArrowRight");
  assert.equal(await focused(), "View options");
  await s.app.keys("Enter");
  await (await panel()).waitForDisplayed();
  await s.app.keys("ArrowDown");
  assert.ok(await s.app.execute(() => Boolean(document.activeElement?.closest(".view-panel"))), "down goes into the menu");
  await s.app.keys("Escape");
  await s.until(async () => !(await (await panel()).isExisting()));
  assert.equal(await focused(), "View options");
});

test("filters are a slim row of pills; one that's on can be removed, and Clear all clears them", async () => {
  await (await s.app.$("button*=Filters")).click();
  const pills = () => s.app.execute(() => [...document.querySelectorAll(".filter-bar select")].map((e) => e.getAttribute("aria-label")));
  assert.deepEqual(await pills(), ["Console", "Project type", "AI use", "Tag", "Installed"]);
  await (await s.app.$(".filter-bar select[aria-label=Console]")).selectByVisibleText("Nintendo 64");
  await s.until(async () => JSON.stringify(await shown()) === '["test-port","test-remake"]');
  assert.ok(await (await s.app.$("button[aria-label='Remove filter Nintendo 64']")).isDisplayed());
  assert.equal(await (await s.app.$(".library-tools .tool-count")).getText(), "1");
  // Closed, only the filters that are on stay.
  await (await s.app.$("button*=Filters")).click();
  await s.until(async () => JSON.stringify(await pills()) === '["Console"]');
  await (await s.app.$("button=Clear all")).click();
  await s.until(async () => (await shown()).length === 3);
  assert.ok(!(await s.app.$(".filter-bar").isExisting()), "nothing on, no filter row");
  // Removing one pill.
  await (await s.app.$("button*=Filters")).click();
  await (await s.app.$(".filter-bar select[aria-label='AI use']")).selectByVisibleText("Hide mostly AI-generated apps");
  await s.until(async () => (await shown()).length === 2);
  await (await s.app.$("button[aria-label='Remove filter Hide mostly AI-generated']")).click();
  await s.until(async () => (await shown()).length === 3);
});

test("the filters still become a playlist", async () => {
  await (await s.app.$(".filter-bar select[aria-label=Console]")).selectByVisibleText("Super Nintendo Entertainment System");
  await s.until(async () => JSON.stringify(await shown()) === '["tampered-port"]');
  await (await s.app.$("button*=Save as playlist")).click();
  const dialog = await s.app.$("[role=dialog][aria-label='New playlist']");
  await (await dialog.$("input[aria-label='Playlist name']")).setValue("SNES");
  await (await dialog.$("button=Save")).click();
  await s.until(async () => (await s.openTab()) === "SNES");
  assert.deepEqual(await shown(), ["tampered-port"]);
  assert.ok(!(await s.app.$(".filter-bar").isExisting()), "the filters went into the playlist");
  await s.showTab("All");
  await s.until(async () => (await shown()).length === 3);
});

test("cards show box art, the icon or the cover, falling back to the game's art and then the cover", async () => {
  // Box art unless the player picks otherwise.
  assert.deepEqual(await pictures(), {
    "test-port": ["test-port-capsule.png", "fill"],
    // No box art of its own: its game's.
    "test-remake": ["capsule.png", "fill"],
    // No box art at all: its cover, whole.
    "tampered-port": ["tampered-port-header.png", "letterbox"],
  });
  const box = await frame("test-port");
  assert.ok(box.height > box.width * 1.4, `box art is portrait: ${JSON.stringify(box)}`);
  await choose("Card image", "Icon");
  await s.until(async () => (await pictures())["test-port"][1] === "icon");
  assert.deepEqual(await pictures(), {
    "test-port": ["test-port-icon.png", "icon"],
    "test-remake": ["icon.png", "icon"],
    "tampered-port": ["tampered-port-header.png", "letterbox"],
  });
  // The whole icon shows, above the names rather than under them.
  const icon = await s.app.execute(() => {
    const card = document.querySelector(`main > div:not(.page-layer) article[data-slug="test-port"]`);
    const img = card.querySelector(".card-cover img").getBoundingClientRect();
    return { icon: Math.round(img.bottom), names: Math.round(card.querySelector(".card-body").getBoundingClientRect().top) };
  });
  assert.ok(icon.icon <= icon.names, `the icon is above the names: ${JSON.stringify(icon)}`);
  // The catalog's cover, as Browse shows it.
  await choose("Card image", "Cover");
  await s.until(async () => (await pictures())["test-port"][1] === "cover");
  assert.deepEqual(await pictures(), {
    "test-port": ["test-port-header.png", "cover"],
    "test-remake": ["test-remake-header.png", "cover"],
    "tampered-port": ["tampered-port-header.png", "cover"],
  });
  await choose("Card image", "Box art");
  await s.until(async () => (await pictures())["test-port"][1] === "fill");
  await closeView();
});

test("a library card says what this computer has of the app, not the catalog's details", async () => {
  const status = () =>
    s.app.execute(
      (library) => Object.fromEntries([...document.querySelectorAll(`${library} article`)].map((a) => [a.dataset.slug, a.querySelector(".card-status")?.textContent])),
      library,
    );
  // Installed and never played, or refused and not installed.
  assert.deepEqual(await status(), {
    "test-port": "Installed, not played yet",
    "test-remake": "Installed, not played yet",
    "tampered-port": "Not installed",
  });
  const card = await s.card("test-port");
  assert.ok(!(await card.$(".based-on").isExisting()) && !(await card.$(".card-bottom").isExisting()), "no Based on, rating or release age");
  assert.ok(!(await card.$(".cover-badge").isExisting()), "not marked New");
  // Played, it says when, and it's the app to continue at the top of the library.
  assert.ok(!(await s.app.$("section[aria-label='Continue playing']").isExisting()));
  // Play shows, with the card's other buttons, while the pointer is on it.
  await (await card.$(".card-open")).moveTo();
  await (await card.$("button*=Play")).click();
  await s.until(async () => (await status())["test-port"] === "Played today");
  const resume = await s.app.$("section[aria-label='Continue playing']");
  await resume.waitForDisplayed();
  // Its name, then the version installed.
  assert.ok((await (await resume.$(".continue-title")).getText()).startsWith("Test Port"));
  // Its Play button (or, where the test app can't start, what went wrong).
  assert.ok(await (await resume.$(".continue-action > *")).isDisplayed());
  // The catalog button opens the app's page; its name is only its name.
  await (await resume.$("button[aria-label='See Test Port in the catalog']")).click();
  await (await (await s.app.$(".app-page")).$("h1=Test Port")).waitForDisplayed();
  await s.app.keys("Escape");
  await s.until(async () => !(await (await s.app.$(".app-page")).isDisplayed()));
  // View options turn it off, and back on.
  await (await s.app.$("button[aria-label='View options']")).click();
  await (await (await panel()).$("label*=Show Continue playing")).click();
  await s.until(async () => !(await s.app.$("section[aria-label='Continue playing']").isExisting()));
  await s.until(() => settings().library?.continuePlaying === false);
  await (await (await panel()).$("label*=Show Continue playing")).click();
  await (await s.app.$("section[aria-label='Continue playing']")).waitForDisplayed();
  await s.until(() => settings().library?.continuePlaying === undefined);
  await closeView();
});

test("a grid card shows its names and a mark, and the rest on hover or focus", async () => {
  const port = await s.card("test-port");
  const text = async (sel) => (await port.$(sel)).getProperty("textContent");
  // The game's name large, the app's under it.
  assert.equal(await text(".card-title"), "Test Port (original)");
  assert.equal(await text(".card-subtitle"), "Test Port");
  const marks = await s.app.execute((library) => Object.fromEntries([...document.querySelectorAll(`${library} article`)].map((a) => [a.dataset.slug, a.querySelector(".tile-mark")?.dataset.copy ?? null])), library);
  // Both installed and ready to play: nothing beside their names (Get's symbol is for one not installed yet). Null, not
  // undefined, which WebKit's driver sends back as null and WebView2's leaves out.
  assert.deepEqual([marks["test-port"], marks["test-remake"]], [null, null]);
  // At rest, only the names; the pointer on it shows its versions and buttons.
  const play = await port.$("button*=Play");
  await (await s.app.$(`${library} select[aria-label=Sort]`)).moveTo();
  await s.until(async () => !(await play.isDisplayed()));
  await (await port.$(".card-open")).moveTo();
  await play.waitForDisplayed();
  assert.match(await (await port.$(".tile-facts")).getText(), /Installed v1\.0\.0\s+Verified v1\.0\.0/);
  assert.ok(await (await port.$("button*=Open folder")).isDisplayed());
  await (await s.app.$(`${library} select[aria-label=Sort]`)).moveTo();
  await s.until(async () => !(await play.isDisplayed()));
  // Focus shows them too, and a controller's Confirm plays the app rather than opening its page.
  await s.app.execute(() => document.querySelector('main > div:not(.page-layer) article[data-slug="test-port"] .card-open').focus());
  await play.waitForDisplayed();
  const launched = join(s.apps, "test-port", "launched.txt");
  rmSync(launched, { force: true });
  for (const down of [true, false])
    await s.app.execute((down) => window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event: "pad", payload: { key: "South", down } }), down);
  await s.until(() => existsSync(launched));
  assert.equal(await (await s.app.$(".app-page")).isExisting(), false);
  // The app's name can be the large one instead.
  await choose("Large name", "App");
  await s.until(async () => (await text(".card-title")) === "Test Port");
  assert.equal(await text(".card-subtitle"), "Test Port (original)");
  await choose("Large name", "Game");
  await closeView();
  await s.until(() => settings().library?.nameOrder === undefined);
});

test("card size changes the layout; names can be hidden", async () => {
  const medium = await frame("test-port");
  await choose("Card size", "Small");
  await s.until(async () => (await frame("test-port")).height < medium.height);
  const small = await frame("test-port");
  assert.ok(small.card < medium.card, `small cards are narrower: ${small.card} < ${medium.card}`);
  await choose("Card size", "Large");
  await s.until(async () => (await frame("test-port")).height > medium.height);
  assert.ok((await frame("test-port")).card > medium.card, "large cards are wider");
  await choose("Card size", "Medium");
  // Names off: only pictures and buttons.
  await (await s.app.$("label*=Show names on cards")).click();
  await s.until(async () => !(await (await (await s.card("test-port")).$(".card-title")).isDisplayed()));
  await (await s.app.$("label*=Show names on cards")).click();
  await s.until(async () => (await (await s.card("test-port")).$(".card-title")).isDisplayed());
  await choose("Card image", "Icon");
  await closeView();
  await s.until(() => settings().library?.image === "icon");
  assert.deepEqual(settings().library, { image: "icon" });
});

test("the library sorts installed apps first or last, or by name, and keeps the choice", async () => {
  // The cards in the order they're shown.
  const order = () => s.app.execute((library) => [...document.querySelectorAll(`${library} article`)].map((e) => e.dataset.slug), library);
  const sortBy = async (label) => (await s.app.$(`${library} select[aria-label=Sort]`)).selectByVisibleText(label);
  await sortBy("Not installed first");
  await s.until(async () => (await order()).join() === "tampered-port,test-port,test-remake");
  await sortBy("Installed first");
  await s.until(async () => (await order()).join() === "test-port,test-remake,tampered-port");
  await s.until(() => settings().librarySort === "installed");
  await sortBy("Recently played");
  await s.until(() => settings().librarySort === undefined);
});

test("View options can give each game one card, which lists its ports, each with its page and its button", async () => {
  const cards = () => s.app.execute((library) => [...document.querySelectorAll(`${library} article`)].map((a) => a.dataset.game ?? a.dataset.slug).sort(), library);
  const oneEach = async () => {
    if (!(await (await panel()).isExisting())) await (await s.app.$("button[aria-label='View options']")).click();
    await (await (await panel()).$("label*=One card for each game")).click();
  };
  // The library starts with a card for each app.
  await oneEach();
  // Test Port's game has Test Remake too; Tampered Port's, only it.
  await s.until(async () => JSON.stringify(await cards()) === '["tampered-port","test-port"]');
  await s.until(() => settings().library?.byGame === true);
  await closeView();
  const card = await s.app.$(`${library} article[data-game="test-port"]`);
  assert.equal(await (await card.$(".card-title")).getProperty("textContent"), "Test Port (original)");
  assert.equal(await (await card.$(".card-subtitle")).getProperty("textContent"), "2 ways to play");
  // Its ports, the one played last first, each with its button.
  await (await card.$(".card-open")).click();
  const list = await s.app.$(".port-panel");
  await list.waitForDisplayed();
  const ports = await s.app.execute(() => [...document.querySelectorAll(".port-panel .port-row")].map((r) => [r.querySelector(".port-open strong").textContent, r.querySelector(".port-action button")?.textContent.trim()]));
  assert.deepEqual(ports, [
    ["Test Port", "Play"],
    ["Test Remake", "Play"],
  ]);
  // A port's line opens its page.
  await (await list.$("button*=Test Remake")).click();
  await (await (await s.app.$(".app-page")).$("h1=Test Remake")).waitForDisplayed();
  await s.app.keys("Escape");
  await s.until(async () => !(await (await s.app.$(".app-page")).isDisplayed()));
  // And a card for each app again.
  await oneEach();
  await s.until(async () => (await shown()).length === 3);
  await closeView();
  await s.until(() => settings().library?.byGame === undefined);
});

test("Settings has the same options under Library", async () => {
  await s.settings("Library");
  const section = await s.app.$("section.settings-page [role=region][aria-label=Library]");
  await section.waitForDisplayed();
  const pressed = (group) => s.app.execute((group) => document.querySelector(`[aria-label=Settings] [aria-label='${group}'] [aria-pressed=true]`)?.textContent, group);
  assert.deepEqual([await pressed("Layout"), await pressed("Card image"), await pressed("Card size")], ["Grid", "Icon", "Medium"]);
  await (await (await section.$("[aria-label='Card image']")).$("button=Box art")).click();
  await (await (await section.$("[aria-label='Card size']")).$("button=Large")).click();
  await (await section.$("select[aria-label='Group by']")).selectByVisibleText("Group by console");
  await s.closeSettings();
  await (await s.app.$("h2=Nintendo 64")).waitForDisplayed();
  await (await s.app.$("h2=Super Nintendo Entertainment System")).waitForDisplayed();
  // Grouped, every section starts folded away.
  await (await s.app.$(`${library} button[aria-label='Expand all sections']`)).click();
  await s.until(async () => (await pictures())["test-port"][1] === "fill");
  await s.until(() => settings().groupBy === "console");
  assert.deepEqual(settings().library, { size: "large" });
});

test("the view is kept on this computer through a restart", async () => {
  const data = s.data;
  await s.close();
  s = await launch({ api, data });
  await s.until(async () => (await shown()).length === 3);
  assert.equal(
    await s.app.execute(() => document.querySelector("main > div:not(.page-layer) .library-grid").className),
    "catalog-grid library-grid layout-grid image-box size-large",
  );
  await (await s.app.$("h2=Nintendo 64")).waitForDisplayed();
  assert.equal((await pictures())["test-port"][0], "test-port-capsule.png");
});

test("the sort is kept through a restart too", async () => {
  await (await s.app.$(`${library} select[aria-label=Sort]`)).selectByVisibleText("Not installed first");
  await s.until(() => settings().librarySort === "not-installed");
  const data = s.data;
  await s.close();
  s = await launch({ api, data });
  await s.until(async () => (await shown()).length === 3);
  assert.equal(await (await s.app.$(`${library} select[aria-label=Sort]`)).getValue(), "not-installed");
});
