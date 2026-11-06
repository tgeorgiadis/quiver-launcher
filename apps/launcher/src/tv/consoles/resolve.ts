/**
 * Which console tab an app belongs to.
 * Pure; first match wins:
 * 1. The player's choice (Details → Console, or the consoles on the app's page): its first console. Choosing none
 *    files it under Apps & Tools when it's one, else Other.
 * 2. A tool or emulator → Apps & Tools: the catalog says it isn't a game, even
 *    when it names the consoles it's for (mGBA, Wheel Wizard).
 * 3. The console version it's based on (catalog `basedOn`).
 * 4. The first of its games' consoles (catalog `consoles`).
 * 5. An app/tool tag → Apps & Tools.
 * 6. Otherwise Other.
 * Apps the player added get their matched game's consoles from upstream, in `consoles`.
 */
import type { ProjectType } from "@quiverlauncher/api";
import { APPS, OTHER } from "./consoles";

const APP_TAGS = new Set(["app", "apps", "tool", "tools", "utility", "emulator"]);

/** "Game Boy", "game_boy" and "game-boy" are the same tag. */
export const normalizeTag = (tag: string) => tag.trim().toLowerCase().replace(/[\s_]+/g, "-");

export type Resolvable = { consoles: string[]; ownConsoles?: string[]; basedOn?: string; projectType: ProjectType; tags: string[] };

const isApp = (app: Resolvable) => app.projectType === "tool" || app.projectType === "emulator" || app.tags.some((t) => APP_TAGS.has(normalizeTag(t)));

export function resolveConsole(app: Resolvable): string {
  if (app.ownConsoles) return app.ownConsoles[0] ?? (isApp(app) ? APPS : OTHER);
  if (app.projectType === "tool" || app.projectType === "emulator") return APPS;
  if (app.basedOn) return app.basedOn;
  if (app.consoles.length) return app.consoles[0];
  if (app.tags.some((t) => APP_TAGS.has(normalizeTag(t)))) return APPS;
  return OTHER;
}
