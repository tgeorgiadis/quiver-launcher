/**
 * Mods for an installed app, from the Thunderstore communities and GameBanana
 * games its catalog entry names: browse and search them, install one (and
 * the mods it needs), update one or all of them, and uninstall. What each mod
 * put in the mods folder is recorded beside the app, so nothing else there
 * is touched.
 */
import { useEffect, useMemo, useState } from "react";
import { Puzzle } from "lucide-react";
import { msg, plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { Entry, ModSettings } from "@quiverlauncher/api";
import { native, type InstalledMod, type ModFile, type ModPackage } from "./native";
import { useLauncher } from "./store";
import { closesOutside } from "./overlay";
import { i18n } from "@quiverlauncher/i18n";

const PAGE = 30;
const SORTS = {
  installed: msg({ message: "Installed first", comment: "Sort order for mods" }),
  updates: msg({ message: "Updates first", comment: "Sort order for mods" }),
  name: msg({ message: "Name", comment: "Sort order for mods" }),
  rating: msg({ message: "Top rated", comment: "Sort order for mods" }),
  updated: msg({ message: "Last updated", comment: "Sort order for mods" }),
  downloads: msg({ message: "Most downloaded", comment: "Sort order for mods" }),
} as const;
type Sort = keyof typeof SORTS;
const PROVIDERS: Record<string, string> = { thunderstore: "Thunderstore", gamebanana: "GameBanana" };

/** An app's mod settings, when it has a mods folder and somewhere to get mods. */
export function modSettings(entry: Entry): ModSettings | undefined {
  const mods = entry.launcher?.mods;
  return mods?.path && mods.sources?.length ? mods : undefined;
}

const message = (e: unknown) => (e instanceof Error ? e.message : String(e));
const count = (n: number) => new Intl.NumberFormat(i18n.locale, { notation: "compact" }).format(n);
const day = (at: number) => new Date(at).toLocaleDateString(i18n.locale, { dateStyle: "medium" });
const size = (bytes: number) => {
  if (bytes < 1_048_576) {
    const kb = Math.max(1, Math.round(bytes / 1024)).toLocaleString(i18n.locale);
    return t`${kb} KB`;
  }
  const mb = (bytes / 1_048_576).toLocaleString(i18n.locale, { minimumFractionDigits: 1, maximumFractionDigits: 1 });
  return t`${mb} MB`;
};
/** A GameBanana mod without a version number goes by when it last changed. */
const versionText = (p: { provider?: string; version: string; updated?: number }) => {
  if (!/^\d{10,}$/.test(p.version)) return `v${p.version.replace(/^v/i, "")}`;
  const date = day(Number(p.version));
  return t({ message: `the ${date} version`, comment: "A mod's version, by its date, inside a sentence: the 3 Jan 2025 version installed" });
};

/** On the app page, for an installed app whose catalog entry has mods. */
export function ModsButton({ entry }: { entry: Entry }) {
  const { installs, config } = useLauncher();
  const install = installs[entry.id];
  const [open, setOpen] = useState(false);
  const mods = modSettings(entry);
  // A Windows installer's files aren't in the app's folder, where mods go.
  if (!install || install.local || install.installer || !mods || config.os === "android") return null;
  return (
    <>
      <button className="panel-action opens" onClick={() => setOpen(true)}>
        <Puzzle size={16} aria-hidden="true" /> <Trans>Mods</Trans>
      </button>
      {open && <ModsDialog name={entry.projectName} folder={install.folder} dir={install.dir} mods={mods} onClose={() => setOpen(false)} />}
    </>
  );
}

function ModsDialog({ name, folder, dir, mods, onClose }: { name: string; folder: string; dir?: string; mods: ModSettings; onClose: () => void }) {
  const [packages, setPackages] = useState<ModPackage[] | null>(null);
  const [failed, setFailed] = useState<string[]>([]);
  const [installed, setInstalled] = useState<InstalledMod[]>([]);
  const [tab, setTab] = useState<"browse" | "installed">("browse");
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<Sort>("installed");
  const [provider, setProvider] = useState<string | null>(null);
  const [nsfw, setNsfw] = useState(false);
  const [shown, setShown] = useState(PAGE);
  const [busy, setBusy] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [asking, setAsking] = useState<{ pkg: ModPackage; missing: ModPackage[] } | null>(null);
  const [picking, setPicking] = useState<{ pkg: ModPackage; files: ModFile[] } | null>(null);
  const [loads, setLoads] = useState(0);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      if (asking || picking) {
        setAsking(null);
        setPicking(null);
      } else onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose, asking, picking]);

  useEffect(() => {
    setPackages(null);
    void Promise.allSettled(mods.sources.map((s) => native.modPackages(s))).then((results) => {
      setPackages(results.flatMap((r) => (r.status === "fulfilled" ? r.value : [])));
      setFailed(
        results.flatMap((r, i) => (r.status === "rejected" ? [`${PROVIDERS[mods.sources[i].provider] ?? mods.sources[i].provider}: ${message(r.reason)}`] : [])),
      );
    });
    native.installedMods(folder, dir).then(setInstalled, () => setInstalled([]));
  }, [loads]); // eslint-disable-line react-hooks/exhaustive-deps

  const byKey = useMemo(() => new Map((packages ?? []).map((p) => [p.key, p])), [packages]);
  const mine = useMemo(() => new Map(installed.map((m) => [m.key, m])), [installed]);
  const outdated = (p: ModPackage) => {
    const m = mine.get(p.key);
    return Boolean(m && m.version !== p.version);
  };
  const updates = (packages ?? []).filter(outdated);

  /** The mods it needs that aren't installed, the ones they need first; a loop is followed once. */
  const missingFor = (pkg: ModPackage) => {
    const out: ModPackage[] = [];
    const seen = new Set([pkg.key]);
    const visit = (p: ModPackage) => {
      for (const key of p.dependencies) {
        if (seen.has(key)) continue;
        seen.add(key);
        const needed = byKey.get(key);
        if (!needed) continue;
        visit(needed);
        if (!mine.has(key)) out.push(needed);
      }
    };
    visit(pkg);
    return out;
  };

  const act = async (key: string, doing: string, work: () => Promise<unknown>) => {
    setBusy((b) => ({ ...b, [key]: doing }));
    setErrors(({ [key]: _, ...rest }) => rest);
    try {
      await work();
    } catch (e) {
      setErrors((es) => ({ ...es, [key]: message(e) }));
    } finally {
      setBusy(({ [key]: _, ...rest }) => rest);
    }
  };

  /** Installs or updates one mod; a GameBanana mod with several files asks which. */
  const put = async (pkg: ModPackage, file?: ModFile) => {
    const files = file ? [file] : await native.modFiles(pkg);
    const before = mine.get(pkg.key);
    const chosen = file ?? (files.length === 1 ? files[0] : files.find((f) => f.name === before?.file));
    if (!chosen) {
      if (!files.length) throw new Error(t`This mod has no files to download.`);
      setPicking({ pkg, files });
      return;
    }
    const done = await native.installMod(folder, dir, mods, pkg, chosen);
    setInstalled((list) => [...list.filter((m) => m.key !== done.key), done]);
  };

  const install = (pkg: ModPackage, needed: ModPackage[] = []) =>
    act(pkg.key, t`Installing…`, async () => {
      for (const d of needed) await put(d);
      await put(pkg);
    });
  const askOrInstall = (pkg: ModPackage) => {
    const missing = missingFor(pkg);
    if (missing.length) setAsking({ pkg, missing });
    else void install(pkg);
  };
  const update = (pkg: ModPackage) => act(pkg.key, t`Updating…`, () => put(pkg));
  const updateAll = async () => {
    for (const pkg of updates) await update(pkg);
  };
  const uninstall = (key: string) =>
    act(key, t`Removing…`, async () => {
      await native.uninstallMod(folder, dir, mods, key);
      setInstalled((list) => list.filter((m) => m.key !== key));
    });

  const providers = [...new Set(mods.sources.map((s) => s.provider))];
  const query = search.trim().toLowerCase();
  const listed = (packages ?? [])
    .filter((p) => (nsfw || !p.nsfw) && (!provider || p.provider === provider))
    .filter((p) => !query || [p.name, p.author, p.description].some((text) => text.toLowerCase().includes(query)));
  const rank = (p: ModPackage) => (sort === "updates" ? (outdated(p) ? 2 : mine.has(p.key) ? 1 : 0) : sort === "installed" ? (mine.has(p.key) ? 1 : 0) : 0);
  const sorted = [...listed].sort(
    (a, b) =>
      rank(b) - rank(a) ||
      (sort === "name"
        ? a.name.localeCompare(b.name)
        : sort === "rating"
          ? b.rating - a.rating
          : sort === "updated"
            ? b.updated - a.updated
            : b.downloads - a.downloads),
  );

  const row = (key: string, pkg: ModPackage | undefined, record: InstalledMod | undefined) => {
    const doing = busy[key];
    const title = pkg?.name ?? record?.name ?? key;
    // What it says under the name, each a part of its own.
    const author = pkg?.author;
    const current = pkg && versionText(pkg);
    const have = record && versionText(record);
    const downloadCount = pkg?.downloads ?? 0;
    const downloads = count(downloadCount);
    const rating = pkg?.rating ?? 0;
    const likes = count(rating);
    const updatedOn = pkg?.updated ? day(pkg.updated) : undefined;
    const site = pkg && PROVIDERS[pkg.provider];
    return (
      <li key={key} className="mod" aria-label={title}>
        {pkg?.icon ? <img className="mod-icon" src={pkg.icon} alt="" loading="lazy" /> : <span className="mod-icon" aria-hidden="true" />}
        <div className="mod-text">
          <p className="mod-title">
            <strong>{title}</strong>
            {record &&
              (pkg && outdated(pkg) ? (
                <span className="mod-badge update">
                  <Trans>Update available</Trans>
                </span>
              ) : (
                <span className="mod-badge">
                  <Trans comment="A mod that's installed">Installed</Trans>
                </span>
              ))}
          </p>
          <p className="muted small">
            {[
              author && t({ message: `by ${author}`, comment: "Who made a mod" }),
              have ? t({ message: `${have} installed`, comment: "A mod's version on this computer: v1.2 installed" }) : current,
              pkg && outdated(pkg) && t({ message: `${current} is out`, comment: "A mod's newer version: v1.3 is out" }),
              pkg &&
                (pkg.provider === "gamebanana"
                  ? plural(downloadCount, { one: `${downloads} view`, other: `${downloads} views` })
                  : plural(downloadCount, { one: `${downloads} download`, other: `${downloads} downloads` })),
              pkg && plural(rating, { one: `${likes} like`, other: `${likes} likes` }),
              updatedOn && t({ message: `updated ${updatedOn}`, comment: "When a mod last changed" }),
            ]
              .filter(Boolean)
              .join(" · ")}
          </p>
          {pkg?.description && <p className="mod-description">{pkg.description}</p>}
          {pkg?.url && (
            <button type="button" className="link-button" onClick={() => void native.openUrl(pkg.url)}>
              {site ? <Trans>Open on {site}</Trans> : <Trans>Open on its site</Trans>}
            </button>
          )}
          {errors[key] && (
            <p className="job-error" role="status">
              {errors[key]}
            </p>
          )}
        </div>
        <div className="mod-actions">
          {doing ? (
            <span className="muted" role="status">
              {doing}
            </span>
          ) : (
            <>
              {pkg && !record && (
                <button className="primary" onClick={() => askOrInstall(pkg)}>
                  <Trans comment="Button that installs a mod">Install</Trans>
                </button>
              )}
              {pkg && record && outdated(pkg) && (
                <button className="primary" onClick={() => void update(pkg)}>
                  <Trans comment="Button that updates a mod">Update</Trans>
                </button>
              )}
              {record && (
                <button onClick={() => void uninstall(key)}>
                  <Trans>Uninstall</Trans>
                </button>
              )}
            </>
          )}
        </div>
      </li>
    );
  };

  // The mod asking for others, and the ones it needs; the mod with files to choose from.
  const mod = asking?.pkg.name;
  const needed = asking?.missing.map((m) => m.name).join(", ");
  const missing = asking?.missing.length ?? 0;
  const modWithFiles = picking?.pkg.name;
  const updateCount = updates.length;
  const modsPath = mods.path;
  return (
    <div className="overlay" {...closesOutside(onClose)}>
      <section className="detail choose mods" role="dialog" aria-label={t`Mods for ${name}`} onClick={(e) => e.stopPropagation()}>
        <div className="detail-body">
          <div className="mods-head">
            <h2>
              <Trans>Mods for {name}</Trans>
            </h2>
            <div className="row">
              <button onClick={() => setLoads((n) => n + 1)} disabled={!packages}>
                <Trans>Refresh</Trans>
              </button>
              <button onClick={onClose}>
                <Trans>Close</Trans>
              </button>
            </div>
          </div>
          <div className="detail-tabs" role="tablist" aria-label={t`Mods`}>
            <button type="button" role="tab" aria-selected={tab === "browse"} onClick={() => setTab("browse")}>
              <Trans comment="Tab listing mods to install">Browse</Trans>
            </button>
            <button type="button" role="tab" aria-selected={tab === "installed"} onClick={() => setTab("installed")}>
              <Trans context="tab" comment="Tab listing the mods installed">Installed</Trans>
              <span className="tab-count">{installed.length}</span>
            </button>
          </div>
          {failed.map((f) => (
            <p key={f} className="job-error" role="status">
              {f}
            </p>
          ))}

          {asking && (
            <div className="confirm" role="group" aria-label={t`Mod requirements`}>
              <p>
                {plural(missing, {
                  one: `${mod} needs ${needed}, which isn't installed yet. Install it as well?`,
                  other: `${mod} needs ${needed}, which aren't installed yet. Install them as well?`,
                })}
              </p>
              <div className="row">
                <button className="primary" onClick={() => (setAsking(null), void install(asking.pkg, asking.missing))}>
                  {plural(missing, { one: "Install both", other: "Install all" })}
                </button>
                <button onClick={() => (setAsking(null), void install(asking.pkg))}>
                  <Trans comment="Installs the mod alone, without the ones it needs">Just {mod}</Trans>
                </button>
                <button onClick={() => setAsking(null)}>
                  <Trans>Cancel</Trans>
                </button>
              </div>
            </div>
          )}
          {picking && (
            <div className="confirm" role="group" aria-label={t`Choose a file`}>
              <p>
                <Trans>{modWithFiles} has more than one file. Which one do you want?</Trans>
              </p>
              <ul className="mod-files">
                {picking.files.map((f) => (
                  <li key={f.id}>
                    <button
                      onClick={() => {
                        setPicking(null);
                        void act(picking.pkg.key, t`Installing…`, () => put(picking.pkg, f));
                      }}
                    >
                      <strong>{f.name}</strong>
                      <small>
                        {size(f.size)}
                        {f.description && ` · ${f.description}`}
                      </small>
                    </button>
                  </li>
                ))}
              </ul>
              <button onClick={() => setPicking(null)}>
                <Trans>Cancel</Trans>
              </button>
            </div>
          )}

          {tab === "browse" ? (
            <>
              <div className="mods-filters">
                <input type="search" placeholder={t`Search mods`} aria-label={t`Search mods`} value={search} onChange={(e) => (setSearch(e.target.value), setShown(PAGE))} />
                <select aria-label={t`Sort mods`} value={sort} onChange={(e) => setSort(e.target.value as Sort)}>
                  {Object.entries(SORTS).map(([value, label]) => (
                    <option key={value} value={value}>
                      {i18n._(label)}
                    </option>
                  ))}
                </select>
              </div>
              <div className="chips mods-chips">
                {providers.length > 1 &&
                  [null, ...providers].map((p) => (
                    <button key={p ?? "all"} type="button" className={`chip${provider === p ? " on" : ""}`} onClick={() => (setProvider(p), setShown(PAGE))}>
                      {p ? PROVIDERS[p] ?? p : t({ message: "All", comment: "Shows mods from every site" })}
                    </button>
                  ))}
                <label className="toggle">
                  <input type="checkbox" checked={nsfw} onChange={(e) => setNsfw(e.target.checked)} />
                  <Trans>Include NSFW</Trans>
                </label>
              </div>
              {!packages ? (
                <p className="muted" role="status">
                  <Trans>Loading mods…</Trans>
                </p>
              ) : sorted.length ? (
                <>
                  <ul className="mods-list" aria-label={t`Mods to browse`}>
                    {sorted.slice(0, shown).map((p) => row(p.key, p, mine.get(p.key)))}
                  </ul>
                  {sorted.length > shown && (
                    <button onClick={() => setShown((n) => n + PAGE)}>
                      <Trans>Show more</Trans>
                    </button>
                  )}
                </>
              ) : (
                <p className="muted">{query ? t`No mods match your search.` : t`There are no mods here yet.`}</p>
              )}
            </>
          ) : (
            <>
              {updates.length > 0 && (
                <div className="mods-updates">
                  <span className="muted">
                    {plural(updateCount, { one: "# mod has an update.", other: "# mods have updates." })}
                  </span>
                  <button className="primary" disabled={Object.keys(busy).length > 0} onClick={() => void updateAll()}>
                    <Trans>Update all</Trans>
                  </button>
                </div>
              )}
              {installed.length ? (
                <ul className="mods-list" aria-label={t`Installed mods`}>
                  {[...installed].sort((a, b) => a.name.localeCompare(b.name)).map((m) => row(m.key, byKey.get(m.key), m))}
                </ul>
              ) : (
                <p className="muted">
                  <Trans>No mods are installed yet. Find some under Browse.</Trans>
                </p>
              )}
            </>
          )}
          <p className="muted small">
            <Trans>Mods go in {modsPath} in the app&apos;s folder. Files you put there yourself are left alone.</Trans>
          </p>
        </div>
      </section>
    </div>
  );
}
