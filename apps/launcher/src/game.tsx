/**
 * Original games, as on quiverlauncher.com: the games a search matches, shown
 * above the apps, and a game's page comparing every app that plays it.
 */
import { useEffect, useState, type ReactNode } from "react";
import { ArrowLeft } from "lucide-react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { BasedOn, Entry, Game, GameDetail, GameMatch } from "@quiverlauncher/api";
import { Artwork, EntryCard, boxOf, cardSources } from "@quiverlauncher/ui";
import { CATALOG_BOX } from "./library-view";
import { useLauncher } from "./store";
import { byPlayerFeedback } from "./catalog";

/** How a version reads, as on the website: "PlayStation version", "PlayStation · Director's Cut". */
export function gameVersionLabel(version: BasedOn, consoleNames: Record<string, string>) {
  const console = version.console && (consoleNames[version.console] ?? version.console.toUpperCase());
  if (console && version.edition) return `${console} · ${version.edition}`;
  return version.edition ?? t({ message: `${console} version`, comment: "A version of a game, by the console it came out on: PlayStation version" });
}

const versionKey = (version?: BasedOn) => (version ? `${version.console ?? ""}|${version.edition?.toLowerCase() ?? ""}` : "");

/**
 * Apps by the version of the game they're based on, in the order given, with
 * apps of no known version last; one group when fewer than two are known.
 */
export function byVersion(entries: Entry[]) {
  const groups = new Map<string, { version?: BasedOn; entries: Entry[] }>();
  for (const entry of entries) {
    const key = versionKey(entry.basedOn);
    const group = groups.get(key) ?? { version: entry.basedOn, entries: [] };
    group.entries.push(entry);
    groups.set(key, group);
  }
  const known = [...groups.values()].filter((g) => g.version);
  if (known.length < 2) return [{ version: undefined, entries }];
  return [...known, ...(groups.get("") ? [groups.get("")!] : [])];
}

/** What's known of a game before its page loads, to show its title meanwhile. */
export type GameLink = { slug: string; title?: string };

export function GamesSection({ games, onOpen }: { games: GameMatch[]; onOpen: (game: GameLink) => void }) {
  return (
    <section className="search-games" aria-label={t`Matching games`}>
      <h2 className="catalog-section-label">
        <Trans>Games</Trans>
      </h2>
      <ul>
        {games.map((g) => {
          const { matched, apps } = g;
          return (
            <li key={g.slug}>
              <button type="button" data-game={g.slug} onClick={() => onOpen(g)}>
                <Artwork src={g.art} name={g.title} className="search-game-art cover-photo" />
                <span>
                  <strong>{g.title}</strong>
                  {matched && (
                    <small className="search-game-aka">
                      <Trans>Also known as {matched}</Trans>
                    </small>
                  )}
                  <small>{plural(apps, { one: "# way to play", other: "# ways to play" })} →</small>
                </span>
              </button>
            </li>
          );
        })}
      </ul>
    </section>
  );
}

/** "Called Mother in Japan", "Called Mega Man in the US": where a name is used. */
const inRegion = (title: string, region: string) => (/^(US|UK)\b/.test(region) ? t`Called ${title} in the ${region}` : t`Called ${title} in ${region}`);

/** Under a game's title, as on the website: when it came out, where it's called that, and its other names. */
function GameNames({ game }: { game: Game }) {
  const others = game.alternateTitles?.join(" · ");
  const parts = [
    game.year && <span key="year">{game.year}</span>,
    game.titleRegion && <span key="region">{inRegion(game.title, game.titleRegion)}</span>,
    !!game.alternateTitles?.length && (
      <span key="others">
        <Trans>
          Also known as <strong>{others}</strong>
        </Trans>
      </span>
    ),
  ].filter(Boolean);
  if (!parts.length) return null;
  return <p className="game-akas">{parts.flatMap((part, i) => (i ? [" · ", part] : [part]))}</p>;
}

// The last answer for each game, so going back to one shows it at once.
const seen = new Map<string, GameDetail | null>();

/** A game's page: the original game, and the apps that play it, best first. */
export function GamePage({
  game: link,
  back,
  onBack,
  onOpenApp,
  action,
}: {
  game: GameLink;
  back: string;
  onBack: () => void;
  onOpenApp: (entry: Entry) => void;
  action: (entry: Entry) => ReactNode;
}) {
  const { client, library, consoleNames, remember } = useLauncher();
  const [data, setData] = useState<GameDetail | null | undefined>(() => seen.get(link.slug));
  const [failed, setFailed] = useState(false);
  const [round, setRound] = useState(0);
  const [expanded, setExpanded] = useState(false);
  useEffect(() => {
    let live = true;
    setFailed(false);
    client.game(link.slug).then(
      (d) => {
        if (!live) return;
        seen.set(link.slug, d);
        setData(d);
        if (d) remember(d.entries);
      },
      () => live && setFailed(true),
    );
    return () => {
      live = false;
    };
  }, [client, link.slug, round]); // eslint-disable-line react-hooks/exhaustive-deps

  const backLink = (
    <button className="back-link" onClick={onBack}>
      <ArrowLeft size={15} /> {back}
    </button>
  );
  if (data === null)
    return (
      <section className="app-page game-page" aria-label={t`Game not found`}>
        <div className="backdrop-inner">{backLink}</div>
        <div className="empty">
          <h2>
            <Trans>Game not found</Trans>
          </h2>
          <button className="primary" onClick={onBack}>
            <Trans>Return to catalog</Trans>
          </button>
        </div>
      </section>
    );
  const game = data?.game;
  const title = game?.title ?? link.title ?? "";
  const backdrop = game?.libraryArt?.hero || game?.libraryArt?.header;
  const boxArt = game?.libraryArt?.capsule;
  const systems = [...new Set(game?.originalSystems ?? [])];
  const entries = data ? [...data.entries].sort(byPlayerFeedback) : [];
  const groups = byVersion(entries);
  const inLibrary = new Set(library.map((i) => i.id));
  return (
    <section className="app-page game-page" aria-label={title || t`Game`} aria-busy={data === undefined} data-game={link.slug}>
      <div className={`app-backdrop${backdrop ? " has-art" : ""}`}>
        {backdrop && <img key={backdrop} className="backdrop-art" src={backdrop} alt="" onError={(e) => (e.currentTarget.hidden = true)} />}
        <div className="backdrop-inner">
          {backLink}
          <div className="app-hero">
            {game && <Artwork key={boxArt ?? game.artwork} src={boxArt ?? game.artwork} name={title} className={boxArt ? "cover-photo box-art" : ""} />}
            <div>
              <div className="eyebrow">
                <Trans>THE ORIGINAL GAME</Trans>
              </div>
              <h1>{title}</h1>
              {game && <GameNames game={game} />}
              {game?.description.trim() && (
                <p
                  className={`app-tagline game-description${expanded ? " expanded" : ""}`}
                  title={expanded ? undefined : t`Show the full description`}
                  onClick={() => setExpanded((open) => !open)}
                >
                  {game.description}
                </p>
              )}
              {systems.length > 0 && (
                <div className="detail-tags">
                  {systems.map((id) => (
                    <span key={id}>{consoleNames[id] ?? id.toUpperCase()}</span>
                  ))}
                </div>
              )}
            </div>
          </div>
        </div>
      </div>
      <div className="game-body">
        {data === undefined ? (
          failed ? (
            <div className="empty">
              <p>
                <Trans>Couldn't reach quiverlauncher.com. Check your connection and try again.</Trans>
              </p>
              <button className="primary" onClick={() => setRound((r) => r + 1)}>
                <Trans>Try again</Trans>
              </button>
            </div>
          ) : (
            <div className="more" role="status" aria-label={t`Loading the game`}>
              <span className="spinner" />
            </div>
          )
        ) : (
          <>
            <div className="section-top">
              <h2>
                <Trans>Ways to play</Trans> <span className="tab-count">{entries.length}</span>
              </h2>
              <span className="muted">{entries.length > 1 ? t`Best first, by player feedback` : t`Community ports and recreations`}</span>
            </div>
            {entries.length === 0 ? (
              <p className="empty">
                <Trans>No apps play this game yet.</Trans>
              </p>
            ) : (
              groups.map((group) => (
                // Apps based on different versions (PlayStation, N64) are grouped by version.
                <section key={versionKey(group.version)} className="game-version" aria-label={group.version && gameVersionLabel(group.version, consoleNames)}>
                  {groups.length > 1 && (
                    <h3 className="game-version-heading">
                      {group.version ? gameVersionLabel(group.version, consoleNames) : t`Other ways to play`}
                      <span className="tab-count">{group.entries.length}</span>
                    </h3>
                  )}
                  <div className="catalog-grid boxes">
                    {group.entries.map((entry) => (
                      <EntryCard
                        key={entry.id}
                        entry={entry}
                        coverSources={boxOf(entry) ? cardSources(boxOf(entry)!, CATALOG_BOX) : undefined}
                        consoleNames={consoleNames}
                        onOpen={() => onOpenApp(entry)}
                        badge={inLibrary.has(entry.id) ? t`In library` : undefined}
                        action={action(entry)}
                      />
                    ))}
                  </div>
                </section>
              ))
            )}
          </>
        )}
      </div>
    </section>
  );
}
