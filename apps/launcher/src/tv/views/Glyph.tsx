/**
 * A button in the style of whatever was pressed last: Xbox (also for other
 * pads), PlayStation, or the keyboard (spec §5.3). Art: Kenney's Input Prompts
 * (CC0), in ../glyphs.
 */
import { useSyncExternalStore, type CSSProperties } from "react";
import { useTv, type InputFamily } from "../context";
import type { InputAction } from "../input/actions";

const urls = import.meta.glob<string>("../glyphs/*/*.svg", { eager: true, query: "?url", import: "default" });

const set = (family: InputFamily) => (family === "playstation" ? "playstation" : family === "keyboard" ? "keyboard" : "xbox");
export const glyphUrl = (family: InputFamily, action: InputAction) => urls[`../glyphs/${set(family)}/${action}.svg`];

const NAMES: Record<string, Partial<Record<InputAction, string>>> = {
  xbox: { accept: "A", back: "B", secondary: "X", details: "Y", tabPrev: "LB", tabNext: "RB", pagePrev: "LT", pageNext: "RT", search: "View", menu: "Menu" },
  playstation: { accept: "Cross", back: "Circle", secondary: "Square", details: "Triangle", tabPrev: "L1", tabNext: "R1", pagePrev: "L2", pageNext: "R2", search: "Create", menu: "Options" },
  keyboard: { accept: "Enter", back: "Esc", secondary: "X", details: "Y", tabPrev: "Q", tabNext: "E", pagePrev: "Page Up", pageNext: "Page Down", search: "/", menu: "F10" },
};

export function Glyph({ action, size }: { action: InputAction; size?: number }) {
  const { family, router } = useTv();
  useSyncExternalStore(router.subscribe, router.snapshot);
  const hold = router.holding;
  const holding = hold?.action === action;
  return (
    <span
      className={`tv-glyph${holding ? " holding" : ""}`}
      style={{ "--hold": holding ? `${hold.duration}ms` : undefined, "--size": size } as CSSProperties}
    >
      <img src={glyphUrl(family, action)} alt={NAMES[set(family)][action] ?? action} draggable={false} />
    </span>
  );
}
