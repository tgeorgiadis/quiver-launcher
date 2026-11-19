/**
 * Tags, all in one place: a line in Settings, Library (TagSettings) and a window of their own (TagsDialog), which lists
 * them in two tabs. The player's own tags (an app's Tags, in its menu and on its page), with how many apps have each,
 * to rename or delete across the library; and the catalog's, which aren't the player's and can't be changed, only
 * hidden on this computer (catalog-tags.ts).
 */
import { useEffect, useMemo, useRef, useState } from "react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { Button } from "@quiverlauncher/ui/controls";
import { tagLabel } from "@quiverlauncher/ui";
import { useLauncher, type LibraryItem } from "./store";
import { catalogTagCounts, hiddenEverywhere, hideAllEverywhere, hideEverywhere, hideNewTags, listedTags, type TagFilter, type TagSort } from "./catalog-tags";
import { Dialog } from "./library";

/** The tags window's tabs: the player's own tags, and the catalog's. */
type TagsTab = "yours" | "catalog";

/**
 * Asks for the player's tags, from an app's Tags menu: the app (App.tsx) opens Settings' Library section, and the tags
 * window opens there on the player's own (TagSettings, whether it was showing already or not).
 */
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
 * Tags in Settings: how many of the player's own there are and how many the catalog's (and how many of those are
 * hidden), the way into the window that lists them (TagsDialog), and whether the ones the catalog adds later are
 * hidden as they come.
 */
export function TagSettings() {
  const { library, settings, setSettings } = useLauncher();
  const hidden = settings.hiddenTags;
  const own = tagCounts(library);
  const { tags: theirs, known } = useCatalogTags();
  const [open, setOpen] = useState<TagsTab | null>(null);
  const section = useRef<HTMLElement>(null);
  // Asked for from an app's Tags menu: on the player's own, as Settings opens here or while it shows already.
  useEffect(() => {
    const take = () => {
      if (!asked) return;
      asked = false;
      section.current?.scrollIntoView({ block: "start" });
      setOpen("yours");
    };
    take();
    window.addEventListener(MANAGE_TAGS, take);
    return () => window.removeEventListener(MANAGE_TAGS, take);
  }, []);
  const off = theirs.filter(({ tag }) => hiddenEverywhere(hidden, tag)).length;
  const any = own.length > 0 || theirs.length > 0;
  return (
    <section ref={section} className="settings-section" aria-label={t`Tags`}>
      <h3>
        <Trans comment="Settings section: the player's own tags and the catalog's">Tags</Trans>
      </h3>
      <p className="muted">
        {any ? (
          <Trans>Your own tags, which you can rename or delete on every app at once, and the tags your apps have from the catalog, which you can hide on this computer.</Trans>
        ) : (
          <Trans>You haven't tagged any apps yet. Add tags from an app's menu, under Tags.</Trans>
        )}
      </p>
      {any && (
        <div className="tags-summary">
          <span role="status" className="tags-summary-counts">
            <span>
              <strong>{own.length}</strong> <span className="muted">{plural(own.length, { one: "of your tags", other: "of your tags" })}</span>
            </span>
            <span>
              <strong>{theirs.length}</strong>{" "}
              <span className="muted">
                {off === 0
                  ? t({ message: "from the catalog", comment: "After how many catalog tags the library's apps have, when none is hidden" })
                  : plural(off, { one: "from the catalog, # hidden", other: "from the catalog, # hidden" })}
              </span>
            </span>
          </span>
          <Button variant="main" onClick={() => setOpen(own.length || !theirs.length ? "yours" : "catalog")}>
            <Trans comment="Button: opens the window listing the player's tags and the catalog's">Manage tags</Trans>
          </Button>
        </div>
      )}
      {(theirs.length > 0 || hidden?.hideNew) && (
        <label className="toggle">
          <input type="checkbox" checked={Boolean(hidden?.hideNew)} onChange={(e) => setSettings((s) => ({ ...s, hiddenTags: hideNewTags(s.hiddenTags, e.target.checked, known()) }))} />
          <Trans>Hide tags the catalog adds from now on</Trans>
        </label>
      )}
      {open && <TagsDialog tab={open} onClose={() => setOpen(null)} />}
    </section>
  );
}

/** The tags window: the player's own tags and the catalog's, a tab each, with how many of each there are. */
export function TagsDialog({ tab: first, onClose }: { tab: TagsTab; onClose: () => void }) {
  const { library } = useLauncher();
  const [tab, setTab] = useState(first);
  // The search field takes the focus as the window opens, to type at once; a tab chosen after keeps it.
  const [chose, setChose] = useState(false);
  const own = tagCounts(library).length;
  const theirs = useCatalogTags().tags.length;
  const TABS: [TagsTab, string, number][] = [
    ["yours", t({ message: "Your tags", comment: "Tab: the player's own tags" }), own],
    ["catalog", t({ message: "From the catalog", comment: "Tab: the tags apps have from the catalog" }), theirs],
  ];
  return (
    <Dialog label={t`Manage tags`} className="tags-window" onClose={onClose}>
      <div className="tags-head">
        <h2>
          <Trans>Manage tags</Trans>
        </h2>
        <Button onClick={onClose}>
          <Trans>Close</Trans>
        </Button>
      </div>
      <div className="detail-tabs tags-tabs" role="tablist" aria-label={t`Tags`}>
        {TABS.map(([key, label, n]) => (
          <button key={key} type="button" role="tab" aria-selected={tab === key} onClick={() => (setTab(key), setChose(true))}>
            {label}
            <span className="tab-count">{n}</span>
          </button>
        ))}
      </div>
      {/* Each its own, so its search and filters start over. */}
      {tab === "yours" ? <OwnTags key="yours" focus={!chose} /> : <CatalogTags key="catalog" focus={!chose} />}
    </Dialog>
  );
}

/** What a row is doing: being renamed (with the name typed so far), or asking whether to delete it. */
type Editing = { tag: string; renaming: string } | { tag: string; deleting: true };

/** With `focus`, focuses the search field as the tab shows, to type at once; but not on a phone, whose keyboard would cover the list. */
function useSearchFocus(focus: boolean) {
  const { config } = useLauncher();
  const field = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (focus && config.os !== "android" && config.os !== "ios") field.current?.focus({ preventScroll: true });
  }, [focus, config.os]);
  return field;
}

/** Search and sort, as both tabs have them. */
function Find({ field, search, setSearch, sort, setSort }: { field: React.RefObject<HTMLInputElement | null>; search: string; setSearch: (s: string) => void; sort: TagSort; setSort: (s: TagSort) => void }) {
  return (
    <div className="tags-find">
      <input ref={field} type="search" placeholder={t`Search tags`} aria-label={t`Search tags`} value={search} onChange={(e) => setSearch(e.target.value)} />
      <select aria-label={t`Sort tags`} value={sort} onChange={(e) => setSort(e.target.value as TagSort)}>
        <option value="name">{t({ message: "By name", comment: "Sorts tags by name" })}</option>
        <option value="apps">{t({ message: "Most apps", comment: "Sorts tags, the ones on the most apps first" })}</option>
      </select>
    </div>
  );
}

/** The player's own tags, searched and sorted, each renamed or deleted on every app that has it. */
function OwnTags({ focus }: { focus: boolean }) {
  const { library, renameTag } = useLauncher();
  const tags = tagCounts(library);
  const [editing, setEditing] = useState<Editing | null>(null);
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<TagSort>("name");
  const field = useSearchFocus(focus);
  const done = () => setEditing(null);
  const listed = listedTags(tags, undefined, { search, sort });
  return (
    <>
      <div className="tags-tools">
        <p className="muted">
          <Trans>The tags you gave your apps, including any brought over from Quiver Launcher 3. Renaming or deleting one changes it on every app that has it. The catalog's own tags aren't affected.</Trans>
        </p>
        {tags.length > 0 && <Find field={field} search={search} setSearch={setSearch} sort={sort} setSort={setSort} />}
      </div>
      <div className="tags-scroll">
        {!tags.length ? (
          <p className="muted">
            <Trans>You haven't tagged any apps yet. Add tags from an app's menu, under Tags.</Trans>
          </p>
        ) : !listed.length ? (
          <p className="muted" role="status">
            <Trans>No tags match your search.</Trans>
          </p>
        ) : (
          <ul className="own-tags" aria-label={t`Your tags`}>
            {listed.map(({ tag, apps: n }) => {
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
                        // Escape leaves the rename, not the window.
                        onKeyDown={(e) => e.key === "Escape" && (e.stopPropagation(), e.nativeEvent.stopImmediatePropagation(), done())}
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
                    <span className="own-tag-ask">{t`Remove “${tag}” from ${count}?`}</span>
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
      </div>
    </>
  );
}

/**
 * Every catalog tag on the library's apps: ticked shows it, unticked hides it on every app. Searched by name, narrowed
 * to those showing or hidden, by name or by how many apps have them, and hidden or shown all at once, as many as are
 * listed. A controller moves through it as a grid; Confirm ticks.
 */
function CatalogTags({ focus }: { focus: boolean }) {
  const { settings, setSettings } = useLauncher();
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
  const field = useSearchFocus(focus);
  const FILTERS: [TagFilter, string][] = [
    ["all", t({ message: "All", comment: "Lists every catalog tag" })],
    ["shown", t({ message: "Showing", comment: "Lists the catalog tags that show" })],
    ["hidden", t({ message: "Hidden", comment: "Lists the catalog tags hidden on every app" })],
  ];
  return (
    <>
      <div className="tags-tools">
        <p className="muted">
          <Trans>Untick a tag to hide it on every app on this computer. Tick it to show it again. To hide one on a single app, untick it in that app's Tags menu.</Trans>
        </p>
        <Find field={field} search={search} setSearch={(v) => (setSearch(v), relist())} sort={sort} setSort={setSort} />
        <div className="tags-bar">
          <div className="chips" role="group" aria-label={t`Which tags`}>
            {FILTERS.map(([key, label]) => (
              <button key={key} type="button" className={`chip${filter === key ? " on" : ""}`} aria-pressed={filter === key} onClick={() => (setFilter(key), relist())}>
                {label} <span className="chip-count">{counts[key]}</span>
              </button>
            ))}
          </div>
          <div className="tags-bulk">
            <Button disabled={!anyHidden} onClick={() => setAll(false)}>
              {narrowed ? <Trans comment="Button: show again every catalog tag the search or filter lists">Show these</Trans> : <Trans comment="Button: show every catalog tag again">Show all</Trans>}
            </Button>
            <Button disabled={!anyShown} onClick={() => setAll(true)}>
              {narrowed ? <Trans comment="Button: hide every catalog tag the search or filter lists">Hide these</Trans> : <Trans comment="Button: hide every catalog tag on every app">Hide all</Trans>}
            </Button>
          </div>
        </div>
      </div>
      <div className="tags-scroll">
        {listed.length ? (
          <ul className="catalog-tags" aria-label={t`Catalog tags`}>
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
    </>
  );
}
