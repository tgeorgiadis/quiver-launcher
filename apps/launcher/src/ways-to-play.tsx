/**
 * Ways to play, on an app's page: a pill beside "Based on" saying how many apps
 * play its game, with a list of them, best first by player feedback, so a
 * player sees at once whether there's a better one for them.
 */
import { useEffect, useState } from "react";
import { ChevronDown, Layers } from "lucide-react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { Entry, GameDetail } from "@quiverlauncher/api";
import { PlatformIcons } from "@quiverlauncher/ui";
import { CardArt } from "./library-view";
import { Popover } from "./library";
import { availableOn, useLauncher } from "./store";
import { byPlayerFeedback, feedbackScore, reportCount } from "./catalog";

/** Fewer reports than this, and an app's share of "runs well" says too little to show, or to rank it top. */
export const ENOUGH_REPORTS = 5;

/** The share of players who said an app runs well, in whole percent; none without enough reports. */
export function runsWellShare(entry: Pick<Entry, "recommended" | "reportIssues" | "reportBroken">) {
  const total = reportCount(entry);
  return total >= ENOUGH_REPORTS ? Math.round((100 * entry.recommended) / total) : undefined;
}

/**
 * The apps that play a game, best first, those this computer can run before
 * those it can't; and the one players rate top, when it has enough reports and
 * is ahead of the next (not tied with it).
 */
export function waysToPlay(entries: Entry[], os: string) {
  const best = [...entries].sort(byPlayerFeedback);
  const [first, second] = best;
  const top = first && reportCount(first) >= ENOUGH_REPORTS && (!second || feedbackScore(first) > feedbackScore(second)) ? first.id : undefined;
  return {
    here: best.filter((e) => availableOn(e, os)),
    elsewhere: best.filter((e) => !availableOn(e, os)),
    top,
  };
}

/** A game's apps, kept for the session: the pill shows at once on an app's page opened again. */
const seen = new Map<string, GameDetail | null>();

/** The apps that play `slug`'s game, once they've come. */
function useGameApps(slug: string | undefined) {
  const { client, remember } = useLauncher();
  const [data, setData] = useState(() => (slug ? seen.get(slug) : undefined));
  useEffect(() => {
    if (!slug) return setData(undefined);
    if (seen.has(slug)) return setData(seen.get(slug));
    setData(undefined);
    let live = true;
    client.game(slug).then(
      (d) => {
        seen.set(slug, d);
        if (d) remember(d.entries);
        if (live) setData(d);
      },
      () => {},
    );
    return () => {
      live = false;
    };
  }, [client, slug]); // eslint-disable-line react-hooks/exhaustive-deps
  return data;
}

/** How a share of "runs well" looks: the colours the feedback summary uses. */
const tone = (share: number) => (share >= 80 ? "positive" : share >= 50 ? "caution" : "negative");

function Way({ entry, current, top, installed, onOpen }: { entry: Entry; current: boolean; top: boolean; installed: boolean; onOpen: () => void }) {
  const total = reportCount(entry);
  const share = runsWellShare(entry);
  return (
    <div className="port-row way-row" data-slug={entry.slug} data-current={current || undefined} data-top={top || undefined}>
      <button type="button" className="port-open" aria-current={current ? "page" : undefined} onClick={onOpen}>
        <CardArt entry={entry} image="icon" sizes="44px" />
        <span className="port-names">
          <span className="way-title">
            <strong>{entry.projectName}</strong>
            {share !== undefined ? (
              <span className={`way-tag ${tone(share)}`}>{t({ message: `${share}% run well`, comment: "The share of players who said an app runs well, on a list of a game's apps" })}</span>
            ) : (
              <span className="way-tag muted">{total ? t`Too few reports` : t`Not rated yet`}</span>
            )}
            {top && <span className="way-tag top">{t({ message: "Top rated", context: "badge", comment: "On the one app players rate best of a game's apps" })}</span>}
            {current && <span className="way-tag here">{t({ message: "You're here", comment: "On the app whose page is open, in a list of the game's apps" })}</span>}
            {installed && <span className="way-tag installed">{t({ message: "Installed", context: "badge", comment: "On an app this computer has installed, in a list of a game's apps" })}</span>}
          </span>
          <small>{[entry.developer?.name, total ? plural(total, { one: "# report", other: "# reports" }) : t`No reports yet`].filter(Boolean).join(" · ")}</small>
        </span>
        <PlatformIcons os={entry.supportedOS} />
      </button>
    </div>
  );
}

/**
 * The pill and its list, for an app based on one game that other apps play too.
 * Nothing for an app of several games (a collection) or none, or a game it alone plays.
 */
export function WaysToPlay({ entry, onOpenApp, onOpenGame }: { entry: Entry; onOpenApp: (entry: Entry) => void; onOpenGame: (game: { slug: string; title: string }) => void }) {
  const { config, installs } = useLauncher();
  const game = entry.games.length === 1 ? entry.games[0] : undefined;
  const data = useGameApps(game?.slug);
  const entries = data?.entries ?? [];
  if (!game || entries.length < 2) return null;
  const { here, elsewhere, top } = waysToPlay(entries, config.os);
  const apps = entries.length;
  const title = game.title;
  const way = (close: () => void) => (e: Entry) => (
    <Way key={e.id} entry={e} current={e.id === entry.id} top={e.id === top} installed={Boolean(installs[e.id])} onOpen={() => (close(), e.id !== entry.id && onOpenApp(e))} />
  );
  return (
    <Popover
      label={plural(apps, { one: "# way to play", other: "# ways to play" })}
      panelLabel={t`Ways to play ${title}`}
      role="group"
      className="ways-pill"
      panel="port-panel ways-panel"
      icon={
        <>
          <Layers size={14} aria-hidden />
          {plural(apps, { one: "# way to play", other: "# ways to play" })}
          <ChevronDown size={14} aria-hidden className="ways-chevron" />
        </>
      }
    >
      {(close) => (
        <div className="port-list">
          <div className="port-head">
            <strong>{t`Ways to play ${title}`}</strong>
            <span className="muted">{t`Best first, by player feedback`}</span>
          </div>
          {here.map(way(close))}
          {elsewhere.length > 0 && (
            <div className="ways-group" role="presentation">
              {config.os === "android" ? <Trans>Not for this device</Trans> : <Trans>Not for this computer</Trans>}
            </div>
          )}
          {elsewhere.map(way(close))}
          <div className="ways-foot">
            <span className="muted">{t`"Runs well" is the share of players who said it runs well for them.`}</span>
            <button type="button" className="link-button" onClick={() => (close(), onOpenGame(game))}>
              {t`See the game's page`} →
            </button>
          </div>
        </div>
      )}
    </Popover>
  );
}
