/**
 * Whether TV mode shows: the player's setting, or by itself in Steam's Game Mode unless they turned it off
 * (tvModeOn). The top bar's TV button turns it on; TV mode's Settings, or its Exit, turn it off.
 */
import { tvModeOn, useLauncher } from "../store";

export function useTvMode() {
  const { settings, config, setSettings } = useLauncher();
  return {
    open: tvModeOn(settings, config),
    set: (on: boolean) => setSettings((s) => ({ ...s, tvMode: on ? "on" : "off" })),
  };
}
