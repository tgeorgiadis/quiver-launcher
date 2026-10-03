/**
 * Library sync with a signed-in account (plan: "Library and sync"). The
 * library lives on the device; these pure steps fold the account's copy in
 * and pick out what still has to be saved to it.
 */
import type { LibraryItem } from "./store";

/** An app in the account's library (library.list on the site). */
export type ServerItem = {
  entryId: string;
  slug: string;
  name?: string;
  artwork?: string;
  tags?: string[];
  removed: boolean;
  updatedAt: number;
};
/** A change for library.save: only the fields it names; null clears one. */
export type Change = {
  entryId: string;
  name?: string | null;
  artwork?: string | null;
  tags?: string[] | null;
  removed?: boolean;
};

/** First sight of an account: this device's apps join it, removals meant for another account are dropped. */
export function joinAccount(local: LibraryItem[], account: string): LibraryItem[] {
  return local.flatMap((item) => {
    if (item.account === account) return [item];
    if (item.removed) return [];
    const o = item.overrides ?? {};
    const pending = { added: true, name: !!o.name, cover: !!o.cover, tags: !!o.tags };
    return [{ ...item, account, pending: { ...item.pending, ...pending } }];
  });
}

/** The account's values win, except fields changed here and not saved yet. */
export function applyServer(
  local: LibraryItem[],
  server: ServerItem[],
  account: string,
  installed: (id: string) => boolean,
): LibraryItem[] {
  const items = new Map(local.map((i) => [i.id, i]));
  for (const s of server) {
    const mine = items.get(s.entryId);
    const pending = mine?.pending ?? {};
    if (s.removed) {
      if (!mine || mine.account !== account || pending.added || mine.removed) continue;
      if (installed(mine.id)) items.set(mine.id, { ...mine, removedElsewhere: true });
      else items.delete(mine.id);
      continue;
    }
    if (mine?.removed) continue;
    const overrides = { ...mine?.overrides };
    if (!pending.name) overrides.name = s.name;
    if (!pending.cover) overrides.cover = s.artwork;
    if (!pending.tags) overrides.tags = s.tags;
    items.set(s.entryId, {
      addedAt: s.updatedAt,
      ...mine,
      id: s.entryId,
      slug: s.slug,
      account,
      overrides,
      removedElsewhere: undefined,
    });
  }
  return [...items.values()];
}

/** What still has to reach the account. */
export function changesFrom(local: LibraryItem[], account: string): Change[] {
  return local
    .filter((i) => i.pending && i.account === account)
    .map(({ id, pending = {}, overrides = {}, removed }) => ({
      entryId: id,
      ...(removed ? { removed: true } : {}),
      ...(pending.name ? { name: overrides.name ?? null } : {}),
      ...(pending.cover ? { artwork: overrides.cover ?? null } : {}),
      ...(pending.tags ? { tags: overrides.tags ?? null } : {}),
    }));
}
