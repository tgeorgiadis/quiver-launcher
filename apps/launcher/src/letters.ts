/** The letters a list by name or by game goes by, for jumping through it (letter-index.tsx). */
import type { Entry } from "@quiverlauncher/api";
import { sortTitle, type LibrarySort } from "./sort";

/**
 * The letter a title files under in a list by name, as sorting reads it ("The Legend of Zelda" under L, "Élan" under
 * E), and # for one starting with a digit or a sign.
 */
export function letterOf(title: string) {
  const first = [...sortTitle(title).normalize("NFD")][0] ?? "";
  return /\p{L}/u.test(first) ? first.toLocaleUpperCase() : "#";
}

/**
 * For Jump to, in a list by name or by game: each letter its apps file under, in the list's order, and where the first
 * of them is. None in another order, where letters don't follow on.
 */
export function jumpLetters(entries: readonly Entry[], sort: LibrarySort) {
  if (sort !== "name" && sort !== "game") return [];
  const at = new Map<string, number>();
  entries.forEach((e, i) => {
    const letter = letterOf(sort === "game" ? (e.games[0]?.title ?? e.projectName) : e.projectName);
    if (!at.has(letter)) at.set(letter, i);
  });
  return [...at].map(([letter, index]) => ({ letter, index }));
}
