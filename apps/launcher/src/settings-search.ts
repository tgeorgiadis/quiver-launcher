/**
 * Searching Settings (settings-page.tsx): while something is typed, every section shows at once, and the settings in
 * them that don't match are hidden. It reads what's on the screen (names, hints, a menu's choices, and the few extra
 * words a setting gives in `data-search`), so it's in the player's language and finds new settings without a list
 * of them to keep up.
 *
 * A "setting" is one of SETTINGS: a switch, a field, a choice. Settings sit in groups (GROUPS: a section with its
 * heading, the view options…). A group whose heading matches shows whole; otherwise it shows the settings in it that
 * match, or, none of them matching but something else in it (its description, a button), all of it.
 */

/** One setting each. */
const SETTINGS = [
  "label.toggle",
  ".ql-field",
  ".view-choice",
  ".prereleases-setting",
  ".telemetry-setting",
  "label.close-combo",
  ".game-copy-add",
  ".update-choice",
  "table.bindings tbody tr",
  "[data-setting]",
].join(", ");
/** Several settings, under a heading of their own or not. */
// A list of boxes under one label (Show on cards) is a group too: "update" shows its Update available, not every box.
const GROUPS = [
  ".settings-search-group",
  ".settings-section",
  ".controls",
  ".view-options",
  ".view-choice:has(> label.toggle)",
  ".update-choices",
  "table.bindings",
  "table.bindings tbody",
  "[data-settings]",
].join(", ");
/** What a group is called. */
const HEADINGS = ":scope > :is(h2, h3, legend, .view-label)";

/** Marks what the search hides (app.css). */
export const HIDDEN = "data-search-hidden";

/**
 * The words of what was typed, to look for: lower case, without accents, so "theme" finds "Thème" and "è" finds "e".
 */
export function searchWords(query: string): string[] {
  return fold(query).split(/\s+/).filter(Boolean);
}

/** Text as it's searched: lower case, accents taken off, spaces made one. */
export function fold(text: string) {
  return text.normalize("NFD").replace(/\p{M}/gu, "").toLocaleLowerCase().replace(/\s+/g, " ").trim();
}

/**
 * Whether every word is in `text`: as typed, or run together ("fullscreen" finds "Full screen"). `text` is folded
 * (fold) already.
 */
export function matchesAll(words: string[], text: string) {
  const joined = text.replace(/ /g, "");
  return words.every((word) => text.includes(word) || joined.includes(word));
}

/** What a setting is searched by: its text, the choices of a menu in it, and the extra words it gives. */
function textOf(el: Element) {
  const extra = [el, ...el.querySelectorAll("[data-search]")].map((e) => e.getAttribute("data-search") ?? "").join(" ");
  const labels = [...el.querySelectorAll(":is(button, input, select)[aria-label]")].map((e) => e.getAttribute("aria-label")).join(" ");
  return fold(`${el.textContent ?? ""} ${extra} ${labels}`);
}

function headingOf(group: Element) {
  return [...group.querySelectorAll(HEADINGS)].map((h) => h.textContent ?? "").join(" ") + " " + (group.getAttribute("data-search") ?? "");
}

const show = (el: Element) => el.removeAttribute(HIDDEN);
const hide = (el: Element) => el.setAttribute(HIDDEN, "");

/** Everything in `root` shown again. */
export function clearSearch(root: Element) {
  for (const el of root.querySelectorAll(`[${HIDDEN}]`)) show(el);
}

/**
 * Hides what doesn't match in `group`, `context` being the headings it's under (so "controller swap" finds Swap A and B
 * in Controllers). Returns whether anything in it matched.
 */
function filterGroup(group: Element, words: string[], context: string): boolean {
  const here = fold(`${context} ${headingOf(group)}`);
  // Its heading is what was looked for: all of it.
  if (matchesAll(words, here)) {
    clearSearch(group);
    show(group);
    return true;
  }
  if (!matchesAll(words, `${here} ${textOf(group)}`)) {
    hide(group);
    return false;
  }
  // Something in it matches: the settings that do, or all of it if what matches isn't a setting.
  let any = false;
  const parts: [Element, boolean][] = [];
  for (const child of group.children) {
    if (child.matches(GROUPS)) {
      const found = filterGroup(child, words, here);
      parts.push([child, found]);
      any ||= found;
    } else if (child.matches(SETTINGS)) {
      const found = matchesAll(words, `${here} ${textOf(child)}`);
      parts.push([child, found]);
      any ||= found;
      if (found) clearSearch(child);
    } else if (child.querySelector(SETTINGS)) {
      // A wrapper (a list, a row) around settings: its settings, as though they were the group's own.
      const found = filterGroup(child, words, here);
      parts.push([child, found]);
      any ||= found;
    }
  }
  show(group);
  if (!any) {
    clearSearch(group);
    return true;
  }
  for (const [el, found] of parts) (found ? show : hide)(el);
  // A group without a heading of its own (the controller settings) has hints and buttons for what's in it, which
  // mean nothing beside another setting; one with a heading has a description under it, which stays.
  if (!group.querySelector(":scope > :is(h2, h3, legend)")) {
    for (const child of group.children) if (child.matches("p, button, .row") && !parts.some(([el]) => el === child)) hide(child);
  }
  return true;
}

/**
 * Filters `root`'s groups (one a section, each a .settings-search-group with its name in data-section's heading) by
 * `query`, returning the sections something matched in. An empty query shows everything.
 */
export function filterSettings(root: Element, query: string): string[] {
  const words = searchWords(query);
  if (!words.length) {
    clearSearch(root);
    return [];
  }
  const found: string[] = [];
  for (const section of root.querySelectorAll<HTMLElement>(":scope > .settings-search-group")) {
    if (filterGroup(section, words, "")) found.push(section.dataset.section ?? "");
  }
  return found;
}

/** Where the words are in `root`'s shown text, for the highlight (CSS.highlights, where there is it). */
export function highlightMatches(root: Element, query: string) {
  const highlights = (globalThis as { CSS?: { highlights?: Map<string, unknown> } }).CSS?.highlights;
  const Highlight = (globalThis as { Highlight?: new (...ranges: Range[]) => unknown }).Highlight;
  if (!highlights || !Highlight) return;
  const words = searchWords(query);
  if (!words.length) return void highlights.delete("settings-search");
  const ranges: Range[] = [];
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
    acceptNode: (node) => (node.parentElement?.closest(`[${HIDDEN}], option, select, script, style`) ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT),
  });
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    const text = node.textContent ?? "";
    // Folded letter by letter, so a place in it is a place in the text (an accent off "é" leaves one letter).
    const folded = [...text].map((c) => fold(c) || c).join("");
    if (folded.length !== text.length) continue;
    for (const word of words) {
      for (let at = folded.indexOf(word); at >= 0; at = folded.indexOf(word, at + word.length)) {
        const range = document.createRange();
        range.setStart(node, at);
        range.setEnd(node, at + word.length);
        ranges.push(range);
      }
    }
  }
  highlights.set("settings-search", new Highlight(...ranges));
}
