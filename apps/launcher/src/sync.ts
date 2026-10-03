/**
 * Library sync with a signed-in account (plan: "Library and sync"). The
 * library lives on the device; these pure steps fold the account's copy in
 * and pick out what still has to be saved to it. Installs are kept apart and
 * never touched here.
 */
import type { CustomApp, LibraryItem } from "./store";

/** An app in the account's library (library.list on the site). */
export type ServerItem = {
  /** The catalog entry's id, or "github:owner/repo" for a custom app. */
  key: string;
  entryId?: string;
  slug?: string;
  custom?: CustomApp;
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
  key: string;
  add?: true;
  /** What an add stands for: a catalog entry or a custom app. */
  entryId?: string;
  custom?: CustomApp;
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
    const mine = items.get(s.key);
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
    items.set(s.key, {
      addedAt: s.updatedAt,
      ...mine,
      id: s.key,
      slug: s.slug ?? mine?.slug ?? "",
      ...(s.custom ? { custom: s.custom } : {}),
      account,
      overrides,
    });
  }
  return [...items.values()];
}

/** What still has to reach the account. */
export function changesFrom(local: LibraryItem[], account: string): Change[] {
  return local
    .filter((i) => i.pending && i.account === account)
    .map(({ id, pending = {}, overrides = {}, removed, custom }): Change => {
      if (removed) return { key: id, removed: true };
      const added = custom ? { add: true as const, custom } : { add: true as const, entryId: id };
      const change: Change = { key: id, ...(pending.added ? added : {}) };
      const fields = [["name", "name"], ["cover", "artUrl"], ["tags", "tags"]] as const;
      for (const [mine, theirs] of fields) {
        const value = overrides[mine];
        // An add sends what's set; an edit sends what changed, null to clear.
        if (pending.added ? value !== undefined : pending[mine]) Object.assign(change, { [theirs]: value ?? null });
      }
      return change;
    });
}
