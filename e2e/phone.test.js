/**
 * Every screen on Android phones (phone.js): nothing scrolls sideways or is
 * cut off at the screen's edge, no text is squeezed to a few letters a line,
 * and pinching doesn't zoom. Each screen's picture is saved in
 * phone-screenshots/, to look over (CI keeps them with the run).
 *
 * A phone as wide as most (Pixel 7, 412 px) in dark, and one of the narrowest
 * still common (Galaxy S8, 360 px) in light.
 */
import { after, before, describe, test } from "node:test";
import assert from "node:assert/strict";
import { mkdirSync } from "node:fs";
import { join } from "node:path";
import { cutOff, openPhone, sideways, squeezed, zoomable } from "./phone.js";

const shots = join(import.meta.dirname, "phone-screenshots");

// Test Port installed and played, the other two in the library only, Browse listing them too, and a card for each game.
const state = {
  settings: { browseShowsLibrary: true, library: { byGame: true } },
  library: ["test-port", "test-remake", "tampered-port"].map((slug, i) => ({ id: `entry_${slug}`, slug, addedAt: Date.now() - i * 60_000 })),
  installs: { "entry_test-port": { version: "1.0.0", folder: "test-port", executables: [], lastPlayed: Date.now() - 3600_000, playTime: 5400 } },
};

for (const [device, theme] of [
  ["Pixel 7", "dark"],
  ["Galaxy S8", "light"],
]) {
  describe(`${device}, ${theme}`, () => {
    let s, page;
    const folder = join(shots, `${device.replaceAll(" ", "-").toLowerCase()}-${theme}`);
    before(async () => {
      mkdirSync(folder, { recursive: true });
      // Tampered Port has no Android release, so the library leaves it out.
      s = await openPhone({ device, theme, state, computerOnly: ["tampered-port"] });
      page = s.page;
    });
    after(() => s?.close());

    /** Saves the screen as `name`, and checks it fits the phone. */
    const check = async (name) => {
      // Artwork and lists come in after the screen does.
      await page.waitForLoadState("networkidle").catch(() => {});
      await page.waitForTimeout(400);
      // The page scrolls in its own area under the top bar: for the picture, it runs its whole length instead.
      const whole = (on) =>
        page.evaluate((on) => {
          const shell = document.querySelector(".shell");
          shell?.style.setProperty("position", on ? "static" : "");
          shell?.querySelector(":scope > main")?.style.setProperty("overflow", on ? "visible" : "");
        }, on);
      await whole(true);
      await page.screenshot({ path: join(folder, `${name}.png`), fullPage: true });
      await whole(false);
      assert.deepEqual(await sideways(page), { by: 0, elements: [] }, `${name} scrolls sideways`);
      assert.deepEqual(await cutOff(page), [], `${name} has text cut off at the edge`);
      assert.deepEqual(await squeezed(page), [], `${name} has text squeezed to a few letters a line`);
    };
    const tap = (name) => page.getByRole("button", { name }).first().click();
    const tab = async (name) => {
      await page.locator(".topbar nav button", { hasText: name }).click();
      await page.locator("main > div:not(.page-layer):not([hidden])").waitFor();
    };

    test("pinching doesn't zoom", async () => assert.equal(await zoomable(page), false));

    test("Library", async () => {
      await page.locator("article").first().waitFor();
      await check("01-library");
      await tap(/^View/);
      await check("02-library-view");
      await page.keyboard.press("Escape");
      await page.locator(".library-tools").getByRole("button", { name: "Filters" }).click();
      await check("03-library-filters");
    });

    test("the library leaves out apps with no Android release, and says so", async () => {
      await page.keyboard.press("Escape");
      // A card for each game (Test Remake shares Test Port's), by the game's slug; an app of no game's by its own.
      const slugs = () => page.locator(".library-grid article").evaluateAll((cards) => cards.map((c) => c.dataset.game ?? c.dataset.slug).sort());
      assert.deepEqual(await slugs(), ["test-port"]);
      const menu = page.locator(".library-tools .platform-filter");
      assert.equal(await menu.getAttribute("aria-label"), "Platforms: 1 app hidden");
      assert.match((await menu.getAttribute("class")) ?? "", /\bon\b/);
      await menu.click();
      // Android is ticked, and stays: it's the only one.
      const panel = page.locator(".platform-panel");
      assert.equal(await panel.getByLabel("Android").isChecked(), true);
      assert.equal(await panel.getByLabel("Android").isDisabled(), true);
      assert.equal(await panel.getByLabel("Windows").isChecked(), false);
      await check("03b-library-platforms");
      await page.keyboard.press("Escape");
      // For now, then hidden again.
      await page.locator(".platform-note").getByRole("button", { name: "Show them" }).click();
      assert.deepEqual(await slugs(), ["tampered-port", "test-port"]);
      await page.locator(".platform-note").getByRole("button", { name: "Hide them" }).click();
      assert.deepEqual(await slugs(), ["test-port"]);
    });

    test("a game's card lists its ports", async () => {
      await page.locator('.library-grid article[data-game="test-port"] .card-open').click();
      await page.locator(".port-panel").waitFor();
      await page.waitForTimeout(400);
      // The screen as it is: a whole page's picture changes the window's size, which closes the list.
      await page.screenshot({ path: join(folder, "03c-library-game-ports.png") });
      assert.ok(await page.locator(".port-panel").isVisible(), "the list stays open");
      assert.deepEqual(await sideways(page), { by: 0, elements: [] }, "the list scrolls sideways");
      assert.deepEqual(await cutOff(page), [], "the list has text cut off at the edge");
      assert.deepEqual(await squeezed(page), [], "the list has text squeezed to a few letters a line");
      await page.keyboard.press("Escape");
    });

    test("Browse", async () => {
      await tab("Browse");
      await page.locator("article").first().waitFor();
      await check("04-browse");
      await tap(/^Filters/);
      await check("05-browse-filters");
    });

    test("An app's page", async () => {
      await tab("Library");
      // From its game's card, its line in the list.
      await page.locator('.library-grid article[data-game="test-port"] .card-open').click();
      await page.locator(".port-panel .port-open", { hasText: "Test Port" }).click();
      await page.locator(".detail-tabs").waitFor();
      await check("06-app");
      for (const [i, name] of ["Releases", "Player feedback"].entries()) {
        await page.getByRole("tab", { name }).click();
        await check(`0${7 + i}-app-${name.toLowerCase().replace(" ", "-")}`);
      }
    });

    test("Downloads", async () => {
      await tap(/^Downloads/);
      await check("09-downloads");
    });

    test("Settings, and each of its sections", async () => {
      await tap("Settings");
      await check("10-settings");
      const sections = await page.locator(".settings-nav button").allTextContents();
      for (const [i, section] of sections.entries()) {
        await page.locator(".settings-nav button", { hasText: section }).click();
        await check(`${11 + i}-settings-${section.toLowerCase().replaceAll(" ", "-")}`);
        await page.locator(".page-layer:not([hidden]) .back-link").click();
        await page.locator(".settings-nav").waitFor();
      }
    });

    test("Announcements, Add an app and Sign in", async () => {
      await tab("Browse");
      await check("20-announcement");
      // Add an app is the library's; Browse's button is the catalog's submission form.
      for (const [i, [button, name, from]] of [
        ["All announcements", "announcements"],
        [/Add an app/, "add-an-app", "Library"],
        [/^Sign in/, "sign-in"],
      ].entries()) {
        if (from) await tab(from);
        await tap(button);
        await page.getByRole("dialog").waitFor();
        await check(`${21 + i}-${name}`);
        await page.keyboard.press("Escape");
        await page.getByRole("dialog").waitFor({ state: "detached" });
      }
    });

    test("a link opens in the phone's browser, wherever it is", async () => {
      // The site never loads in the launcher's window, should a link get through.
      await page.route("https://quiverlauncher.com/**", (route) => route.fulfill({ contentType: "text/html", body: "<h1>The website</h1>" }));
      const at = page.url();
      await page.locator(".announcement-banner a").tap();
      // In a dialog too, which stops clicks reaching the page.
      await tap("All announcements");
      await page.locator(".announcement-list a").first().tap();
      assert.deepEqual(await s.opened(), ["https://quiverlauncher.com/", "https://quiverlauncher.com/"]);
      assert.equal(page.url(), at);
      await page.keyboard.press("Escape");
    });

    test("no errors in the page", () => assert.deepEqual(s.errors, []));
  });
}
