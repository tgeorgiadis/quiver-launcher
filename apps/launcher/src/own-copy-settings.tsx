/**
 * Settings → Game copies: the folders the player keeps their ROMs and disc images in, for each console or for all of
 * them. When a game needs the player's own copy (own-copy-setup.tsx), its setup looks in its consoles' folders first,
 * deeper than in the usual places, and lists what it finds there first.
 */
import { useState } from "react";
import { FolderPlus, X } from "lucide-react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { useLauncher } from "./store";
import { native } from "./native";
import { EVERY_CONSOLE, withCopyFolder } from "./own-copy";

export function GameCopySettings() {
  const { settings, setSettings, consoles } = useLauncher();
  const folders = settings.copyFolders ?? {};
  const [adding, setAdding] = useState("");
  const nameOf = (id: string) => (id === EVERY_CONSOLE ? t`Every console` : (consoles.find((c) => c.id === id)?.name ?? id));
  // Every console's first, then the consoles with folders, by name.
  const set = [EVERY_CONSOLE, ...Object.keys(folders).filter((id) => id !== EVERY_CONSOLE).sort((a, b) => nameOf(a).localeCompare(nameOf(b)))];
  const unset = consoles.filter((c) => !folders[c.id]).sort((a, b) => a.name.localeCompare(b.name));

  const add = async (id: string) => {
    const console = nameOf(id);
    const folder = await native.pickFolder(id === EVERY_CONSOLE ? t`Choose a folder you keep game copies in` : t`Choose where you keep your ${console} games`, folders[id]?.[0]);
    if (folder) setSettings((s) => ({ ...s, copyFolders: withCopyFolder(s.copyFolders, id, folder, true) }));
  };
  const remove = (id: string, folder: string) => setSettings((s) => ({ ...s, copyFolders: withCopyFolder(s.copyFolders, id, folder, false) }));

  return (
    <section className="settings-section game-copies" aria-label={t`Game copies`}>
      <p className="muted">
        <Trans>When an app needs your own copy of a game, like a ROM or a disc image, Quiver looks in these folders first, then in Downloads and your drives.</Trans>
      </p>
      <ul className="game-copy-consoles">
        {set.map((id) => {
          const console = nameOf(id);
          return (
            <li key={id}>
              <div className="game-copy-console">
                <strong>{console}</strong>
                <button onClick={() => void add(id)} aria-label={t`Add a folder for ${console}`}>
                  <FolderPlus size={15} aria-hidden="true" /> <Trans>Add folder…</Trans>
                </button>
              </div>
              {(folders[id] ?? []).map((folder) => (
                <div key={folder} className="game-copy-folder">
                  <span title={folder}>{folder}</span>
                  <button className="icon" onClick={() => remove(id, folder)} aria-label={t`Stop looking in ${folder} for ${console}`} title={t`Remove`}>
                    <X size={15} aria-hidden="true" />
                  </button>
                </div>
              ))}
              {id === EVERY_CONSOLE && !folders[id]?.length && (
                <p className="muted small">
                  <Trans>For a folder with games for many consoles in it.</Trans>
                </p>
              )}
            </li>
          );
        })}
      </ul>
      {unset.length > 0 && (
        <label className="game-copy-add">
          <span>
            <Trans>Add folders for a console</Trans>
          </span>
          <select
            value={adding}
            onChange={(e) => {
              const id = e.target.value;
              setAdding("");
              if (id) void add(id);
            }}
          >
            <option value="">{t`Choose a console…`}</option>
            {unset.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </label>
      )}
    </section>
  );
}
