/**
 * The player's own tags (an app's Tags, in its menu and on its page), all in one place: how many apps have each, and
 * renaming or deleting one across the library. Tags from the catalog aren't here: they're the catalog's, not the
 * player's, and nothing here changes them: they're hidden or shown below (CatalogTags).
 */
import { useEffect, useMemo, useRef, useState } from "react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { Button } from "@quiverlauncher/ui/controls";
import { tagLabel } from "@quiverlauncher/ui";
import { useLauncher, type LibraryItem } from "./store";
import { catalogTagCounts, hiddenEverywhere, hideAllEverywhere, hideEverywhere, hideNewTags, listedTags, type TagFilter, type TagSort } from "./catalog-tags";
import { Dialog } from "./library";

/** Asks for Settings' Library section, where the player's tags are: the app (App.tsx) opens it. */
export const MANAGE_TAGS = "quiver-manage-tags";
let asked = false;
export const manageTags = () => {
  asked = true;
  window.dispatchEvent(new Event(MANAGE_TAGS));
};

/** Each of the player's tags, as first written, with how many apps in the library have it; by name. */
export function tagCounts(library: LibraryItem[]): { tag: string; apps: number }[] {
  const counts = new Map<string, { tag: string; apps: number }>();
  for (const item of library) {
    if (item.removed) continue;
    for (const tag of item.overrides?.tags ?? []) {
      const key = tag.toLowerCase();
      const had = counts.get(key);
      counts.set(key, { tag: had?.tag ?? tag, apps: (had?.apps ?? 0) + 1 });
    }
  }
  return [...counts.values()].sort((a, b) => a.tag.localeCompare(b.tag, undefined, { sensitivity: "base" }));
}

/** What a row is doing: being renamed (with the name typed so far), or asking whether to delete it. */
type Editing = { tag: string; renaming: string } | { tag: string; deleting: true };

export function TagSettings() {
  const { library, renameTag } = useLauncher();
  const tags = tagCounts(library);
  const [editing, setEditing] = useState<Editing | null>(null);
  const done = () => setEditing(null);
  const section = useRef<HTMLElement>(null);
  // Opened from an app's Tags menu: scrolled to, past how the library looks.
  useEffect(() => {
    if (!asked) return;
    asked = false;
    section.current?.scrollIntoView({ block: "start" });
  }, []);
  return (
    <section ref={section} className="settings-section" aria-label={t`Your tags`}>
      <h3>
        <Trans>Your tags</Trans>
      </h3>
      <p className="muted">
        {tags.length ? (
          <Trans>The tags you gave your apps, including any brought over from Quiver Launcher 3. Renaming or deleting one changes it on every app that has it. The catalog's own tags aren't affected.</Trans>
        ) : (
          <Trans>You haven't tagged any apps yet. Add tags from an app's menu, under Tags.</Trans>
        )}
      </p>
      {tags.length > 0 && (
        <ul className="tag-list" aria-label={t`Your tags`}>
          {tags.map(({ tag, apps: n }) => {
            const count = plural(n, { one: "# app", other: "# apps" });
            const mine = editing?.tag === tag ? editing : null;
            if (mine && "renaming" in mine) {
              const save = () => {
                const name = mine.renaming.trim();
                if (name && name !== tag) renameTag(tag, name);
                done();
              };
              return (
                <li key={tag}>
                  <form
                    className="tag-rename"
                    onSubmit={(e) => {
                      e.preventDefault();
                      save();
                    }}
                  >
                    <input
                      autoFocus
                      aria-label={t`New name for ${tag}`}
                      maxLength={50}
                      value={mine.renaming}
                      onChange={(e) => setEditing({ tag, renaming: e.target.value })}
                      onKeyDown={(e) => e.key === "Escape" && (e.stopPropagation(), done())}
                    />
                    <Button type="submit" variant="main" disabled={!mine.renaming.trim()}>
                      <Trans>Save</Trans>
                    </Button>
                    <Button onClick={done}>
                      <Trans>Cancel</Trans>
                    </Button>
                  </form>
                </li>
              );
            }
            if (mine) {
              return (
                <li key={tag} role="group" aria-label={t`Delete ${tag}`}>
                  <span>{t`Remove “${tag}” from ${count}?`}</span>
                  <span className="tag-actions">
                    <Button
                      variant="danger"
                      onClick={() => {
                        renameTag(tag);
                        done();
                      }}
                    >
                      <Trans comment="Button: confirm deleting one of the player's tags from all their apps">Delete</Trans>
                    </Button>
                    <Button onClick={done}>
                      <Trans>Cancel</Trans>
                    </Button>
                  </span>
                </li>
              );
            }
            return (
              <li key={tag}>
                <span className="tag-name">
                  <strong>{tag}</strong> <span className="muted">{count}</span>
                </span>
                <span className="tag-actions">
                  <Button variant="text" aria-label={t`Rename ${tag}`} onClick={() => setEditing({ tag, renaming: tag })}>
                    <Trans comment="Button: rename one of the player's tags">Rename</Trans>
                  </Button>
                  <Button variant="text" aria-label={t`Delete ${tag}`} onClick={() => setEditing({ tag, deleting: true })}>
                    <Trans comment="Button: delete one of the player's tags from all their apps">Delete</Trans>
                  </Button>
                </span>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

/** The catalog's tags on the library's apps, each once with how many apps have it, by the name each shows under. */
function useCatalogTags() {
  const { library, catalog, settings, consoleNames } = useLauncher();
  const hidden = settings.hiddenTags;
  return useMemo(() => {
    const isConsole = (tag: string) => Object.hasOwn(consoleNames, tag);
    const apps = library.flatMap((i) => (!i.removed && catalog[i.id] ? [{ tags: catalog[i.id].allTags ?? catalog[i.id].tags }] : []));
    const tags = catalogTagCounts(apps, isConsole, hidden?.everywhere).sort((a, b) => tagLabel(a.tag).localeCompare(tagLabel(b.tag), undefined, { sensitivity: "base" }));
    // Every tag the catalog is known to have here, the library's or not: the ones that aren't new.
    const known = () => [...Object.values(catalog).flatMap((e) => e.allTags ?? e.tags), ...tags.map((x) => x.tag)].filter((tag) => !isConsole(tag));
    return { tags, known };
  }, [library, catalog, consoleNames, hidden?.everywhere]);
}

/**
 * The catalog's tags in Settings: how many the library's apps have and how many are hidden, whether the ones the
 * catalog adds later are hidden as they come, and the way into the list of them all (CatalogTagsDialog), which is
 * long. One app's are unticked on the app (its Tags menu, or its page).
 */
export function CatalogTagSettings() {
  const { settings, setSettings } = useLauncher();
  const hidden = settings.hiddenTags;
  const { tags, known } = useCatalogTags();
  const [open, setOpen] = useState(false);
  if (!tags.length && !hidden?.hideNew) return null;
  const off = tags.filter(({ tag }) => hiddenEverywhere(hidden, tag)).length;
  return (
    <section className="settings-section" aria-label={t`Catalog tags`}>
      <h3>
        <Trans comment="Settings section: the tags apps have from the catalog, which the player can hide">Catalog tags</Trans>
      </h3>
      <p className="muted">
        <Trans>The tags your apps have from the catalog. A hidden tag is left off cards, the tag filter, search and playlists on this computer. To hide one on a single app, untick it in that app's Tags menu.</Trans>
      </p>
      {tags.length > 0 && (
        <div className="catalog-tags-summary">
          <span role="status">
            <strong>{plural(tags.length, { one: "# tag", other: "# tags" })}</strong>{" "}
            <span className="muted">
              {off === 0
                ? t({ message: "all showing", comment: "After how many catalog tags the library's apps have, when none is hidden" })
                : off === tags.length
                  ? t({ message: "all hidden", comment: "After how many catalog tags the library's apps have, when every one is hidden" })
                  : plural(off, { one: "# hidden", other: "# hidden" })}
            </span>
          </span>
          <Button variant="main" onClick={() => setOpen(true)}>
            <Trans comment="Button: opens the list of the catalog's tags, to hide or show them">Manage catalog tags</Trans>
          </Button>
        </div>
      )}
      <label className="toggle">
        <input type="checkbox" checked={Boolean(hidden?.hideNew)} onChange={(e) => setSettings((s) => ({ ...s, hiddenTags: hideNewTags(s.hiddenTags, e.target.checked, known()) }))} />
        <Trans>Hide tags the catalog adds from now on</Trans>
      </label>
      {open && <CatalogTagsDialog onClose={() => setOpen(false)} />}
    </section>
  );
}

/**
 * Every catalog tag on the library's apps, in a window of its own: ticked shows it, unticked hides it on every app.
 * Searched by name, narrowed to those showing or hidden, by name or by how many apps have them, and hidden or shown
 * all at once, as many as are listed. A controller moves through it as a grid; Confirm ticks.
 */
export function CatalogTagsDialog({ onClose }: { onClose: () => void }) {
  const { settings, setSettings, config } = useLauncher();
  const hidden = settings.hiddenTags;
  const { tags } = useCatalogTags();
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState<TagFilter>("all");
  const [sort, setSort] = useState<TagSort>("name");
  // Which are hidden as the list was last searched or filtered: a tag ticked or unticked stays in the list it was in
  // (Hidden, say) until then, rather than going from under the pointer or the controller's focus.
  const [asListed, setAsListed] = useState(hidden);
  const relist = () => setAsListed(hidden);
  const listed = listedTags(tags, asListed, { search, filter, sort, label: tagLabel });
  const off = tags.filter(({ tag }) => hiddenEverywhere(hidden, tag)).length;
  const counts: Record<TagFilter, number> = { all: tags.length, shown: tags.length - off, hidden: off };
  const names = listed.map((x) => x.tag);
  const anyShown = names.some((tag) => !hiddenEverywhere(hidden, tag));
  const anyHidden = names.some((tag) => hiddenEverywhere(hidden, tag));
  // All of them, or only those listed, as the buttons say.
  const narrowed = Boolean(search.trim()) || filter !== "all";
  const setAll = (hide: boolean) => setSettings((s) => ({ ...s, hiddenTags: hideAllEverywhere(s.hiddenTags, names, hide) }));
  const searchField = useRef<HTMLInputElement>(null);
  // Typing finds a tag at once, but not on a phone, whose keyboard would cover the list.
  useEffect(() => {
    if (config.os !== "android" && config.os !== "ios") searchField.current?.focus({ preventScroll: true });
  }, [config.os]);
  const FILTERS: [TagFilter, string][] = [
    ["all", t({ message: "All", comment: "Lists every catalog tag" })],
    ["shown", t({ message: "Showing", comment: "Lists the catalog tags that show" })],
    ["hidden", t({ message: "Hidden", comment: "Lists the catalog tags hidden on every app" })],
  ];
  return (
    <Dialog label={t`Catalog tags`} className="catalog-tags-window" onClose={onClose}>
      <div className="catalog-tags-head">
        <h2>
          <Trans>Catalog tags</Trans>
        </h2>
        <Button onClick={onClose}>
          <Trans>Close</Trans>
        </Button>
      </div>
      <div className="catalog-tags-tools">
        <p className="muted">
          <Trans>Untick a tag to hide it on every app on this computer. Tick it to show it again.</Trans>
        </p>
        <div className="catalog-tags-find">
          <input ref={searchField} type="search" placeholder={t`Search tags`} aria-label={t`Search tags`} value={search} onChange={(e) => (setSearch(e.target.value), relist())} />
          <select aria-label={t`Sort tags`} value={sort} onChange={(e) => setSort(e.target.value as TagSort)}>
            <option value="name">{t({ message: "By name", comment: "Sorts the catalog's tags by name" })}</option>
            <option value="apps">{t({ message: "Most apps", comment: "Sorts the catalog's tags, the ones on the most apps first" })}</option>
          </select>
        </div>
        <div className="catalog-tags-bar">
          <div className="chips" role="group" aria-label={t`Which tags`}>
            {FILTERS.map(([key, label]) => (
              <button key={key} type="button" className={`chip${filter === key ? " on" : ""}`} aria-pressed={filter === key} onClick={() => (setFilter(key), relist())}>
                {label} <span className="chip-count">{counts[key]}</span>
              </button>
            ))}
          </div>
          <div className="catalog-tags-bulk">
            <Button disabled={!anyHidden} onClick={() => setAll(false)}>
              {narrowed ? <Trans comment="Button: show again every catalog tag the search or filter lists">Show these</Trans> : <Trans comment="Button: show every catalog tag again">Show all</Trans>}
            </Button>
            <Button disabled={!anyShown} onClick={() => setAll(true)}>
              {narrowed ? <Trans comment="Button: hide every catalog tag the search or filter lists">Hide these</Trans> : <Trans comment="Button: hide every catalog tag on every app">Hide all</Trans>}
            </Button>
          </div>
        </div>
      </div>
      <div className="catalog-tags-list">
        {listed.length ? (
          <ul aria-label={t`Catalog tags`}>
            {listed.map(({ tag, apps: n }) => {
              const shown = !hiddenEverywhere(hidden, tag);
              const name = tagLabel(tag);
              return (
                <li key={tag}>
                  <label className={shown ? "catalog-tag" : "catalog-tag off"}>
                    <input type="checkbox" checked={shown} onChange={(e) => setSettings((s) => ({ ...s, hiddenTags: hideEverywhere(s.hiddenTags, tag, !e.target.checked) }))} />
                    <span className="catalog-tag-name">{name}</span>
                    <span className="catalog-tag-count">{plural(n, { one: "# app", other: "# apps" })}</span>
                  </label>
                </li>
              );
            })}
          </ul>
        ) : (
          <p className="muted" role="status">
            {search.trim() ? <Trans>No tags match your search.</Trans> : filter === "hidden" ? <Trans>No tags are hidden.</Trans> : <Trans>Every tag is hidden.</Trans>}
          </p>
        )}
      </div>
    </Dialog>
  );
}
