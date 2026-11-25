/**
 * The setup file a port's repository keeps for other launchers, as the catalog serves it: passed at Play so the
 * launcher can start the port's program with what it says (port_setup.rs), when the app's folder holds none of its
 * own. Asked once per app a session, and kept for when the site can't be reached.
 */
import type { Client } from "@quiverlauncher/api";

const asked = new Map<string, string | null>();
const key = (slug: string) => `quiver.setupFile.${slug}`;
function kept(slug: string): string | undefined {
  try {
    return localStorage.getItem(key(slug)) ?? undefined;
  } catch {
    return undefined;
  }
}
function keep(slug: string, text: string | null) {
  try {
    if (text) localStorage.setItem(key(slug), text);
    else localStorage.removeItem(key(slug));
  } catch {
    // Storage full or off: asked again next session.
  }
}

/** The app's setup file text, or undefined when it has none. Never holds Play up for long: 3 seconds at most. */
export async function setupFileText(client: Pick<Client, "setupFile">, slug: string | undefined, waitMs = 3_000): Promise<string | undefined> {
  if (!slug) return undefined;
  if (asked.has(slug)) return asked.get(slug) ?? undefined;
  let timer: ReturnType<typeof setTimeout> | undefined;
  const late = new Promise<"late">((resolve) => (timer = setTimeout(() => resolve("late"), waitMs)));
  try {
    const found = await Promise.race([client.setupFile(slug), late]);
    if (found === "late") return kept(slug);
    const text = found?.text ?? null;
    asked.set(slug, text);
    keep(slug, text);
    return text ?? undefined;
  } catch {
    return kept(slug);
  } finally {
    clearTimeout(timer);
  }
}

/** For tests: forget what was asked this session. */
export const forgetSetupFiles = () => asked.clear();
