/**
 * Library sync with a signed-in account (plan: "Library and sync"). The
 * library lives on the device; these pure steps fold the account's copy in
 * and pick out what still has to be saved to it. Installs are kept apart and
 * never touched here.
 */
import type { LibraryItem } from "./store";

/** An app in the account's library (library.list on the site). */
export type ServerItem = {
  entryId: string;
  slug: string;
  name?: string;
  artUrl?: string;
  tags?: string[];
  removed: boolean;
  updatedAt: number;
};
/**
 * A change for library.save. An add fills only fields the account left
 * empty; an edit sets the fields it names, and null clears one.
 */
export type Change = {
  entryId: string;
  add?: true;
  name?: string | null;
  artUrl?: string | null;
  tags?: string[] | null;
  removed?: true;
};

/**
 * Signing in: the guest library's apps join the account. Apps another
 * account left here go with it (signing out takes the library along).
 */
export function joinAccount(local: LibraryItem[], account: string): LibraryItem[] {
  return local.flatMap((item) => {
    if (item.account) return item.account === account ? [item] : [];
    return [{ ...item, account, pending: { added: true } }];
  });
}

/** The account's values win, except fields changed here and not saved yet. */
export function applyServer(local: LibraryItem[], server: ServerItem[], account: string): LibraryItem[] {
  const items = new Map(local.map((i) => [i.id, i]));
  for (const s of server) {
    const mine = items.get(s.entryId);
    const pending = mine?.pending ?? {};
    if (s.removed) {
      // Removed elsewhere: it leaves the library here; an install stays.
      if (mine && !pending.added && !mine.removed) items.delete(mine.id);
      continue;
    }
    if (mine?.removed) continue;
    const overrides = { ...mine?.overrides };
    if (!pending.added) {
      if (!pending.name) overrides.name = s.name;
      if (!pending.cover) overrides.cover = s.artUrl;
      if (!pending.tags) overrides.tags = s.tags;
    }
    items.set(s.entryId, { addedAt: s.updatedAt, ...mine, id: s.entryId, slug: s.slug, account, overrides });
  }
  return [...items.values()];
}

/** What still has to reach the account. */
export function changesFrom(local: LibraryItem[], account: string): Change[] {
  return local
    .filter((i) => i.pending && i.account === account)
    .map(({ id, pending = {}, overrides = {}, removed }): Change => {
      if (removed) return { entryId: id, removed: true };
      const change: Change = { entryId: id, ...(pending.added ? { add: true } : {}) };
      const fields = [["name", "name"], ["cover", "artUrl"], ["tags", "tags"]] as const;
      for (const [mine, theirs] of fields) {
        const value = overrides[mine];
        // An add sends what's set; an edit sends what changed, null to clear.
        if (pending.added ? value !== undefined : pending[mine]) Object.assign(change, { [theirs]: value ?? null });
      }
      return change;
    });
}
