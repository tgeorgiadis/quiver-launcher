/**
 * How Browse's catalog cards look on this computer: box art, or the wide art Quiver Launcher 3's cards had, and which
 * of their details show. Browse's shelves and grid, an app's More like this and a game's ways to play have
 * them; TV mode keeps its own. Kept in settings.json with the other preferences for this device.
 */
import { msg } from "@lingui/core/macro";
import type { MessageDescriptor } from "@lingui/core";
import type { Entry } from "@quiverlauncher/api";
import { CARD_DETAILS, type CardDetail, type CardLook } from "@quiverlauncher/ui";

export type BrowseCards = { look: CardLook; details: CardDetail[] };

/** Box art, with every detail but New: its kinds, and both what players said and when it was updated. */
export const DEFAULT_BROWSE_CARDS: BrowseCards = { look: "box", details: ["subtitle", "kinds", "rating", "updated", "platforms", "ai"] };

// The choices' names put in the language when shown (i18n._), not here, where the module loads before any is picked.
export const CARD_LOOKS: [CardLook, MessageDescriptor][] = [
  ["box", msg({ message: "Box art", comment: "Browse card style: the portrait box art over the catalog's details" })],
  ["wide", msg({ message: "Wide art", comment: "Browse card style: the wide art over the catalog's details, as Quiver Launcher 3 had" })],
];
export const DETAIL_NAMES: Record<CardDetail, MessageDescriptor> = {
  subtitle: msg({ message: "Based on", comment: "Before the game(s) an app is made from" }),
  kinds: msg({ message: "Tags", comment: "Browse card detail: the app's tags, such as Recomp or its console, over its name" }),
  rating: msg({ message: "What players say", comment: "Browse card detail: Runs well, Has issues, and how many players said so" }),
  updated: msg({ message: "Last updated", comment: "Browse card detail: when the app's latest release came out" }),
  platforms: msg({ message: "Platforms", comment: "Browse card detail: icons for Windows, Linux, macOS, Android" }),
  ai: msg({ message: "AI label", comment: "Browse card detail: AI-assisted or Mostly AI" }),
  new: msg({ message: "New badge", comment: "Browse card detail: a badge on apps added to the catalog lately" }),
};

/**
 * The app for a sample card, where every option should be seen to do something: the first with the most of what they
 * turn on and off. (New isn't one: the card says it of any app.)
 */
export function sampleEntry(entries: readonly Entry[]): Entry | undefined {
  const has = (e: Entry) =>
    [
      e.libraryArt?.capsule,
      e.libraryArt?.header,
      e.games.length,
      e.tags.length,
      e.recommended + e.reportIssues + e.reportBroken,
      e.lastReleaseAt,
      e.supportedOS.length,
      e.aiLevel && e.aiLevel !== "none",
    ].filter(Boolean).length;
  return entries.reduce<Entry | undefined>((best, e) => (!best || has(e) > has(best) ? e : best), undefined);
}

/** The saved choices over the defaults; anything unknown (an older or newer launcher's) is left out. */
export function browseCardsOf(saved?: { look?: unknown; details?: unknown }): BrowseCards {
  const look = CARD_LOOKS.some(([l]) => l === saved?.look) ? (saved!.look as CardLook) : DEFAULT_BROWSE_CARDS.look;
  const details = Array.isArray(saved?.details) ? CARD_DETAILS.filter((d) => (saved.details as unknown[]).includes(d)) : DEFAULT_BROWSE_CARDS.details;
  return { look, details };
}

/** Only what differs from the defaults is saved. */
export function savedBrowseCards({ look, details }: BrowseCards): { look?: CardLook; details?: CardDetail[] } | undefined {
  const same = details.length === DEFAULT_BROWSE_CARDS.details.length && details.every((d) => DEFAULT_BROWSE_CARDS.details.includes(d));
  const changed = { ...(look !== DEFAULT_BROWSE_CARDS.look && { look }), ...(!same && { details: CARD_DETAILS.filter((d) => details.includes(d)) }) };
  return Object.keys(changed).length ? changed : undefined;
}
