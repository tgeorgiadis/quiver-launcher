import { useEffect, useRef } from "react";
import { useLauncher } from "./store";
import { withOverrides } from "./library";
import { shortcutRequest, shortcutsOn } from "./detail";
import { native, type ShortcutRequest } from "./native";

/**
 * Once a start, the desktop and Steam shortcuts Quiver made before its shortcuts started games through it are changed
 * to, so their saves sync (shortcut_upgrade.rs). Each installed app is described as for a new shortcut. Not in a
 * Quiver a shortcut started, which has a game to see to and quits after.
 */
export function useShortcutUpgrade() {
  const { config, installs, catalog, library, settings } = useLauncher();
  const done = useRef(false);
  // The apps' names, which their shortcuts are named for, come with the catalog.
  const ready = Object.keys(catalog).length > 0;
  useEffect(() => {
    if (done.current || !ready || !shortcutsOn(config.os) || config.shortcut?.host) return;
    done.current = true;
    const requests: ShortcutRequest[] = Object.entries(installs).flatMap(([id, install]) => {
      const entry = catalog[id] && withOverrides(catalog[id], library.find((i) => i.id === id)?.overrides);
      return entry ? [shortcutRequest(entry, install, settings)] : [];
    });
    if (requests.length) void native.upgradeShortcuts(requests).catch(() => {});
  }, [ready]); // eslint-disable-line react-hooks/exhaustive-deps
}
