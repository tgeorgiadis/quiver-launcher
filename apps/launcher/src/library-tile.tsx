/**
 * A library card, in the grid and in TV mode's library alike, as the designers drew it: the picture, with the names
 * under it or over its foot and a mark for this computer's copy (Get's symbol when it isn't installed and can be, a
 * cloud for an update, a warning for a withdrawn release, none when it's ready to play), which says it without a badge
 * on the picture as well. On hover or focus the picture dims and the card shows what's installed and verified, buttons
 * for the app's folder, playlists and its menu, and Play: along the foot of the art, with Update to its version under
 * it when there's one (a round Play in the corner, and Update with the small buttons, on an icon card). A download in
 * progress, or one that failed, shows without hover. A withdrawn release's hover or focus says why, with the way off
 * it and Play anyway. In TV mode, focus shows its buttons alone.
 *
 * And a game's card, for its ports in the library (group.ts): the game's name over the port to play, and a list of
 * every port with its button, from the card.
 */
import { memo, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { ChevronDown, CloudDownload, Compass, Download, FolderOpen, History, Layers, Play, Plus, TriangleAlert } from "lucide-react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { Entry } from "@quiverlauncher/api";
import { Artwork, ScrollingText, boxSubtitle, cardSources, coverOf } from "@quiverlauncher/ui";
import { canGet, hasUpdate, pulledVerified, useLauncher, useLauncherNow, type CatalogEntry, type Install, type Job } from "./store";
import { bare, versionLabel, wayOffLabel } from "./versions";
import { VersionsDialog, foldersOn } from "./detail";
import { native, type Config } from "./native";
import { CardArt, artSizes } from "./library-view";
import { CopyStatus, PlaylistPicker, Popover } from "./library";
import { CardMenu, GameMenu } from "./card-menu";
import { gameVersionLabel } from "./game";
import type { Stack } from "./group";
import type { CardImage, CardSize, NameOrder } from "./view";

/** The large name and the small one under it: the app's, and every game it's based on, as the catalog's cards list them. */
export function namesOf(entry: Entry, order: NameOrder): [string, string | undefined] {
  const games = entry.games.map((g) => g.title).join(", ");
  if (!games || games === entry.projectName) return [entry.projectName, undefined];
  return order === "game" ? [games, entry.projectName] : [entry.projectName, games];
}

export type Copy = "missing" | "ready" | "update" | "withdrawn" | "busy" | "failed";

/** What this computer has of an app, by `latest`, the catalog's: a download (or one that failed) outranks the rest. */
export function copyFor(latest: CatalogEntry, install: Install | undefined, job: Job | undefined): Copy {
  if (job) return "error" in job ? "failed" : "busy";
  if (install && latest.withdrawn?.some((w) => w.version === install.version)) return "withdrawn";
  if (install && hasUpdate(latest, install)) return "update";
  return install ? "ready" : "missing";
}

/**
 * Beside the name, what the app wants of the player: Get's symbol for one to download (that can be, here), a cloud for
 * an update; nothing for one ready to play. A download's and a failure's marks always show; the rest only with `marks`.
 */
function Mark({ copy, marks, gettable }: { copy: Copy; marks: boolean; gettable: boolean }) {
  if (copy === "withdrawn") return <TriangleAlert className="tile-mark" data-copy={copy} size={15} strokeWidth={2.5} aria-hidden="true" />;
  if (copy === "busy" || copy === "failed") return <span className="tile-mark" data-copy={copy} aria-hidden="true" />;
  if (!marks || copy === "ready") return null;
  if (copy === "missing") return gettable ? <Download className="tile-mark" data-copy={copy} size={15} strokeWidth={2.5} aria-hidden="true" /> : null;
  return <CloudDownload className="tile-mark" data-copy={copy} size={16} strokeWidth={2.5} aria-hidden="true" />;
}

type TileProps = {
  entry: Entry;
  /** The picture (view.ts): box art, an icon, or the catalog's cover; and the cards' size, for the copy of it to load. */
  image: CardImage;
  size: CardSize;
  names: boolean;
  nameOrder: NameOrder;
  /** The marks for this computer's copy (view.ts). */
  marks: boolean;
  /** In the player's library, not only installed here: its playlists and its menu. */
  inLibrary: boolean;
  /**
   * Installed here but not in the library (library.tsx's section for them): Add as its button, in Play's place, which
   * Options (Y) presses, as Get on a catalog card; Confirm (A) opens its page.
   */
  onAdd?: (entry: Entry) => void;
  /** A download's progress, or what went wrong, in place of its buttons (App.tsx's Action). */
  action: (entry: Entry) => ReactNode;
  onOpen: (entry: Entry) => void;
  /** The letter it files under in a library by name, for the triggers' jumps and the letters beside it. */
  letter?: string;
  /**
   * In TV mode's library (tv.tsx): on focus its buttons alone (and a withdrawn release's why), the facts and the small
   * buttons being for a mouse, and small from across a room; its menu from Options or a right-click.
   */
  tv?: boolean;
};

/**
 * A card, with what this computer has of its app. Given the same `action` and `onOpen` from one render to the next,
 * it's drawn again only as its own app changes, not with each change to the launcher (another app's download, say),
 * which redrew every card of a large library.
 */
export function LibraryTile(props: TileProps) {
  const { config, installs, jobs, catalog, consoleNames } = useLauncher();
  const id = props.entry.id;
  return <Tile {...props} os={config.os} install={installs[id]} job={jobs[id]} latest={catalog[id] ?? props.entry} consoleNames={consoleNames} />;
}

const Tile = memo(function Tile({
  entry,
  image,
  size,
  names,
  nameOrder,
  marks,
  inLibrary,
  onAdd,
  action,
  onOpen,
  letter,
  tv = false,
  os,
  install,
  job,
  latest,
  consoleNames,
}: TileProps & { os: Config["os"]; install?: Install; job?: Job; latest: CatalogEntry; consoleNames: Record<string, string> }) {
  // What a press does, with the launcher as it is then.
  const now = useLauncherNow();
  const card = useRef<HTMLElement>(null);
  const update = Boolean(install && !job && hasUpdate(latest, install));
  // A download, even the one off it, outranks the warning.
  const pulled = install && !job ? latest.withdrawn?.find((w) => w.version === install.version) : undefined;
  const copy = copyFor(latest, install, job);
  const [large, games] = namesOf(entry, nameOrder);
  // Under the app's name, its games and console, as Browse's cards have them.
  const small = nameOrder === "game" ? games : boxSubtitle(entry, (tag) => (Object.hasOwn(consoleNames, tag) ? consoleNames[tag] : undefined)) || undefined;
  const verified = latest.verified?.version;
  const newer =
    latest.lastReleaseVersion && verified && bare(latest.lastReleaseVersion) !== bare(verified) && (latest.lastReleaseAt ?? 0) > (latest.verified?.releasedAt ?? 0)
      ? latest.lastReleaseVersion
      : undefined;
  const wide = Boolean(entry.libraryArt?.header || entry.libraryArt?.capsule);
  const coverArt = coverOf(entry);
  // Box art and icons are the library's own pictures; a cover is the catalog card's, at about the size it shows.
  const cover =
    image === "cover" ? (
      <Artwork src={coverArt} sources={coverArt ? cardSources(coverArt, artSizes("cover", size)) : undefined} name={entry.projectName} className={wide ? "cover-photo" : ""} />
    ) : (
      <CardArt entry={entry} image={image} sizes={artSizes(image, size)} />
    );
  // What went wrong or a download's progress: the card's usual button, shown all the time.
  const busy = Boolean(job);
  const pinned = install?.updates === "pinned";
  // The app's other versions, from beside Install; or in its place, when the verified one was withdrawn too.
  const [versions, setVersions] = useState(false);
  const [getting, setGetting] = useState(false);
  const builds = os === "linux" && latest.supportedOS.includes("linux") && latest.supportedOS.includes("windows");
  const noWayOff = Boolean(pulledVerified(latest));
  // Not here yet, but can be: Get's symbol beside its name, and Get as its button.
  const gettable = canGet(entry, os);
  // Play, or Get, as the large round button in the card's corner, which the names make room for (app.css).
  const round = !busy && !pulled && Boolean(install || gettable);
  const open = () => onOpen(entry);
  const get = (e: Entry) => void now.current.get(e);
  const play = () => void now.current.play(entry.id);
  // Plain names for the sentences below to put in place.
  const name = entry.projectName;
  const verifiedLabel = versionLabel(verified);
  const installedLabel = install ? versionLabel(install.version) : "";
  const pulledLabel = pulled ? versionLabel(pulled.version) : "";
  const newerLabel = newer ? versionLabel(newer) : "";
  const getIt = (
    <button type="button" className="tile-play tile-get" onClick={() => get(entry)}>
      <Download size={24} />
      <span className="tile-label">{t({ message: "Get", comment: "Button: download and install the app" })}</span>
    </button>
  );
  const others = (
    <button type="button" className="tile-versions" title={t`Choose a version`} aria-label={t`Choose a version of ${name}`} onClick={() => setVersions(true)}>
      <ChevronDown size={16} strokeWidth={2.5} aria-hidden="true" />
    </button>
  );
  return (
    <article
      ref={card}
      className="entry-card library-tile"
      data-slug={entry.slug}
      data-letter={letter}
      data-copy={copy}
      data-busy={busy || undefined}
      data-withdrawn={pulled ? "" : undefined}
      data-round={round || undefined}
    >
      {/* TV mode's has no ⋯ button (a mouse's), and its menu, closed, gives the focus back to the card. */}
      {tv && inLibrary && <GameMenu entry={entry} onOpen={open} target={() => card.current} bare where="card" />}
      <button type="button" className="card-open" onClick={open} aria-label={pulled ? t`${name}, ${pulledLabel} was withdrawn` : name}>
        <span className="card-cover">{cover}</span>
        <span className="card-body">
          {names ? (
            <>
              <span className="tile-name">
                <span className="card-title">
                  <ScrollingText text={large} />
                </span>
                <Mark copy={copy} marks={marks} gettable={gettable} />
              </span>
              {small && (
                <span className="card-subtitle">
                  <ScrollingText text={small} />
                </span>
              )}
            </>
          ) : (
            <Mark copy={copy} marks={marks} gettable={gettable} />
          )}
        </span>
      </button>
      <div className="tile-overlay">
        {/* Its page in the catalog, in the top left corner, with Browse's compass, as Continue playing has it: not only from its menu. */}
        {!tv && (
          <button type="button" className="tile-icon tile-page" title={t`See it in the catalog`} onClick={open}>
            <Compass size={17} />
            <span className="sr-only">
              <Trans>See {name} in the catalog</Trans>
            </span>
          </button>
        )}
        {/* A withdrawn release: why, in place of the rest, which the app's page has. */}
        {(!tv || pulled) && (
          <div className="tile-facts">
            {pulled ? (
              <span className="tile-withdrawn">
                <strong>
                  <Trans>{pulledLabel} was withdrawn.</Trans>
                </strong>{" "}
                {pulled.reason}
              </span>
            ) : (
              <>
                <span className="card-status">
                  <CopyStatus entry={entry} inLibrary={inLibrary} />
                </span>
                {install && (
                  <span>
                    <Trans>Installed {installedLabel}</Trans>
                  </span>
                )}
                {verified && (
                  <span>
                    <Trans>Verified {verifiedLabel}</Trans>
                  </span>
                )}
                {newer && (
                  <span className="tile-unverified">
                    <Trans>{newerLabel} not verified yet</Trans>
                  </span>
                )}
                {install?.unverified && <span className="tile-unverified">{install.blocked ? t`Quiver blocked this version` : t`This version isn't verified`}</span>}
                {pinned && (
                  <span>
                    <Trans>Staying on {installedLabel}</Trans>
                  </span>
                )}
              </>
            )}
          </div>
        )}
        {!tv && (
          <div className="tile-icons">
            {update && !busy && !pulled && (
              <button type="button" className="tile-icon tile-icon-update" title={t`Update to ${verifiedLabel}`} onClick={() => get(latest)}>
                <CloudDownload size={17} />
                <span className="sr-only">
                  <Trans>Update to {verifiedLabel}</Trans>
                </span>
              </button>
            )}
            {install && foldersOn(os) && (
              <button
                type="button"
                className="tile-icon"
                title={t`Open folder`}
                onClick={() => void native.openFolder(entry.id).catch((e) => now.current.setNotice(String(e)))}
              >
                <FolderOpen size={17} />
                <span className="sr-only">
                  <Trans>Open folder</Trans>
                </span>
              </button>
            )}
            {inLibrary && (
              <>
                <PlaylistPicker id={entry.id} />
                <CardMenu entry={entry} onOpen={open} />
              </>
            )}
          </div>
        )}
        <div className="tile-main">
          {busy ? (
            action(entry)
          ) : onAdd ? (
            <button type="button" className="tile-play tile-get tile-add" onClick={() => onAdd(entry)}>
              <Plus size={24} />
              <span className="tile-label">
                <Trans comment="Button that adds an app installed here to the library">Add</Trans>
              </span>
            </button>
          ) : pulled ? (
            <div className="tile-withdrawn-actions">
              {/* A withdrawn release outranks a pin: the way off it is right here. */}
              {noWayOff ? (
                <button type="button" className="tile-fix" onClick={() => setVersions(true)}>
                  <History size={15} aria-hidden="true" />
                  <span>
                    <Trans>Choose a version</Trans>
                  </span>
                </button>
              ) : (
                verified && (
                  <span className="tile-split">
                    <button
                      type="button"
                      className="tile-fix"
                      title={wayOffLabel(install!, latest.verified!)}
                      onClick={() => (pinned && now.current.setUpdates(entry.id, undefined), get(latest))}
                    >
                      <Download size={15} aria-hidden="true" />
                      <span>{wayOffLabel(install!, latest.verified!)}</span>
                    </button>
                    {others}
                  </span>
                )
              )}
              <button type="button" className="tile-anyway" onClick={play}>
                <Trans>Play anyway</Trans>
              </button>
            </div>
          ) : install ? (
            <>
              <button type="button" className="tile-play" onClick={play}>
                <Play size={26} fill="currentColor" />
                <span className="tile-label">
                  <Trans comment="Button: start the app">Play</Trans>
                </span>
              </button>
              {update && (
                <button type="button" className="tile-update" title={t`Update to ${verifiedLabel}`} onClick={() => get(latest)}>
                  {/* To which version; on a narrow card, the word alone (app.css). */}
                  <span className="tile-label tile-label-long">
                    <Trans>Update to {verifiedLabel}</Trans>
                  </span>
                  <span className="tile-label tile-label-short">
                    <Trans context="button" comment="Button on a library card: update the app to its newest verified version">
                      Update
                    </Trans>
                  </span>
                </button>
              )}
            </>
          ) : gettable && noWayOff ? (
            <button type="button" className="tile-play tile-get" title={t`Its newest verified version was withdrawn: choose a version`} onClick={() => setVersions(true)}>
              <History size={24} />
              <span className="tile-label">{t`Choose a version`}</span>
            </button>
          ) : (
            gettable &&
            // A phone has one build to get, and an app's versions are in its menu: Get alone.
            (os === "android" ? (
              getIt
            ) : (
              // Get, and joined to it an arrow for the version, and on Linux the build, to get in place of Get's own choice.
              <span className="tile-get-split">
                {getIt}
                <button
                  type="button"
                  className="tile-versions tile-get-more"
                  title={builds ? t`Choose a version or platform` : t`Choose a version`}
                  aria-label={builds ? t`Choose a version or platform of ${name}` : t`Choose a version of ${name}`}
                  onClick={() => setGetting(true)}
                >
                  <ChevronDown size={16} strokeWidth={2.5} aria-hidden="true" />
                </button>
              </span>
            ))
          )}
        </div>
      </div>
      {versions && <VersionsDialog entry={latest} onClose={() => setVersions(false)} />}
      {getting && <VersionsDialog entry={latest} getting onClose={() => setGetting(false)} />}
    </article>
  );
});

/**
 * A game's ports, from its card: each one's picture, names and state, the way to its page, and its button. A download
 * started here, or a port played, closes it and puts the focus back on the card, which shows the download from then on;
 * Cancel, Try again and Dismiss keep it open. A port's button that changes under the focus (a download done, Play in its
 * place) leaves it on that port's line, not on nothing.
 */
function PortList({
  title,
  ports,
  consoleNames,
  inLibrary,
  action,
  onOpen,
  close,
  card,
}: {
  title: string;
  ports: Entry[];
  consoleNames: Record<string, string>;
  inLibrary: (id: string) => boolean;
  action: (entry: Entry) => ReactNode;
  onOpen: (entry: Entry) => void;
  close: () => void;
  /** The card's own button, for the focus. */
  card: () => HTMLElement | null | undefined;
}) {
  const { jobs, installs } = useLauncher();
  const list = useRef<HTMLDivElement>(null);
  // The ports with a download (or one that failed) as it opened, and when each was played.
  const was = useRef({ jobs: new Set(ports.flatMap((p) => (jobs[p.id] ? [p.id] : []))), played: Object.fromEntries(ports.map((p) => [p.id, installs[p.id]?.lastPlayed ?? 0])) });
  // The port whose line had the focus last.
  const line = useRef<string>(undefined);
  // As the download shows, before anything after it (Which download?, which gives the focus back to what had it then).
  useLayoutEffect(() => {
    const started = ports.some((p) => jobs[p.id] && !was.current.jobs.has(p.id));
    const played = ports.some((p) => (installs[p.id]?.lastPlayed ?? 0) > was.current.played[p.id]);
    if (started || played) {
      close();
      card()?.focus({ preventScroll: true });
      return;
    }
    // A download stopped or dismissed here: another started after it closes the list too.
    for (const id of was.current.jobs) if (!jobs[id]) was.current.jobs.delete(id);
    const now = document.activeElement;
    if (!line.current || (now && now !== document.body)) return;
    const row = list.current?.querySelector(`.port-row[data-slug="${CSS.escape(line.current)}"]`);
    (row?.querySelector<HTMLElement>(".port-action :is(button, a[href]):not(:disabled)") ?? row?.querySelector<HTMLElement>(".port-open"))?.focus({ preventScroll: true });
  });
  const apps = ports.length;
  return (
    <div className="port-list" ref={list} onFocus={(e) => (line.current = e.target.closest(".port-row")?.getAttribute("data-slug") ?? undefined)}>
      <div className="port-head">
        <strong>{title}</strong>
        <span className="muted">{plural(apps, { one: "# way to play", other: "# ways to play" })}</span>
      </div>
      {ports.map((port) => {
        // How it plays the game, and who made it.
        const about = [port.basedOn && gameVersionLabel(port.basedOn, consoleNames), port.developer?.name].filter(Boolean).join(" · ");
        return (
          <div key={port.id} className="port-row" data-slug={port.slug}>
            <button type="button" className="port-open" onClick={() => (close(), onOpen(port))}>
              <CardArt entry={port} image="icon" sizes="48px" />
              <span className="port-names">
                <strong>{port.projectName}</strong>
                {about && <small>{about}</small>}
                <small className="card-status">
                  <CopyStatus entry={port} inLibrary={inLibrary(port.id)} />
                </small>
              </span>
            </button>
            <div className="port-action">{action(port)}</div>
          </div>
        );
      })}
    </div>
  );
}

type GameTileProps = Omit<TileProps, "entry" | "nameOrder" | "inLibrary"> & {
  stack: Stack;
  /** Whether a port is in the player's library, not only on a playlist: what its line in the list says. */
  inLibrary: (id: string) => boolean;
};

/** Which of the game's marks outranks the others: a download or a failure, then a withdrawn release, an update, one installed. */
const COPY_RANK: Copy[] = ["failed", "busy", "withdrawn", "update", "ready", "missing"];

/**
 * A game's card. It's drawn again only as one of its ports, or what this computer has of one, changes; a download's
 * progress and the list's lines follow the launcher by themselves (App.tsx's Action, CopyStatus).
 */
export function GameTile(props: GameTileProps) {
  const { config, installs, jobs, catalog, consoleNames } = useLauncher();
  const { ports } = props.stack;
  const copies = ports.map((p) => copyFor(catalog[p.id] ?? p, installs[p.id], jobs[p.id]));
  const copy = COPY_RANK.find((c) => copies.includes(c)) ?? "missing";
  const gettable = ports.some((p) => canGet(p, config.os));
  // The port Play starts: the first installed, the one played last before any other (group.ts).
  const ready = ports.find((p, i) => installs[p.id] && copies[i] !== "withdrawn");
  // The port whose download, or failure, the card shows, as an app's own card does.
  const busy = ports.find((_, i) => copies[i] === "busy" || copies[i] === "failed");
  const state = ports.map((p, i) => `${p.id} ${copies[i]} ${installs[p.id]?.lastPlayed ?? ""} ${props.inLibrary(p.id)}`).join("|");
  return <Game {...props} copy={copy} gettable={gettable} ready={ready} busy={busy} state={state} consoleNames={consoleNames} />;
}

const Game = memo(
  function Game({
    stack: { game, ports },
    image,
    size,
    names,
    marks,
    inLibrary,
    action,
    onOpen,
    letter,
    tv = false,
    copy,
    gettable,
    ready,
    busy,
    consoleNames,
  }: GameTileProps & { copy: Copy; gettable: boolean; ready?: Entry; busy?: Entry; state: string; consoleNames: Record<string, string> }) {
    const now = useLauncherNow();
    const article = useRef<HTMLElement>(null);
    const first = ports[0];
    const coverArt = coverOf(first);
    const wide = Boolean(first.libraryArt?.header || first.libraryArt?.capsule);
    const cover =
      image === "cover" ? (
        <Artwork src={coverArt} sources={coverArt ? cardSources(coverArt, artSizes("cover", size)) : undefined} name={game.title} className={wide ? "cover-photo" : ""} />
      ) : (
        <CardArt entry={first} image={image} sizes={artSizes(image, size)} />
      );
    const apps = ports.length;
    // Under the game's name, its one port's, or how many it has.
    const small = apps === 1 ? first.projectName : plural(apps, { one: "# way to play", other: "# ways to play" });
    const title = game.title;
    const name = ready?.projectName ?? "";
    return (
      <article ref={article} className="entry-card library-tile game-tile" data-game={game.slug} data-letter={letter} data-copy={copy} data-busy={busy ? "" : undefined} data-round={(!busy && Boolean(ready)) || undefined}>
        <Popover
          label={title}
          panelLabel={t`Ways to play ${title}`}
          role="group"
          className="card-open"
          panel="port-panel"
          icon={
            <>
              <span className="card-cover">
                {cover}
                {apps > 1 && (
                  <span className="tile-count" aria-hidden="true">
                    <Layers size={13} strokeWidth={2.5} />
                    {apps}
                  </span>
                )}
              </span>
              <span className="card-body">
                {names ? (
                  <>
                    <span className="tile-name">
                      <span className="card-title">
                        <ScrollingText text={title} />
                      </span>
                      <Mark copy={copy} marks={marks} gettable={gettable} />
                    </span>
                    <span className="card-subtitle">
                      <ScrollingText text={small} />
                    </span>
                  </>
                ) : (
                  <Mark copy={copy} marks={marks} gettable={gettable} />
                )}
              </span>
            </>
          }
        >
          {(close) => (
            <PortList
              title={title}
              ports={ports}
              consoleNames={consoleNames}
              inLibrary={inLibrary}
              action={action}
              onOpen={onOpen}
              close={close}
              card={() => article.current?.querySelector<HTMLElement>(".card-open")}
            />
          )}
        </Popover>
        <div className="tile-overlay">
          {!tv && ready && (
            <div className="tile-facts">
              <span className="card-status">
                <CopyStatus entry={ready} inLibrary={inLibrary(ready.id)} />
              </span>
              {apps > 1 && <span>{name}</span>}
            </div>
          )}
          <div className="tile-main">
            {busy ? (
              action(busy)
            ) : (
              ready && (
                // Which port it starts, where there's more than one: its words, a controller's hint (spatial.ts) too.
                <button type="button" className="tile-play" title={apps > 1 ? t`Play ${name}` : undefined} onClick={() => void now.current.play(ready.id)}>
                  <Play size={26} fill="currentColor" />
                  <span className="tile-label">{apps > 1 ? t`Play ${name}` : <Trans comment="Button: start the app">Play</Trans>}</span>
                </button>
              )
            )}
          </div>
        </div>
      </article>
    );
  },
  // Its ports by themselves, not the list made of them again each time, and what this computer has of each (`state`).
  (a, b) =>
    a.state === b.state &&
    a.copy === b.copy &&
    a.gettable === b.gettable &&
    a.ready === b.ready &&
    a.busy === b.busy &&
    a.stack.game.id === b.stack.game.id &&
    a.stack.game.title === b.stack.game.title &&
    a.stack.ports.length === b.stack.ports.length &&
    a.stack.ports.every((p, i) => p === b.stack.ports[i]) &&
    a.image === b.image &&
    a.size === b.size &&
    a.names === b.names &&
    a.marks === b.marks &&
    a.letter === b.letter &&
    a.tv === b.tv &&
    a.consoleNames === b.consoleNames &&
    a.action === b.action &&
    a.onOpen === b.onOpen,
);
