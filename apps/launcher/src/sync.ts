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

/**
 * A saved library filter (Library shelves). It shows apps that match every
 * part it sets: any of its tags (the player's or the catalog's), any of its
 * consoles, and installed or not. A hand-picked collection is one tag.
 */
export type Collection = {
  /** Made here, kept once it syncs. */
  key: string;
  name: string;
  tags: string[];
  consoles: string[];
  installed?: "yes" | "no";
  order: number;
  removed?: boolean;
  /** Changed here and not yet saved to the account. */
  pending?: boolean;
  /** The account this collection last synced with. */
  account?: string;
};
/** A collection in the account (libraryCollections.list on the site). */
export type ServerCollection = Omit<Collection, "pending" | "account"> & { removed: boolean; updatedAt: number };

/** Signing in brings this device's collections along; the account's win unless changed here. */
export function applyServerCollections(local: Collection[], server: ServerCollection[], account: string): Collection[] {
  const mine = new Map(
    local.flatMap((c) => (!c.account ? [{ ...c, account, pending: true }] : c.account === account ? [c] : [])).map((c) => [c.key, c]),
  );
  for (const { updatedAt: _, ...s } of server) {
    if (mine.get(s.key)?.pending) continue;
    if (s.removed) mine.delete(s.key);
    else mine.set(s.key, { ...s, removed: undefined, account });
  }
  return [...mine.values()];
}

/** Whether an app belongs on a collection's shelf. */
export function inCollection(c: Pick<Collection, "tags" | "consoles" | "installed">, app: { tags: string[]; consoles: string[]; installed: boolean }) {
  const tags = new Set(app.tags.map((t) => t.toLowerCase()));
  return (
    (!c.tags.length || c.tags.some((t) => tags.has(t.toLowerCase()))) &&
    (!c.consoles.length || c.consoles.some((t) => app.consoles.includes(t))) &&
    (!c.installed || (c.installed === "yes") === app.installed)
  );
}
