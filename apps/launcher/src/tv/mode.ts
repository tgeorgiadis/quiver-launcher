/**
 * Whether TV mode shows: the player's setting, or by itself in Steam's Game Mode unless they turned it off
 * (tvModeOn). The top bar's TV button turns it on; TV mode's Settings, or its Exit, turn it off.
 */
import { useMemo } from "react";
import { tvModeOn, useLauncher } from "../store";
import { savedTvSettings, tvSettings, type TvSettings } from "./settings";

export function useTvMode() {
  const { settings, config, setSettings } = useLauncher();
  return {
    open: tvModeOn(settings, config),
    set: (on: boolean) => setSettings((s) => ({ ...s, tvMode: on ? "on" : "off" })),
  };
}

/** TV mode's settings, in the launcher's (`tv`). */
export function useTvSettings() {
  const { settings, setSettings } = useLauncher();
  const saved = settings.tv;
  return {
    settings: useMemo(() => tvSettings(saved), [saved]),
    set: (patch: Partial<TvSettings>) => setSettings((s) => ({ ...s, tv: savedTvSettings(s.tv, patch) })),
  };
}
