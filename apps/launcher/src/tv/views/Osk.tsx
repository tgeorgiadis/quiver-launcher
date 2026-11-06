/**
 * The on-screen keyboard (spec §8.3): QWERTY, digits and - _ . : / @. A types the
 * focused key, X deletes, Y is a space, LB / RB move the caret, Menu is done, B
 * cancels. A physical keyboard types into it too. Used for search.
 */
import { useState } from "react";
import { useInputHandler } from "../context";
import type { InputAction } from "../input/actions";
import { LAYER } from "../input/router";
import { Glyph } from "./Glyph";

type Key = { label: string; type?: string; action?: "space" | "delete" | "done"; wide?: number };

const letters = (row: string): Key[] => [...row].map((c) => ({ label: c, type: c }));
export const LAYOUT: Key[][] = [
  letters("1234567890"),
  letters("qwertyuiop"),
  letters("asdfghjkl@"),
  letters("zxcvbnm-_."),
  [
    { label: ":", type: ":" },
    { label: "/", type: "/" },
    { label: "Space", action: "space", wide: 4 },
    { label: "⌫", action: "delete", wide: 2 },
    { label: "Done", action: "done", wide: 2 },
  ],
];

/** Where each key starts, in tenths of the row (wide keys take more). */
const starts = (row: Key[]) => row.reduce<number[]>((at, key, i) => [...at, i ? at[i - 1] + (row[i - 1].wide ?? 1) : 0], []);

/** The key reached from (row, col): Left/Right along the row (round), Up/Down to the key below the same spot. */
export function moveKey(rows: Key[][], at: { r: number; c: number }, action: InputAction): { r: number; c: number } {
  if (action === "left") return { r: at.r, c: (at.c - 1 + rows[at.r].length) % rows[at.r].length };
  if (action === "right") return { r: at.r, c: (at.c + 1) % rows[at.r].length };
  if (action !== "up" && action !== "down") return at;
  const r = Math.min(rows.length - 1, Math.max(0, at.r + (action === "down" ? 1 : -1)));
  if (r === at.r) return at;
  const x = starts(rows[at.r])[at.c];
  const target = starts(rows[r]);
  let c = 0;
  for (let i = 0; i < target.length; i++) if (target[i] <= x) c = i;
  return { r, c };
}

type Props = {
  title: string;
  value: string;
  /** Every change, for results that update as the player types. */
  onChange: (value: string) => void;
  onDone: (value: string) => void;
  onCancel: () => void;
};

export function Osk({ title, value: initial, onChange, onDone, onCancel }: Props) {
  const [value, setValue] = useState(initial);
  const [caret, setCaret] = useState(initial.length);
  const [at, setAt] = useState({ r: 1, c: 0 });

  const set = (next: string, nextCaret: number) => {
    setValue(next);
    setCaret(Math.max(0, Math.min(next.length, nextCaret)));
    onChange(next);
  };
  const type = (text: string) => set(value.slice(0, caret) + text + value.slice(caret), caret + text.length);
  const remove = () => caret > 0 && set(value.slice(0, caret - 1) + value.slice(caret), caret - 1);
  const press = (key: Key) => {
    if (key.type) type(key.type);
    else if (key.action === "space") type(" ");
    else if (key.action === "delete") remove();
    else if (key.action === "done") onDone(value);
  };

  useInputHandler(
    {
      onAction(action) {
        if (action === "up" || action === "down" || action === "left" || action === "right") setAt((p) => moveKey(LAYOUT, p, action));
        else if (action === "accept") press(LAYOUT[at.r][at.c]);
        else if (action === "secondary") remove();
        else if (action === "details") type(" ");
        else if (action === "tabPrev") setCaret((c) => Math.max(0, c - 1));
        else if (action === "tabNext") setCaret((c) => Math.min(value.length, c + 1));
        else if (action === "menu") onDone(value);
        else if (action === "back") onCancel();
      },
      onText(text) {
        if (text === "\b") remove();
        else if (text === "\n") onDone(value);
        else type(text);
      },
      hints: () => [
        { action: "accept", label: "Type" },
        { action: "secondary", label: "Delete" },
        { action: "details", label: "Space" },
        { action: "menu", label: "Done" },
        { action: "back", label: "Cancel" },
      ],
    },
    LAYER.keyboard,
  );

  return (
    <div className="tv-osk-backdrop" role="dialog" aria-modal="true" aria-label={title}>
      <section className="tv-osk">
        <header>
          <span className="tv-osk-title">{title}</span>
          <div className="tv-osk-field" aria-live="polite">
            <span>{value.slice(0, caret)}</span>
            <i className="tv-osk-caret" />
            <span>{value.slice(caret)}</span>
            {!value && <span className="tv-osk-placeholder">Type a game's name</span>}
          </div>
          <span className="tv-osk-caret-hint">
            <Glyph action="tabPrev" size={20} />
            <Glyph action="tabNext" size={20} /> caret
          </span>
        </header>
        <div className="tv-osk-keys">
          {LAYOUT.map((row, r) => (
            <div key={r} className="tv-osk-row">
              {row.map((key, c) => (
                <button
                  key={key.label}
                  type="button"
                  className={`tv-osk-key${at.r === r && at.c === c ? " focused" : ""}${key.action ? " action" : ""}`}
                  style={{ flexGrow: key.wide ?? 1 }}
                  onMouseMove={() => setAt({ r, c })}
                  onClick={() => press(key)}
                >
                  {key.label}
                </button>
              ))}
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}
