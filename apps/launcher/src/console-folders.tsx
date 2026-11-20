/**
 * Settings › Apps folder, with new apps grouped by system: whether system folders go by abbreviation or in full, and
 * where each is. Each can have another name in the apps folder or be a folder anywhere else ("D:\Games\N64"), typed
 * or browsed for; empty, its usual place. Listed: the system folders the player's apps go in, and any they set; any
 * other system can be added. When a system folder changes with apps in it, they can follow.
 */
import { useState } from "react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { Button } from "@quiverlauncher/ui/controls";
import { appsRightIn, consoleFolderChoice, consoleFolderChoices, consoleHome, consoleHomePath, samePath, systemFolder, useLauncher } from "./store";
import { native } from "./native";

/** Apps still in a console folder's old place, which can follow it to the new one. */
type Left = { from: string; apps: { id: string; folder: string }[] };

export function ConsoleFolders() {
  const { config, settings, setSettings, installs, consoleNames, consoleFolderOf, moveApp } = useLauncher();
  const folders = settings.consoleFolders ?? {};
  const appsDir = config.appsDir;
  // As typed, until it's left or Enter is pressed; what's wrong with it, until it's changed again.
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [problems, setProblems] = useState<Record<string, string>>({});
  // Consoles the player chose to set a folder for, though none of their apps is for it yet.
  const [added, setAdded] = useState<string[]>([]);
  const [left, setLeft] = useState<Record<string, Left>>({});
  const [moving, setMoving] = useState<{ group: string; done: number; total: number } | null>(null);
  const [done, setDone] = useState<Record<string, { text: string; error?: boolean }>>({});
  const choices = consoleFolderChoices(consoleNames);
  const used = new Set(Object.keys(installs).flatMap((id) => consoleFolderOf(id) ?? []));
  const rows = choices.filter((c) => used.has(c.group) || folders[c.group] || added.includes(c.group));
  const more = choices.filter((c) => !rows.includes(c));
  const pathOf = (group: string) => consoleHomePath(consoleHome(group, settings, consoleNames), appsDir);
  const usual = (group: string) => systemFolder(group, settings.systemFolderNames, consoleNames);
  const count = (group: string) => Object.keys(installs).filter((id) => consoleFolderOf(id) === group).length;
  const forget = <T,>(all: Record<string, T>, group: string) => {
    const { [group]: _, ...rest } = all;
    return rest;
  };

  /** A console folder's new place, typed or picked; empty, where grouping puts it. Its apps can follow. */
  const place = (group: string, typed: string) => {
    setProblems((p) => forget(p, group));
    setDone((d) => forget(d, group));
    const chosen = consoleFolderChoice(usual(group), typed, appsDir, installs);
    // What's wrong is said under it, what was typed kept in the box to fix.
    if (chosen.problem !== undefined) return setProblems((p) => ({ ...p, [group]: chosen.problem }));
    setDrafts((d) => forget(d, group));
    const before = pathOf(group);
    const next = chosen.save ? { ...folders, [group]: chosen.save } : forget(folders, group);
    const after = consoleHomePath(consoleHome(group, { ...settings, consoleFolders: next }, consoleNames), appsDir);
    if (samePath(before, after)) return;
    setSettings({ ...settings, consoleFolders: Object.keys(next).length ? next : undefined });
    // Only this console's apps: another's may share the old folder, and stays.
    const apps = appsRightIn(installs, before).filter((a) => consoleFolderOf(a.id) === group);
    setLeft((l) => (apps.length ? { ...l, [group]: { from: before, apps } } : forget(l, group)));
  };
  const browse = async (group: string, name: string) => {
    const to = await native.pickFolder(t`Choose a folder for ${name}`, pathOf(group));
    if (to) place(group, to);
  };
  /** Moves the apps left in a console folder's old place to its new one, one at a time, then the old one if it's empty. */
  const follow = async (group: string) => {
    const { from, apps } = left[group];
    setLeft((l) => forget(l, group));
    const failed: string[] = [];
    setMoving({ group, done: 0, total: apps.length });
    for (const [n, app] of apps.entries()) {
      try {
        await moveApp(app.id, appsDir);
      } catch (e) {
        failed.push(`${app.folder}: ${e instanceof Error ? e.message : String(e)}`);
      }
      setMoving({ group, done: n + 1, total: apps.length });
    }
    setMoving(null);
    await native.removeEmptyConsoleFolder(from).catch(() => {});
    const n = apps.length;
    setDone((d) => ({
      ...d,
      [group]: failed.length
        ? { text: `${t`Moved ${n - failed.length} of ${plural(n, { one: "# app", other: "# apps" })}.`} ${failed.join(" ")}`, error: true }
        : { text: t`Moved ${plural(n, { one: "# app", other: "# apps" })}.` },
    }));
  };

  return (
    <details className="console-folders">
      <summary>
        <Trans>System folder names and places</Trans>
        {Object.keys(folders).length > 0 && <span className="muted"> · {plural(Object.keys(folders).length, { one: "# changed", other: "# changed" })}</span>}
      </summary>
      <p className="muted">
        <Trans>Rename a system's folder, or put it anywhere, such as on another drive. Leave it empty for the usual place.</Trans>
      </p>
      <ul className="console-folder-list">
        {rows.map(({ group, name }) => {
          const id = `system-folder-${group.replace(/[^A-Za-z0-9]+/g, "-")}`;
          const n = count(group);
          const still = left[group];
          const busy = Boolean(moving);
          return (
            <li key={group} className="console-folder">
              <div className="console-folder-name">
                <label htmlFor={id}>{name}</label>
                {n > 0 && <span className="muted">{plural(n, { one: "# app", other: "# apps" })}</span>}
              </div>
              <div className="console-folder-edit">
                <input
                  id={id}
                  className="console-folder-path"
                  value={drafts[group] ?? (folders[group] ? pathOf(group) : "")}
                  placeholder={pathOf(group)}
                  spellCheck={false}
                  autoComplete="off"
                  disabled={busy}
                  aria-invalid={Boolean(problems[group])}
                  aria-describedby={problems[group] ? `${id}-problem` : undefined}
                  onChange={(e) => setDrafts((d) => ({ ...d, [group]: e.target.value }))}
                  onBlur={(e) => drafts[group] !== undefined && place(group, e.currentTarget.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") e.currentTarget.blur();
                    if (e.key === "Escape") {
                      setDrafts((d) => forget(d, group));
                      setProblems((p) => forget(p, group));
                    }
                  }}
                />
                <Button disabled={busy} aria-label={t`Browse for a folder for ${name}`} onClick={() => void browse(group, name)}>
                  <Trans comment="Opens a folder picker for a system's apps">Browse…</Trans>
                </Button>
                {folders[group] && (
                  <Button disabled={busy} aria-label={t`Put ${name}'s folder back in the usual place`} onClick={() => place(group, "")}>
                    <Trans comment="Puts a system's folder back where grouping by system puts it">Reset</Trans>
                  </Button>
                )}
              </div>
              {problems[group] && (
                <p id={`${id}-problem`} className="job-error console-folder-note" role="status">
                  {problems[group]}
                </p>
              )}
              {still && (
                <div className="console-folder-left" role="group" aria-label={t`Apps in the old folder`}>
                  <p className="muted">
                    {plural(still.apps.length, {
                      one: `# app is still in ${still.from}. New ones go in the new folder.`,
                      other: `# apps are still in ${still.from}. New ones go in the new folder.`,
                    })}
                  </p>
                  <div className="row">
                    <Button variant="main" disabled={busy} onClick={() => void follow(group)}>
                      {plural(still.apps.length, { one: "Move it there", other: "Move them there" })}
                    </Button>
                    <Button onClick={() => setLeft((l) => forget(l, group))}>
                      <Trans>Leave them</Trans>
                    </Button>
                  </div>
                </div>
              )}
              {moving?.group === group && (
                <p className="muted console-folder-note" role="status">
                  <Trans>
                    Moving apps… {moving.done} of {moving.total}
                  </Trans>
                </p>
              )}
              {done[group] && (
                <p className={`${done[group].error ? "job-error" : "muted"} console-folder-note`} role="status">
                  {done[group].text}
                </p>
              )}
            </li>
          );
        })}
      </ul>
      {more.length > 0 && (
        <select
          className="console-folder-add"
          aria-label={t`Set a folder for another system`}
          value=""
          onChange={(e) => {
            const group = e.target.value;
            if (!group) return;
            setAdded((a) => [...a, group]);
            // Its box, to type in straight away.
            setTimeout(() => document.getElementById(`system-folder-${group.replace(/[^A-Za-z0-9]+/g, "-")}`)?.focus());
          }}
        >
          <option value="">{t`Set a folder for another system…`}</option>
          {more.map((c) => (
            <option key={c.group} value={c.group}>
              {c.name}
            </option>
          ))}
        </select>
      )}
    </details>
  );
}
