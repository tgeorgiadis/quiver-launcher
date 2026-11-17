/**
 * The player's own tags (an app's Tags, in its menu and on its page), all in one place: how many apps have each, and
 * renaming or deleting one across the library. Tags from the catalog aren't here: they're the catalog's, not the
 * player's, and nothing here changes them: they're hidden or shown below (CatalogTags).
 */
import { useEffect, useRef, useState } from "react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { Button } from "@quiverlauncher/ui/controls";
import { tagLabel } from "@quiverlauncher/ui";
import { useLauncher, type LibraryItem } from "./store";
import { catalogTagCounts, hiddenEverywhere, hideAllEverywhere, hideEverywhere, hideNewTags } from "./catalog-tags";

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

/**
 * The catalog's tags on the library's apps, to hide on every app or show again, and whether the ones the catalog adds
 * later are hidden as they come. One app's are unticked on the app (its Tags menu, or its page).
 */
export function CatalogTagSettings() {
  const { library, catalog, settings, setSettings, consoleNames } = useLauncher();
  const hidden = settings.hiddenTags;
  const isConsole = (tag: string) => Object.hasOwn(consoleNames, tag);
  const apps = library.flatMap((i) => (!i.removed && catalog[i.id] ? [{ tags: catalog[i.id].allTags ?? catalog[i.id].tags }] : []));
  // By the name each shows under.
  const tags = catalogTagCounts(apps, isConsole, hidden?.everywhere).sort((a, b) => tagLabel(a.tag).localeCompare(tagLabel(b.tag), undefined, { sensitivity: "base" }));
  // Every tag the catalog is known to have here, the library's or not: the ones that aren't new.
  const known = () => [...Object.values(catalog).flatMap((e) => e.allTags ?? e.tags), ...tags.map((x) => x.tag)].filter((tag) => !isConsole(tag));
  if (!tags.length && !hidden?.hideNew) return null;
  const all = tags.map((x) => x.tag);
  const anyShown = all.some((tag) => !hiddenEverywhere(hidden, tag));
  const anyHidden = all.some((tag) => hiddenEverywhere(hidden, tag));
  return (
    <section className="settings-section" aria-label={t`Catalog tags`}>
      <h3>
        <Trans comment="Settings section: the tags apps have from the catalog, which the player can hide">Catalog tags</Trans>
      </h3>
      <p className="muted">
        <Trans>The tags your apps have from the catalog. A hidden tag is left off cards, the tag filter, search and playlists on this computer. To hide one on a single app, untick it in that app's Tags menu.</Trans>
      </p>
      <label className="toggle">
        <input type="checkbox" checked={Boolean(hidden?.hideNew)} onChange={(e) => setSettings((s) => ({ ...s, hiddenTags: hideNewTags(s.hiddenTags, e.target.checked, known()) }))} />
        <Trans>Hide tags the catalog adds from now on</Trans>
      </label>
      {tags.length > 0 && (
        <>
          <p className="tag-all">
            <Button disabled={!anyShown} onClick={() => setSettings((s) => ({ ...s, hiddenTags: hideAllEverywhere(s.hiddenTags, all, true) }))}>
              <Trans comment="Button: hide every catalog tag on every app">Hide all</Trans>
            </Button>
            <Button disabled={!anyHidden} onClick={() => setSettings((s) => ({ ...s, hiddenTags: hideAllEverywhere(s.hiddenTags, all, false) }))}>
              <Trans comment="Button: show every catalog tag again">Show all</Trans>
            </Button>
          </p>
          <ul className="tag-list" aria-label={t`Catalog tags`}>
            {tags.map(({ tag, apps: n }) => {
              const off = hiddenEverywhere(hidden, tag);
              const name = tagLabel(tag);
              return (
                <li key={tag}>
                  <span className={off ? "tag-name muted" : "tag-name"}>
                    <strong>{name}</strong> <span className="muted">{plural(n, { one: "# app", other: "# apps" })}</span>
                  </span>
                  <span className="tag-actions">
                    {off && (
                      <span className="muted">
                        <Trans comment="Beside a catalog tag the player hid on every app">Hidden</Trans>
                      </span>
                    )}
                    <Button variant="text" aria-label={off ? t`Show ${name}` : t`Hide ${name}`} onClick={() => setSettings((s) => ({ ...s, hiddenTags: hideEverywhere(s.hiddenTags, tag, !off) }))}>
                      {off ? <Trans comment="Button: show a hidden catalog tag again">Show</Trans> : <Trans comment="Button: hide a catalog tag on every app">Hide</Trans>}
                    </Button>
                  </span>
                </li>
              );
            })}
          </ul>
        </>
      )}
    </section>
  );
}
