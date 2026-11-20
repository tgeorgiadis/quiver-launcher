/**
 * Which download, when a release has several files for this computer: the store waits on its `choice`, and
 * remembers the pick for later releases itself (an install's `pick`), so this only asks.
 */
import { useEffect, useState } from "react";
import { FileArchive } from "lucide-react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { useTv, useInputHandler } from "../context";
import { LAYER } from "../input/router";
import { versionLabel } from "../../versions";
import { sizeLabel } from "../../downloads";

const size = (bytes?: number) => (bytes ? sizeLabel(bytes) : "");

export function AssetPicker() {
  const { bridge } = useTv();
  const choice = bridge.choice;
  const [index, setIndex] = useState(0);
  useEffect(() => setIndex(0), [choice]);

  const pick = (i: number) => choice?.resolve(choice.assets[i]);

  useInputHandler(
    {
      onAction(action) {
        if (!choice) return;
        if (action === "up") setIndex((i) => Math.max(0, i - 1));
        else if (action === "down") setIndex((i) => Math.min(choice.assets.length - 1, i + 1));
        else if (action === "accept") pick(index);
        else if (action === "back") choice.resolve(null);
      },
      hints: () => [
        { action: "accept", label: t({ message: "Download this", comment: "Controller hint: picks the focused file of a release to download" }) },
        { action: "back", label: t`Cancel` },
      ],
    },
    LAYER.dialog,
    Boolean(choice),
  );

  if (!choice) return null;
  const name = choice.entry.projectName;
  const version = versionLabel(choice.version);
  return (
    <div className="tv-dialog-backdrop" role="dialog" aria-modal="true" aria-label={t`Choose a download`}>
      <section className="tv-dialog">
        <div className="tv-dialog-body">
          <h2>
            <Trans>Which download?</Trans>
          </h2>
          <p className="tv-dialog-lead">
            {t`${name} ${version} has more than one file for your computer.`} <Trans>Quiver remembers the one you pick.</Trans>
          </p>
          <ul className="tv-asset-list">
            {choice.assets.map((a, i) => (
              <li key={a.id} className={i === index ? "focused" : ""} onMouseMove={() => setIndex(i)} onClick={() => pick(i)}>
                <FileArchive aria-hidden="true" />
                <span>{a.filename}</span>
                <small>{[a.architecture !== "unknown" && a.architecture, size(a.size)].filter(Boolean).join(" · ")}</small>
              </li>
            ))}
          </ul>
        </div>
      </section>
    </div>
  );
}
