/**
 * The button or key an action is bound to (Settings → Controls), in the style of whatever was pressed last: Xbox
 * (also for controllers that copy it), PlayStation, Nintendo, Steam's, or the keyboard. Art: Kenney's Input Prompts
 * (CC0), in ../glyphs, where there's a picture of that control; its name on a key cap otherwise, as the controller
 * calls it (spatial.ts's label).
 */
import { useSyncExternalStore, type CSSProperties } from "react";
import { label } from "../../spatial";
import type { PadLabels } from "../../native";
import { useTv, type InputFamily } from "../context";
import { controlFor, type InputAction } from "../input/actions";

const urls = import.meta.glob<string>("../glyphs/*/*.svg", { eager: true, query: "?url", import: "default" });

/** The controls there are pictures of, by the picture's name. */
const PAD_PICTURES: Record<string, string> = {
  South: "accept", East: "back", West: "secondary", North: "details",
  LeftTrigger: "tabPrev", RightTrigger: "tabNext", LeftTrigger2: "pagePrev", RightTrigger2: "pageNext",
  Select: "search", Start: "menu",
  DPadUp: "up", DPadDown: "down", DPadLeft: "left", DPadRight: "right",
  LeftStickUp: "up", LeftStickDown: "down", LeftStickLeft: "left", LeftStickRight: "right",
};
const KEY_PICTURES: Record<string, string> = {
  Enter: "accept", Escape: "back", X: "secondary", Y: "details", Q: "tabPrev", E: "tabNext",
  PageUp: "pagePrev", PageDown: "pageNext", "/": "search", F10: "menu",
  ArrowUp: "up", ArrowDown: "down", ArrowLeft: "left", ArrowRight: "right",
};
/**
 * Which pictures fit each controller. Steam's face buttons and D-pad are an Xbox pad's, its shoulders L1 and R1; a
 * Nintendo pad's face buttons are lettered the other way round, so only its D-pad matches.
 */
const FITS: Record<InputFamily, (control: string) => boolean> = {
  xbox: () => true,
  playstation: () => true,
  keyboard: () => true,
  steam: (c) => !/Trigger/.test(c),
  nintendo: (c) => /^(DPad|LeftStick)/.test(c),
};
/** A controller Quiver can't name yet is called as an Xbox pad's buttons are. */
const XBOX: PadLabels = { kind: "xbox", south: "A", east: "B", west: "X", north: "Y" };
const folder = (family: InputFamily) => (family === "playstation" ? "playstation" : family === "keyboard" ? "keyboard" : "xbox");

export function Glyph({ action, size }: { action: InputAction; size?: number }) {
  const { family, router, bindings, labels } = useTv();
  useSyncExternalStore(router.subscribe, router.snapshot);
  const hold = router.holding;
  const holding = hold?.action === action;
  const kind = family === "keyboard" ? "keys" : "pad";
  const control = controlFor(action, bindings[kind], kind);
  const picture = control && FITS[family](control) ? (kind === "keys" ? KEY_PICTURES : PAD_PICTURES)[control] : undefined;
  const url = picture && urls[`../glyphs/${folder(family)}/${picture}.svg`];
  const name = control ? label(kind, control, kind === "pad" ? (labels ?? XBOX) : null) : "";
  return (
    <span
      className={`tv-glyph${holding ? " holding" : ""}${url ? "" : " tv-glyph-text"}`}
      style={{ "--hold": holding ? `${hold.duration}ms` : undefined, "--size": size } as CSSProperties}
    >
      {url ? <img src={url} alt={name} draggable={false} /> : <span role="img" aria-label={name}>{name || "?"}</span>}
    </span>
  );
}
