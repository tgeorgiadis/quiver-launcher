/**
 * X in the library's tabs: the order of their games (the launcher's sorts, sort.ts), as a list dropped over the top of
 * the page. A picks one and closes it; B closes it.
 */
import { useEffect } from "react";
import { t } from "@lingui/core/macro";
import { i18n } from "@quiverlauncher/i18n";
import { LIBRARY_SORTS } from "../../sort";
import { LAYER } from "../input/router";
import { TV_SORTS, type TvSort } from "../settings";
import { RowList, useRows, type Row } from "./Settings";

/** A sort's name, in the language shown. */
export const sortName = (sort: TvSort) => i18n._(LIBRARY_SORTS.find(([s]) => s === sort)![1]);

/** X's hint in the library's tabs. */
export const sortHint = () => t({ message: "Sort", comment: "Label of the library's sort order" });

export function SortList({ value, onPick, onClose }: { value: TvSort; onPick: (sort: TvSort) => void; onClose: () => void }) {
  const rows: Row[] = TV_SORTS.map((sort) => ({ kind: "action", label: sortName(sort), value: sort === value ? "✓" : "", run: () => onPick(sort) }));
  const list = useRows(rows, onClose, LAYER.overlay, true, t({ message: "Choose", comment: "Controller hint in TV mode's Sort list: sorts the games this way" }));
  // From the order there is now.
  useEffect(() => list.setIndex(Math.max(0, TV_SORTS.indexOf(value))), []); // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <div className="tv-dropdown tv-sort-list" role="dialog" aria-label={sortHint()}>
      <RowList rows={rows} {...list} />
    </div>
  );
}
