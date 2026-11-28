/**
 * Discussions, as on the website (its src/components/Discussions.tsx and
 * docs/specs/discussions.md): an app's Discussions tab, the Community page
 * (Recent, General and Following), each discussion opening in place there. They read the
 * site's backend live, through the same signed-in Convex client as the
 * library sync (account.tsx).
 */
import { createContext, useCallback, useContext, useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode, type RefObject } from "react";
import { useConvex, useMutation, usePaginatedQuery, useQuery, type ConvexReactClient } from "convex/react";
import { getFunctionName, makeFunctionReference, type FunctionReference } from "convex/server";
import { ConvexError } from "convex/values";
import { ArrowLeft, Bell, ChevronRight, BellOff, Eye, EyeOff, Flag, Lock, LockOpen, MessageSquare, MessagesSquare, Pencil, Pin, PinOff, Search, X } from "lucide-react";
import { msg, plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { MessageDescriptor } from "@lingui/core";
import { i18n } from "@quiverlauncher/i18n";
import { Artwork, SourcedImage, fullDate, heroSources, iconSources, relativeTime } from "@quiverlauncher/ui";
import { useAccount, useLive } from "./account";
import { ErrorBoundary } from "./boundary";
import { isOwn } from "./custom";
import { pageArea } from "./spatial";
import { tvModeOn, useLauncher } from "./store";
import { useWiderThan } from "./wide";

/** The site's limits (its convex/limits.ts). */
export const TITLE_LIMIT = 120;
export const BODY_LIMIT = 4000;
export const COMMENT_LIMIT = 2000;

/** The Community board's one category: help with Quiver Launcher goes to Discord and GitHub Issues. */
export type Category = "general";

// Put in the language when shown, not here, where the module loads before any is picked.
export const CATEGORY_LABELS: Record<Category, MessageDescriptor> = {
  general: msg({ message: "General", comment: "Community board category for anything about ports and games" }),
};
const CATEGORY_HINTS: Record<Category, MessageDescriptor> = {
  general: msg`Ports, games, mods and anything else.`,
};

const DISCORD_URL = "https://discord.gg/5XRThpWHGk";
const ISSUES_URL = "https://github.com/tgeorgiadis/quiver-launcher/issues";

type Author = { userId: string; author: string; avatar?: string; badge?: "developer" | "staff"; developerKey?: string };
type Hidden = { underReview?: boolean; moderatorHidden?: boolean };
export type Summary = Author &
  Hidden & {
    id: string;
    title: string;
    /** `consoles`, `icon` and `capsule` come in lists across apps: Recent, Following and search there. */
    /** `hero` (its banner) comes only on a discussion's own page. */
    entry?: { id: string; name: string; slug?: string; consoles?: string[]; icon?: string; capsule?: string; hero?: string };
    category?: Category;
    comments: number;
    createdAt: number;
    lastActivityAt: number;
    pinned: boolean;
    locked: boolean;
    unseen?: boolean;
  };
export type Thread = Summary & {
  body: string;
  editedAt?: number;
  removed?: boolean;
  mine: boolean;
  following: boolean;
  moderate: "staff" | "developer" | null;
  reported: boolean;
};
export type Comment = Author &
  Hidden & {
    id: string;
    body: string;
    createdAt: number;
    editedAt?: number;
    deleted?: boolean;
    hidden?: boolean;
    mine: boolean;
    reported: boolean;
  };

// The website's functions (its convex/discussions.ts), without its generated API types.
const ref = <T extends "query" | "mutation">(name: string) => makeFunctionReference<T>(name) as unknown as FunctionReference<T>;
export const fns = {
  list: ref<"query">("discussions:list"),
  pinned: ref<"query">("discussions:pinned"),
  count: ref<"query">("discussions:count"),
  search: ref<"query">("discussions:search"),
  get: ref<"query">("discussions:get"),
  comments: ref<"query">("discussions:comments"),
  unseenCount: ref<"query">("discussions:unseenCount"),
  followed: ref<"query">("discussions:followed"),
  recent: ref<"query">("discussions:recent"),
  recentInApps: ref<"query">("discussions:recentInApps"),
  create: ref<"mutation">("discussions:create"),
  edit: ref<"mutation">("discussions:edit"),
  remove: ref<"mutation">("discussions:remove"),
  comment: ref<"mutation">("discussions:comment"),
  editComment: ref<"mutation">("discussions:editComment"),
  removeComment: ref<"mutation">("discussions:removeComment"),
  report: ref<"mutation">("discussions:report"),
  setHidden: ref<"mutation">("discussions:setHidden"),
  setPinned: ref<"mutation">("discussions:setPinned"),
  setLocked: ref<"mutation">("discussions:setLocked"),
  setFollowing: ref<"mutation">("discussions:setFollowing"),
  markRead: ref<"mutation">("discussions:markRead"),
};

/** Where a discussion opens, and signing in, from App.tsx's page stack. */
export type DiscussionNav = {
  openDiscussion: (id: string) => void;
  openCommunity: () => void;
  /** An app's page, at its Discussions: from a discussion's app name. */
  openApp: (entryId: string, slug?: string) => void;
  signIn: () => void;
};
const NavContext = createContext<DiscussionNav>({ openDiscussion: () => {}, openCommunity: () => {}, openApp: () => {}, signIn: () => {} });
export const DiscussionNavProvider = NavContext.Provider;
const useNav = () => useContext(NavContext);

/**
 * A list's discussions open where the list is (ThreadPane): over it in an app's Discussions tab and in a narrow
 * Community, beside it in a wide one. Not under one, as from a followed one elsewhere, Community opens at it.
 */
type ThreadAt = { open: (id: string) => void; current: string | null };
const ThreadPlace = createContext<ThreadAt | null>(null);
function useOpenThread() {
  const place = useContext(ThreadPlace);
  const { openDiscussion } = useNav();
  return { open: place?.open ?? openDiscussion, current: place?.current ?? null };
}

/**
 * The discussion open in place of a list or beside it, if one is, and the way back: where the list was scrolled to and
 * the focus on the row it opened from. `beside`, the list stays as it was, so only the focus goes back.
 */
export function useThread(initial: string | null = null, beside = false) {
  const [id, setId] = useState(initial);
  const from = useRef<{ y: number; id: string } | null>(null);
  const besideNow = useRef(beside);
  useLayoutEffect(() => void (besideNow.current = beside));
  const open = useCallback((next: string) => {
    from.current = { y: pageArea().scrollTop, id: next };
    setId(next);
  }, []);
  const close = useCallback(() => setId(null), []);
  useLayoutEffect(() => {
    const was = from.current;
    if (id !== null || !was) return;
    from.current = null;
    // Braces matter: scrollTo returns a Promise in newer WebView2.
    if (!besideNow.current) {
      pageArea().scrollTo(0, was.y);
    }
    document.querySelector<HTMLElement>(`[data-thread="${CSS.escape(was.id)}"]`)?.focus({ preventScroll: true });
  }, [id]);
  return { id, open, close, show: setId };
}
export type OpenThread = ReturnType<typeof useThread>;

/** Escape, or a controller's Back, calls `onClose` while `box` shows, ahead of the page going back: not from under a dialog or menu. */
function useEscapeCloses(box: RefObject<HTMLElement | null>, onClose: () => void) {
  const close = useRef(onClose);
  useLayoutEffect(() => void (close.current = onClose));
  useEffect(() => {
    const key = (e: KeyboardEvent) => {
      if (e.key !== "Escape" || !box.current?.getClientRects().length || document.querySelector('[role="dialog"], [role="menu"], .menu.floating')) return;
      e.stopImmediatePropagation();
      close.current();
    };
    window.addEventListener("keydown", key, true);
    return () => window.removeEventListener("keydown", key, true);
  }, [box]);
}

/** The site's reason for a refusal ("This discussion is locked"), or `fallback`. */
function reason(error: unknown, fallback: string) {
  return error instanceof ConvexError && typeof error.data === "string" ? error.data : fallback;
}

/**
 * Discussions need the site live: in end-to-end tests there's none, and a
 * site from before discussions answers with an error, so they say so here
 * instead of taking the page down.
 */
function Live({ children }: { children: ReactNode }) {
  if (!useLive())
    return (
      <p className="muted">
        <Trans>Discussions aren't available right now.</Trans>
      </p>
    );
  return (
    <ErrorBoundary
      fallback={() => (
        <p className="muted" role="alert">
          <Trans>Couldn't load discussions. Try again later.</Trans>
        </p>
      )}
    >
      {children}
    </ErrorBoundary>
  );
}

/** Text with its web addresses made into links (main.tsx opens them in the browser). */
export function Linkified({ text }: { text: string }) {
  const parts = text.split(/(https?:\/\/[^\s<>"]+[^\s<>".,;:!?)\]])/g);
  return <>{parts.map((part, i) => (i % 2 ? <a key={i} href={part} rel="noopener noreferrer nofollow ugc">{part}</a> : part))}</>;
}

function Avatar({ author }: { author: Author }) {
  return author.avatar ? <img className="avatar" src={author.avatar} alt="" /> : <span className="avatar" aria-hidden="true">{author.author.slice(0, 1).toUpperCase()}</span>;
}

function Name({ author }: { author: Author }) {
  const { user } = useAccount();
  return (
    <span className="discussion-name">
      <strong>{author.author}</strong>
      {author.badge === "developer" && (
        <span className="developer-tag">
          <Trans comment="Tag after the name of an app's developer">Developer</Trans>
        </span>
      )}
      {author.badge === "staff" && (
        <span className="developer-tag">
          <Trans comment="Tag after the name of someone who runs Quiver">Quiver staff</Trans>
        </span>
      )}
      {user?.id === author.userId && (
        <span className="you-tag">
          <Trans comment="Tag on the viewer's own post">You</Trans>
        </span>
      )}
    </span>
  );
}

function HiddenNote({ post }: { post: Hidden }) {
  if (!post.underReview && !post.moderatorHidden) return null;
  return (
    <p className="discussion-hidden" role="note">
      <EyeOff size={16} aria-hidden="true" />{" "}
      {post.moderatorHidden ? t`Quiver staff hid this, so others can't see it.` : t`This is waiting for Quiver staff to review it before others can see it.`}
    </p>
  );
}

function Flags({ thread }: { thread: Summary }) {
  return (
    <>
      {thread.unseen && (
        <span className="discussion-flag new">
          <Trans comment="A followed discussion has comments the viewer hasn't seen">New comments</Trans>
        </span>
      )}
      {thread.pinned && (
        <span className="discussion-flag">
          <Pin size={12} aria-hidden="true" /> <Trans comment="A discussion kept at the top of the list">Pinned</Trans>
        </span>
      )}
      {thread.locked && (
        <span className="discussion-flag">
          <Lock size={12} aria-hidden="true" /> <Trans comment="A discussion that takes no new comments">Locked</Trans>
        </span>
      )}
    </>
  );
}

function Row({ thread }: { thread: Summary }) {
  const { open, current } = useOpenThread();
  const { thread: threadAhead } = useDiscussionsAhead();
  const active = relativeTime(thread.lastActivityAt);
  return (
    <li className="discussion-row">
      <button
        type="button"
        className="discussion-row-link"
        data-thread={thread.id}
        aria-current={current === thread.id || undefined}
        onClick={() => open(thread.id)}
        onPointerEnter={() => threadAhead(thread.id)}
        onFocus={() => threadAhead(thread.id)}
      >
        <span className="discussion-row-title">
          <Flags thread={thread} />
          {thread.title}
        </span>
        <span className="discussion-row-meta">
          {[
            thread.author,
            plural(thread.comments, { one: "# comment", other: "# comments" }),
            t({ message: `active ${active}`, comment: "When a discussion last had a comment, as in “active 2 days ago”" }),
          ]
            .filter(Boolean)
            .join(" · ")}
        </span>
      </button>
    </li>
  );
}

/**
 * An app's Discussions tab: its list, or a discussion open in its place, Back going back to the list. The list stays
 * under it, hidden, so it comes back as it was. `detailsBeside`, the page's column has the discussion's facts and
 * following it (ThreadDetails).
 */
export function DiscussionsTab({ entryId, thread, detailsBeside }: { entryId: string; thread: OpenThread; detailsBeside: boolean }) {
  const place = useMemo(() => ({ open: thread.open, current: thread.id }), [thread.open, thread.id]);
  return (
    <section className="discussions" aria-label={t`Discussions`}>
      <Live>
        <ThreadPlace.Provider value={place}>
          <div className="discussion-board" hidden={!!thread.id}>
            <Board entryId={entryId} />
          </div>
          {thread.id && <ThreadPane key={thread.id} id={thread.id} back={t`All discussions`} onClose={thread.close} inApp detailsBeside={detailsBeside} />}
        </ThreadPlace.Provider>
      </Live>
    </section>
  );
}

/**
 * A discussion open where its list is: in place of it, with Back to it at its top, or `beside` it (a wide Community),
 * with its own scroll and a close button. Escape or a controller's Back closes it, ahead of the page going back.
 */
function ThreadPane({ id, back, onClose, beside, inApp, detailsBeside }: { id: string; back: string; onClose: () => void; beside?: boolean; inApp?: boolean; detailsBeside?: boolean }) {
  const pane = useRef<HTMLDivElement>(null);
  const first = useRef<HTMLButtonElement>(null);
  const room = useRoom(pane, !!beside);
  useEscapeCloses(pane, onClose);
  // The focus to it, to read on from with the d-pad, and its top into sight when the list it took the place of was scrolled past it.
  useLayoutEffect(() => {
    first.current?.focus({ preventScroll: true });
    const el = pane.current;
    const view = el?.closest("main");
    if (el && view && !beside && el.getBoundingClientRect().top < view.getBoundingClientRect().top) el.scrollIntoView({ block: "start" });
  }, [beside]);
  return (
    <div ref={pane} className="thread-pane" data-beside={beside || undefined} style={beside && room ? { maxHeight: room } : undefined}>
      {beside ? (
        <button ref={first} type="button" className="icon thread-close" aria-label={t`Close discussion`} title={t`Close discussion`} onClick={onClose}>
          <X size={18} aria-hidden="true" />
        </button>
      ) : (
        <button ref={first} type="button" className="back-link" onClick={onClose}>
          <ArrowLeft size={15} aria-hidden="true" /> {back}
        </button>
      )}
      <Live>
        <ThreadLoader id={id} inApp={inApp} detailsBeside={detailsBeside} gone={{ label: back, onClose }} />
      </Live>
    </div>
  );
}

/** How tall `box` can be and stay whole in the window as the page scrolls (it's sticky), when `on`. */
function useRoom(box: RefObject<HTMLElement | null>, on: boolean) {
  const [room, setRoom] = useState<number>();
  useLayoutEffect(() => {
    const view = box.current?.closest("main");
    if (!on || !view) return;
    const measure = () => setRoom(view.clientHeight - 32);
    measure();
    if (typeof ResizeObserver === "undefined") return;
    const sized = new ResizeObserver(measure);
    sized.observe(view);
    return () => sized.disconnect();
  }, [box, on]);
  return on ? room : undefined;
}

/** A discussion's facts, in the column beside it on a wide app page: who started it and when, its comments, its last activity, and following or reporting it. */
export function ThreadDetails({ id }: { id: string }) {
  if (!useLive()) return null;
  return (
    <ErrorBoundary fallback={() => null}>
      <Details id={id} />
    </ErrorBoundary>
  );
}

function Details({ id }: { id: string }) {
  const { user } = useAccount();
  const thread = useQuery(fns.get, { id }) as Thread | null | undefined;
  const setFollowing = useMutation(fns.setFollowing);
  if (!thread) return null;
  return (
    <section className="panel content-panel thread-details" aria-label={t`About this discussion`}>
      <h2 className="eyebrow">
        <Trans comment="Heading over a discussion's facts, in the column beside it">DISCUSSION</Trans>
      </h2>
      <dl>
        <div>
          <dt>
            <Trans comment="Who started a discussion">Started by</Trans>
          </dt>
          <dd>{thread.author}</dd>
        </div>
        <div>
          <dt>
            <Trans comment="When a discussion was started">Started</Trans>
          </dt>
          <dd title={fullDate(thread.createdAt)}>{relativeTime(thread.createdAt)}</dd>
        </div>
        <div>
          <dt>
            <Trans comment="How many comments a discussion has">Comments</Trans>
          </dt>
          <dd>{thread.comments.toLocaleString(i18n.locale)}</dd>
        </div>
        <div>
          <dt>
            <Trans comment="When a discussion last had a comment">Last active</Trans>
          </dt>
          <dd title={fullDate(thread.lastActivityAt)}>{relativeTime(thread.lastActivityAt)}</dd>
        </div>
      </dl>
      {user && (
        <div className="discussion-actions">
          <FollowAction thread={thread} onToggle={() => setFollowing({ id: thread.id, following: !thread.following })} />
          {!thread.mine && <ReportControl target={{ discussionId: thread.id }} reported={thread.reported} />}
        </div>
      )}
      {user && (
        <p className="muted thread-details-note">
          {thread.following ? t`You'll see its new comments in Community's Following.` : t`Follow it to see its new comments in Community's Following.`}
        </p>
      )}
    </section>
  );
}

function FollowAction({ thread, onToggle }: { thread: Thread; onToggle: () => Promise<unknown> }) {
  return (
    <Action onClick={onToggle}>
      {thread.following ? (
        <>
          <BellOff size={14} aria-hidden="true" /> <Trans comment="Stops new comments showing on the account page">Unfollow</Trans>
        </>
      ) : (
        <>
          <Bell size={14} aria-hidden="true" /> <Trans comment="Shows new comments on the account page">Follow</Trans>
        </>
      )}
    </Action>
  );
}

/** How many discussions an app has, for its tab; undefined until known, or without the site live. */
export function useDiscussionCount(entryId: string, on: boolean) {
  const live = useLive();
  return useSafeQuery<number>(fns.count, live && on ? { entryId } : "skip");
}

/** Queries asked ahead (useDiscussionsAhead), each watched a minute, the newest few. */
const ahead = new Map<string, { stop: () => void; timer: number }>();
const AHEAD_MS = 60_000;
const AHEAD_MAX = 12;

/** A list's first page, as a paginated query asks for it (useFirstPage reads it). */
const firstPage = (numItems: number) => ({ numItems, cursor: null });

/** Watches `query` for a minute, or a minute more if it's watched already. */
function watchAhead(client: ConvexReactClient, query: FunctionReference<"query">, args: Record<string, unknown>) {
  const key = `${getFunctionName(query)}|${JSON.stringify(args)}`;
  const had = ahead.get(key);
  let stop = had?.stop;
  if (had) {
    window.clearTimeout(had.timer);
    ahead.delete(key);
  } else {
    try {
      stop = client.watchQuery(query, args).onUpdate(() => {});
    } catch {
      return;
    }
  }
  const timer = window.setTimeout(() => {
    ahead.get(key)?.stop();
    ahead.delete(key);
  }, AHEAD_MS);
  ahead.set(key, { stop: stop!, timer });
  for (const [k, kept] of ahead) {
    if (ahead.size <= AHEAD_MAX) break;
    window.clearTimeout(kept.timer);
    kept.stop();
    ahead.delete(k);
  }
}

/**
 * What the community asks the site for, asked ahead as the pointer or the focus comes to what shows it: an app's
 * discussion count (its card), its Discussions tab, a discussion (its row), Community (its button). Each is watched a
 * minute, so the page or tab, opened meanwhile, shows it as it opens rather than a moment after. Nothing without the
 * site live.
 */
export function useDiscussionsAhead() {
  const client = useConvex();
  const live = useLive();
  return useMemo(() => {
    const watch = (query: FunctionReference<"query">, args: Record<string, unknown>) => live && client && watchAhead(client, query, args);
    return {
      count: (entryId: string) => watch(fns.count, { entryId }),
      board: (entryId: string) => {
        watch(fns.pinned, { entryId });
        watch(fns.list, { entryId, paginationOpts: firstPage(BOARD_PAGE) });
      },
      thread: (id: string) => {
        watch(fns.get, { id });
        watch(fns.comments, { discussionId: id, paginationOpts: firstPage(COMMENTS_PAGE) });
      },
      community: () => watch(fns.recent, { order: "activity", paginationOpts: firstPage(RECENT_PAGE) }),
    };
  }, [client, live]);
}

const BOARD_PAGE = 20;
const COMMENTS_PAGE = 30;
const RECENT_PAGE = 20;

type Paged = { results: unknown[]; status: string; loadMore: (n: number) => void };
/**
 * A paginated list, with the first page one asked ahead has (useDiscussionsAhead) while its own is on its way: each
 * paginated query asks with an id of its own, so the one asked ahead isn't the one it waits for.
 */
function useFirstPage<T extends Paged>(query: FunctionReference<"query">, args: Record<string, unknown> | "skip", numItems: number, list: T): T {
  const client = useConvex();
  if (args === "skip" || list.status !== "LoadingFirstPage" || !client) return list;
  let kept: { page: unknown[]; isDone: boolean } | undefined;
  try {
    kept = client.watchQuery(query, { ...args, paginationOpts: firstPage(numItems) }).localQueryResult() as typeof kept;
  } catch {
    return list;
  }
  return kept ? { ...list, results: kept.page, status: kept.isDone ? "Exhausted" : "CanLoadMore" } : list;
}

/** A query that answers undefined, not an error, when the site doesn't have it yet. */
function useSafeQuery<T>(query: FunctionReference<"query">, args: Record<string, unknown> | "skip") {
  try {
    // eslint-disable-next-line react-hooks/rules-of-hooks
    return useQuery(query, args) as T | undefined;
  } catch {
    return undefined;
  }
}

function Board({ entryId, category, search = "" }: { entryId?: string; category?: Category; search?: string }) {
  const { user } = useAccount();
  const { signIn } = useNav();
  const [composing, setComposing] = useState(false);
  const where = entryId ? { entryId } : { category };
  const pinned = useQuery(fns.pinned, where) as Summary[] | undefined;
  const list = useFirstPage(fns.list, where, BOARD_PAGE, usePaginatedQuery(fns.list, where, { initialNumItems: BOARD_PAGE }));
  const results = list.results as Summary[];
  const text = search.trim();
  const found = useQuery(fns.search, text ? { ...where, text } : "skip") as Summary[] | undefined;
  const empty = list.status === "Exhausted" && results.length === 0 && (pinned?.length ?? 0) === 0;
  const start = user ? (
    <button type="button" className="button" onClick={() => setComposing(true)}>
      <MessageSquare size={16} aria-hidden="true" /> <Trans>Start a discussion</Trans>
    </button>
  ) : (
    <button type="button" className="button secondary" onClick={signIn}>
      <Trans>Sign in</Trans>
    </button>
  );
  return (
    <>
      {composing && user ? (
        <Composer entryId={entryId} category={category} onClose={() => setComposing(false)} />
      ) : empty && !text ? (
        <div className="discussion-empty panel">
          <MessagesSquare size={28} aria-hidden="true" />
          <p>{t`No discussions yet. Ask a question or share a tip.`}</p>
          {start}
        </div>
      ) : (
        <div className="discussions-head">
          <p className="muted">
            {!user
              ? t`Sign in to start a discussion or comment.`
              : entryId
                ? t`Ask a question, share a tip or talk about this app. For how it ran, use Player feedback.`
                : category && i18n._(CATEGORY_HINTS[category])}
          </p>
          {start}
        </div>
      )}
      {text ? (
        found === undefined ? (
          <Spinner label={t`Searching…`} />
        ) : found.length ? (
          <ul className="discussion-list panel">
            {found.map((thread) => (
              <Row key={thread.id} thread={thread} />
            ))}
          </ul>
        ) : (
          <p className="muted">
            <Trans>No discussion titles match “{text}”.</Trans>
          </p>
        )
      ) : list.status === "LoadingFirstPage" ? (
        <Spinner label={t`Loading discussions…`} />
      ) : (
        !empty && (
          <ul className="discussion-list panel">
            {(pinned ?? []).map((thread) => (
              <Row key={thread.id} thread={thread} />
            ))}
            {results.map((thread) => (
              <Row key={thread.id} thread={thread} />
            ))}
          </ul>
        )
      )}
      {!text && list.status === "CanLoadMore" && (
        <button type="button" className="button secondary" onClick={() => list.loadMore(20)}>
          <Trans>Load more</Trans>
        </button>
      )}
    </>
  );
}

function Spinner({ label }: { label: string }) {
  return (
    <div className="more" role="status" aria-label={label}>
      <span className="spinner" />
    </div>
  );
}

/** Starts a discussion, or edits one (`editing`). */
function Composer({
  entryId,
  category,
  editing,
  onClose,
}: {
  entryId?: string;
  category?: Category;
  editing?: { id: string; title: string; body: string };
  onClose: () => void;
}) {
  const create = useMutation(fns.create);
  const edit = useMutation(fns.edit);
  const { open: openThread } = useOpenThread();
  const [title, setTitle] = useState(editing?.title ?? "");
  const [body, setBody] = useState(editing?.body ?? "");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  return (
    <form
      className="discussion-composer panel"
      onSubmit={async (e) => {
        e.preventDefault();
        setBusy(true);
        setMessage("");
        try {
          if (editing) {
            await edit({ id: editing.id, title, body });
            onClose();
          } else {
            const id = (await create({ ...(entryId ? { entryId } : { category }), title, body })) as string;
            onClose();
            openThread(id);
          }
        } catch (error) {
          setMessage(reason(error, t`Couldn't post that. Try again.`));
        } finally {
          setBusy(false);
        }
      }}
    >
      <h3>{editing ? <Trans>Edit your discussion</Trans> : <Trans>Start a discussion</Trans>}</h3>
      <label className="field">
        <Trans comment="Form field: a discussion's title">Title</Trans>
        <input value={title} required minLength={3} maxLength={TITLE_LIMIT} onChange={(e) => setTitle(e.target.value)} autoFocus />
      </label>
      <label className="field">
        <Trans comment="Form field: the opening post of a discussion">Post</Trans>
        <textarea
          value={body}
          required
          rows={6}
          maxLength={BODY_LIMIT}
          placeholder={t`What would you like to ask or share? Say which version and platform if it's about a problem.`}
          onChange={(e) => setBody(e.target.value)}
        />
      </label>
      <Count length={body.length} limit={BODY_LIMIT} />
      <div className="discussion-form-actions">
        <button className="button" disabled={busy}>
          {busy ? (
            <Trans comment="Button label while a post is being saved">Posting…</Trans>
          ) : editing ? (
            <Trans>Save changes</Trans>
          ) : (
            <Trans comment="Button that posts a new discussion">Post</Trans>
          )}
        </button>
        <button type="button" className="button secondary" disabled={busy} onClick={onClose}>
          <Trans>Cancel</Trans>
        </button>
      </div>
      {message && <p role="alert">{message}</p>}
    </form>
  );
}

function Count({ length, limit }: { length: number; limit: number }) {
  const max = limit.toLocaleString(i18n.locale);
  return (
    <div className={`discussion-count${length >= limit ? " full" : ""}`}>
      <span aria-live="polite">{length >= limit ? t`You've reached the ${max}-character limit.` : ""}</span>
      <span>
        {length}/{limit}
      </span>
    </div>
  );
}

const REPORT_REASONS = {
  spam: msg({ message: "Spam", comment: "Reason for reporting a post" }),
  abuse: msg({ message: "Abuse", comment: "Reason for reporting a post" }),
  piracy: msg({ message: "Piracy or malware", comment: "Reason for reporting a post" }),
  other: msg({ message: "Something else", comment: "Reason for reporting a post" }),
} as const;
type ReportReason = keyof typeof REPORT_REASONS;
type Target = { discussionId: string } | { commentId: string };

function ReportControl({ target, reported }: { target: Target; reported: boolean }) {
  const report = useMutation(fns.report);
  const [open, setOpen] = useState(false);
  const [why, setWhy] = useState<ReportReason>("spam");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  if (reported)
    return (
      <span className="muted discussion-reported">
        <Flag size={14} aria-hidden="true" /> <Trans comment="The viewer already reported this post">Reported</Trans>
      </span>
    );
  if (!open)
    return (
      <button type="button" className="text-button" onClick={() => setOpen(true)}>
        <Flag size={14} aria-hidden="true" /> <Trans comment="Button that reports a post to Quiver staff">Report</Trans>
      </button>
    );
  return (
    <span className="discussion-report" role="group">
      <label>
        <Trans>Why are you reporting this?</Trans>{" "}
        <select value={why} onChange={(e) => setWhy(e.target.value as ReportReason)}>
          {(Object.keys(REPORT_REASONS) as ReportReason[]).map((key) => (
            <option key={key} value={key}>
              {i18n._(REPORT_REASONS[key])}
            </option>
          ))}
        </select>
      </label>
      <button
        type="button"
        className="button small secondary"
        disabled={busy}
        onClick={async () => {
          setBusy(true);
          setError("");
          try {
            await report({ ...target, reason: why });
            setOpen(false);
          } catch (e) {
            setError(reason(e, t`Couldn't send that. Try again.`));
          } finally {
            setBusy(false);
          }
        }}
      >
        <Trans comment="Button that sends a report">Report</Trans>
      </button>
      <button type="button" className="text-button" disabled={busy} onClick={() => setOpen(false)}>
        <Trans>Cancel</Trans>
      </button>
      {error && <span role="alert">{error}</span>}
    </span>
  );
}

function Action({ onClick, children }: { onClick: () => Promise<unknown>; children: ReactNode }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  return (
    <>
      <button
        type="button"
        className="text-button"
        disabled={busy}
        onClick={async () => {
          setBusy(true);
          setError("");
          try {
            await onClick();
          } catch (e) {
            setError(reason(e, t`That didn't work. Try again.`));
          } finally {
            setBusy(false);
          }
        }}
      >
        {children}
      </button>
      {error && (
        <span className="job-error" role="alert">
          {error}
        </span>
      )}
    </>
  );
}

function HideAction({ target, hidden }: { target: Target; hidden: boolean }) {
  const setHidden = useMutation(fns.setHidden);
  return (
    <Action onClick={() => setHidden({ ...target, hidden: !hidden })}>
      {hidden ? (
        <>
          <Eye size={14} aria-hidden="true" /> <Trans comment="Makes a hidden post visible again">Show</Trans>
        </>
      ) : (
        <>
          <EyeOff size={14} aria-hidden="true" /> <Trans comment="Hides a post from everyone">Hide</Trans>
        </>
      )}
    </Action>
  );
}

/** "Delete", then the question and "Yes, delete", as the website's ConfirmDelete. */
function Delete({ prompt, onConfirm }: { prompt: string; onConfirm: () => Promise<void> }) {
  const [asking, setAsking] = useState(false);
  const [busy, setBusy] = useState(false);
  if (!asking)
    return (
      <button type="button" className="text-button danger" onClick={() => setAsking(true)}>
        <Trans>Delete</Trans>
      </button>
    );
  return (
    <span className="discussion-report" role="group" aria-label={prompt}>
      <span>{prompt}</span>
      <button
        type="button"
        className="button small secondary danger"
        disabled={busy}
        onClick={async () => {
          setBusy(true);
          try {
            await onConfirm();
          } finally {
            setBusy(false);
            setAsking(false);
          }
        }}
      >
        {busy ? t`Deleting…` : t`Yes, delete`}
      </button>
      <button type="button" className="text-button" disabled={busy} onClick={() => setAsking(false)} autoFocus>
        <Trans>Cancel</Trans>
      </button>
    </span>
  );
}

function When({ at, edited }: { at: number; edited?: number }) {
  return (
    <small>
      <span title={fullDate(at)}>{relativeTime(at)}</span>
      {edited && (
        <span title={fullDate(edited)}>
          {" · "}
          <Trans comment="After a post's date: its author changed it">edited</Trans>
        </span>
      )}
    </small>
  );
}

/** A discussion: its post, comments and the comment box. Gone (deleted or hidden), the way back to its list. */
function ThreadLoader({ id, inApp, detailsBeside, gone }: { id: string; inApp?: boolean; detailsBeside?: boolean; gone: { label: string; onClose: () => void } }) {
  const { user } = useAccount();
  const thread = useQuery(fns.get, { id }) as Thread | null | undefined;
  const markRead = useMutation(fns.markRead);
  const comments = thread?.comments;
  useEffect(() => {
    if (user && thread?.following) void markRead({ id }).catch(() => {});
  }, [user, id, thread?.following, comments, markRead]);
  if (thread === undefined) return <Spinner label={t`Loading discussion…`} />;
  if (thread === null)
    return (
      <div className="empty panel">
        <h2>
          <Trans>This discussion isn't available</Trans>
        </h2>
        <p>
          <Trans>It may have been deleted or hidden.</Trans>
        </p>
        <button type="button" className="button secondary" onClick={gone.onClose}>
          {gone.label}
        </button>
      </div>
    );
  return <ThreadView thread={thread} inApp={inApp} detailsBeside={detailsBeside} />;
}

/**
 * A discussion's post and comments. `inApp`, on its app's page, without the app over it; `detailsBeside`, following
 * and reporting it are in the column beside it (ThreadDetails), not under its post.
 */
function ThreadView({ thread, inApp, detailsBeside }: { thread: Thread; inApp?: boolean; detailsBeside?: boolean }) {
  const { user } = useAccount();
  const [editing, setEditing] = useState(false);
  const remove = useMutation(fns.remove);
  const setPinned = useMutation(fns.setPinned);
  const setLocked = useMutation(fns.setLocked);
  const setFollowing = useMutation(fns.setFollowing);
  const [error, setError] = useState("");
  const { openApp } = useNav();
  const entry = thread.entry;
  const place = entry?.name ?? (thread.category ? i18n._(CATEGORY_LABELS[thread.category]) : t`Community`);
  const hidden = !!(thread.underReview || thread.moderatorHidden);
  return (
    <article className="discussion-thread">
      {inApp ? null : entry ? <DiscussionApp entry={entry} onOpen={() => openApp(entry.id, entry.slug)} /> : <p className="eyebrow">{place}</p>}
      <h2 className="discussion-thread-title">{thread.title}</h2>
      {editing ? (
        <Composer editing={{ id: thread.id, title: thread.title, body: thread.body }} onClose={() => setEditing(false)} />
      ) : (
        <div className="discussion-post panel">
          <header className="discussion-post-head">
            <Avatar author={thread} />
            <div>
              <Name author={thread} />
              <When at={thread.createdAt} edited={thread.editedAt} />
            </div>
            <Flags thread={{ ...thread, locked: false }} />
          </header>
          <HiddenNote post={thread} />
          {thread.removed ? (
            <p className="muted discussion-deleted">
              <Trans comment="In place of a post its author deleted">Deleted</Trans>
            </p>
          ) : (
            <p className="discussion-body">
              <Linkified text={thread.body} />
            </p>
          )}
          <div className="discussion-actions">
            {user && !detailsBeside && <FollowAction thread={thread} onToggle={() => setFollowing({ id: thread.id, following: !thread.following })} />}
            {thread.mine && !thread.removed && !thread.locked && (
              <button type="button" className="text-button" onClick={() => setEditing(true)}>
                <Pencil size={14} aria-hidden="true" /> <Trans>Edit</Trans>
              </button>
            )}
            {thread.mine && !thread.removed && (
              <Delete
                prompt={t`Delete your discussion? This can't be undone.`}
                onConfirm={async () => {
                  try {
                    await remove({ id: thread.id });
                  } catch (e) {
                    setError(reason(e, t`Couldn't delete it. Try again.`));
                  }
                }}
              />
            )}
            {user && !thread.mine && !detailsBeside && <ReportControl target={{ discussionId: thread.id }} reported={thread.reported} />}
            {thread.moderate && (
              <>
                <Action onClick={() => setPinned({ id: thread.id, pinned: !thread.pinned })}>
                  {thread.pinned ? (
                    <>
                      <PinOff size={14} aria-hidden="true" /> <Trans comment="Moderation action">Unpin</Trans>
                    </>
                  ) : (
                    <>
                      <Pin size={14} aria-hidden="true" /> <Trans comment="Moderation action">Pin</Trans>
                    </>
                  )}
                </Action>
                <Action onClick={() => setLocked({ id: thread.id, locked: !thread.locked })}>
                  {thread.locked ? (
                    <>
                      <LockOpen size={14} aria-hidden="true" /> <Trans comment="Moderation action">Unlock</Trans>
                    </>
                  ) : (
                    <>
                      <Lock size={14} aria-hidden="true" /> <Trans comment="Moderation action">Lock</Trans>
                    </>
                  )}
                </Action>
                {(thread.moderate === "staff" || !hidden) && <HideAction target={{ discussionId: thread.id }} hidden={hidden} />}
              </>
            )}
          </div>
          {error && (
            <p className="job-error" role="alert">
              {error}
            </p>
          )}
        </div>
      )}
      <Comments thread={thread} />
    </article>
  );
}

function Comments({ thread }: { thread: Thread }) {
  const { user } = useAccount();
  const { signIn } = useNav();
  const list = useFirstPage(fns.comments, { discussionId: thread.id }, COMMENTS_PAGE, usePaginatedQuery(fns.comments, { discussionId: thread.id }, { initialNumItems: COMMENTS_PAGE }));
  const results = list.results as Comment[];
  return (
    <section className="discussion-comments" aria-label={t`Comments`}>
      <h3>{plural(thread.comments, { one: "# comment", other: "# comments" })}</h3>
      {list.status === "LoadingFirstPage" ? (
        <Spinner label={t`Loading comments…`} />
      ) : (
        results.map((c) => <CommentView key={c.id} comment={c} thread={thread} />)
      )}
      {list.status === "CanLoadMore" && (
        <button type="button" className="button secondary" onClick={() => list.loadMore(30)}>
          <Trans>Load more</Trans>
        </button>
      )}
      {thread.locked && !thread.moderate ? (
        <p className="discussion-locked muted" role="note">
          <Lock size={14} aria-hidden="true" /> <Trans>This discussion is locked, so it takes no new comments.</Trans>
        </p>
      ) : !user ? (
        <p className="muted">
          <button type="button" className="link-button" onClick={signIn}>
            <Trans>Sign in to start a discussion or comment.</Trans>
          </button>
        </p>
      ) : (
        !(thread.underReview || thread.moderatorHidden) && <CommentBox discussionId={thread.id} />
      )}
    </section>
  );
}

function CommentView({ comment, thread }: { comment: Comment; thread: Thread }) {
  const { user } = useAccount();
  const [editing, setEditing] = useState(false);
  const remove = useMutation(fns.removeComment);
  const [error, setError] = useState("");
  if (comment.deleted)
    return (
      <div className="discussion-comment deleted">
        <p className="muted">
          <Trans comment="In place of a comment its author deleted">Deleted</Trans>
        </p>
      </div>
    );
  return (
    // A stop of its own, for a controller's d-pad (and Tab) to read down the list.
    <div className={`discussion-comment${comment.hidden ? " is-hidden" : ""}`} tabIndex={0}>
      <header className="discussion-post-head">
        <Avatar author={comment} />
        <div>
          <Name author={comment} />
          <When at={comment.createdAt} edited={comment.editedAt} />
        </div>
      </header>
      <HiddenNote post={comment} />
      {editing ? (
        <CommentBox editing={{ id: comment.id, body: comment.body }} onDone={() => setEditing(false)} />
      ) : (
        <p className="discussion-body">
          <Linkified text={comment.body} />
        </p>
      )}
      {!editing && (
        <div className="discussion-actions">
          {comment.mine && !thread.locked && !comment.moderatorHidden && (
            <button type="button" className="text-button" onClick={() => setEditing(true)}>
              <Pencil size={14} aria-hidden="true" /> <Trans>Edit</Trans>
            </button>
          )}
          {comment.mine && (
            <Delete
              prompt={t`Delete your comment? This can't be undone.`}
              onConfirm={async () => {
                try {
                  await remove({ id: comment.id });
                } catch (e) {
                  setError(reason(e, t`Couldn't delete it. Try again.`));
                }
              }}
            />
          )}
          {user && !comment.mine && <ReportControl target={{ commentId: comment.id }} reported={comment.reported} />}
          {thread.moderate && (thread.moderate === "staff" || !comment.hidden) && <HideAction target={{ commentId: comment.id }} hidden={!!comment.hidden} />}
        </div>
      )}
      {error && (
        <p className="job-error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

function CommentBox({ discussionId, editing, onDone }: { discussionId?: string; editing?: { id: string; body: string }; onDone?: () => void }) {
  const add = useMutation(fns.comment);
  const edit = useMutation(fns.editComment);
  const [body, setBody] = useState(editing?.body ?? "");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  return (
    <form
      className="discussion-comment-box"
      onSubmit={async (e) => {
        e.preventDefault();
        setBusy(true);
        setMessage("");
        try {
          if (editing) {
            await edit({ id: editing.id, body });
            onDone?.();
          } else if (discussionId) {
            await add({ discussionId, body });
            setBody("");
          }
        } catch (error) {
          setMessage(reason(error, t`Couldn't post that. Try again.`));
        } finally {
          setBusy(false);
        }
      }}
    >
      <label className="field">
        {editing ? <Trans>Edit your comment</Trans> : <Trans>Add a comment</Trans>}
        <textarea value={body} required rows={editing ? 4 : 3} maxLength={COMMENT_LIMIT} onChange={(e) => setBody(e.target.value)} />
      </label>
      <Count length={body.length} limit={COMMENT_LIMIT} />
      <div className="discussion-form-actions">
        <button className="button" disabled={busy || !body.trim()}>
          {busy ? (
            <Trans comment="Button label while a post is being saved">Posting…</Trans>
          ) : editing ? (
            <Trans>Save changes</Trans>
          ) : (
            <Trans comment="Button that posts a comment">Comment</Trans>
          )}
        </button>
        {editing && (
          <button type="button" className="button secondary" disabled={busy} onClick={onDone}>
            <Trans>Cancel</Trans>
          </button>
        )}
      </div>
      {message && <p role="alert">{message}</p>}
    </form>
  );
}

type Place = "recent" | "general" | "following";
/** What Community's Recent tab shows, as on the site: everything, the library's apps, or by when they started. */
type RecentFilter = "all" | "mine" | "new";

/** How wide Community is (not the window) before a discussion opens beside its list rather than in place of it. */
const SPLIT_FROM = 1080;

/**
 * The Community page: Recent across every app and General, General itself, and the discussions the player follows.
 * A discussion opens beside the list, wide enough, or in place of it, narrower and in TV mode; `asked`, a link to one
 * from elsewhere, opens at it.
 */
export function CommunityPage({ asked, back, onBack }: { asked: { thread?: string }; back: string; onBack: () => void }) {
  const { user } = useAccount();
  const live = useLive();
  const { settings, config } = useLauncher();
  const page = useRef<HTMLElement>(null);
  // Without the site live there's nothing to open, so no room kept for it.
  const beside = useWiderThan(page, SPLIT_FROM) && !tvModeOn(settings, config) && live;
  const thread = useThread(asked.thread ?? null, beside);
  const { show } = thread;
  // Each link again, with the page open already: a new one each time (App.tsx's open).
  const firstAsk = useRef(asked);
  useEffect(() => {
    if (asked !== firstAsk.current && asked.thread) show(asked.thread);
  }, [asked, show]);
  const at = useMemo(() => ({ open: thread.open, current: thread.id }), [thread.open, thread.id]);
  // Narrower, a discussion open takes the list's place, and its tabs' and filters'.
  const over = !beside && !!thread.id;
  const [place, setPlace] = useState<Place>("recent");
  const [filter, setFilter] = useState<RecentFilter>("all");
  const [search, setSearch] = useState("");
  const [typed, setTyped] = useState("");
  useEffect(() => {
    const id = setTimeout(() => setSearch(typed), 300);
    return () => clearTimeout(id);
  }, [typed]);
  const shown = place === "following" && !user ? "recent" : place;
  const tabs: { id: Place; label: string }[] = [
    { id: "recent", label: t({ message: "Recent", context: "Community tab", comment: "Community tab: the newest discussions on every app" }) },
    { id: "general", label: i18n._(CATEGORY_LABELS.general) },
    ...(user ? [{ id: "following" as const, label: t({ message: "Following", comment: "Community tab: the discussions the player follows" }) }] : []),
  ];
  const listName = tabs.find((tab) => tab.id === shown)?.label ?? t`Community`;
  const filters: { id: RecentFilter; label: string }[] = [
    { id: "all", label: t({ message: "All activity", comment: "Community filter: discussions on every app" }) },
    { id: "mine", label: t({ message: "Apps I have", comment: "Community filter: only discussions on apps in the player's library" }) },
    { id: "new", label: t({ message: "New discussions", comment: "Community filter: newest started first, not newest comment" }) },
  ];
  return (
    <section ref={page} className="app-page community-page" data-split={beside || undefined} aria-label={t`Community`}>
      <button className="back-link" onClick={onBack}>
        <ArrowLeft size={15} /> {back}
      </button>
      <h1>
        <Trans comment="The page where players talk about anything that isn't one app">Community</Trans>
      </h1>
      <p className="muted">
        <Trans>
          What players are talking about across the catalog, and a place for everything else. Need help with Quiver Launcher? Ask on <a href={DISCORD_URL}>Discord</a> or in{" "}
          <a href={ISSUES_URL}>GitHub Issues</a>.
        </Trans>
      </p>
      <ThreadPlace.Provider value={at}>
        <div className="community-body">
          <div className="community-list" hidden={over}>
          <div className="community-bar">
            <div className="detail-tabs" role="tablist" aria-label={t({ message: "Community", comment: "The Community page's tabs" })}>
              {tabs.map((tab) => (
                <button key={tab.id} type="button" role="tab" aria-selected={shown === tab.id} onClick={() => setPlace(tab.id)}>
                  {tab.label}
                  {tab.id === "following" && live && (
                    <ErrorBoundary fallback={() => null}>
                      <NewComments className="tab-count" />
                    </ErrorBoundary>
                  )}
                </button>
              ))}
            </div>
            {shown !== "following" && (
              <label className="community-search">
                <Search size={16} aria-hidden="true" />
                <input type="search" value={typed} placeholder={t`Search titles`} aria-label={t`Search discussion titles`} onChange={(e) => setTyped(e.target.value)} />
              </label>
            )}
          </div>
          {shown === "recent" && !search.trim() && (
            <div className="community-filters">
              <div className="community-chips" role="group" aria-label={t`Show`}>
                {filters.map((f) => (
                  <button key={f.id} type="button" className="community-chip" aria-pressed={filter === f.id} onClick={() => setFilter(f.id)}>
                    {f.label}
                  </button>
                ))}
              </div>
              <p className="muted community-hint">
                <Trans>Start a discussion from an app's page, or in General.</Trans>
              </p>
            </div>
          )}
            <Live>
              {shown === "following" ? <Followed /> : shown === "general" ? <Board key="general" category="general" search={search} /> : <Recent key={filter} filter={filter} search={search} />}
            </Live>
          </div>
          {thread.id ? (
            <ThreadPane key={thread.id} id={thread.id} back={listName} onClose={thread.close} beside={beside} />
          ) : (
            beside && (
              <div className="thread-pane thread-empty" data-beside>
                <MessagesSquare size={28} aria-hidden="true" />
                <p>
                  <Trans>Choose a discussion to read it here.</Trans>
                </p>
              </div>
            )
          )}
        </div>
      </ThreadPlace.Provider>
    </section>
  );
}

/** The library's catalog apps, by their ids on the site (not apps the player added themselves). */
function useLibraryEntryIds() {
  const { library } = useLauncher();
  return useMemo(() => library.map((i) => i.id).filter((id) => !isOwn(id)).slice(0, 500), [library]);
}

/** Followed discussions with new comments, so Recent can say so: the newest 50 followed. */
function useUnseen() {
  const { user } = useAccount();
  const list = usePaginatedQuery(fns.followed, user ? {} : "skip", { initialNumItems: 50 });
  return useMemo(() => new Set((list.results as Summary[]).filter((d) => d.unseen).map((d) => d.id)), [list.results]);
}

/** Community's Recent tab: the newest discussions on every app and in General, only on the library's apps, or newest started first. */
function Recent({ filter, search }: { filter: RecentFilter; search: string }) {
  const text = search.trim();
  const mine = filter === "mine";
  const ids = useLibraryEntryIds();
  const unseen = useUnseen();
  const [limit, setLimit] = useState(20);
  const recentArgs = !text && !mine ? { order: filter === "new" ? "new" : "activity" } : "skip";
  const all = useFirstPage(fns.recent, recentArgs, RECENT_PAGE, usePaginatedQuery(fns.recent, recentArgs, { initialNumItems: RECENT_PAGE }));
  const inLibrary = useQuery(fns.recentInApps, !text && mine ? { entryIds: ids, limit } : "skip") as Summary[] | undefined;
  const found = useQuery(fns.search, text ? { text } : "skip") as Summary[] | undefined;
  const rows = text ? found : mine ? inLibrary : all.status === "LoadingFirstPage" ? undefined : (all.results as Summary[]);
  const more = !text && (mine ? (inLibrary?.length ?? 0) >= limit && limit < 100 : all.status === "CanLoadMore");
  return (
    <>
      {rows === undefined ? (
        <Spinner label={text ? t`Searching…` : t`Loading discussions…`} />
      ) : rows.length ? (
        <ul className="discussion-list panel">
          {rows.map((thread) => (
            <ActivityRow key={thread.id} thread={{ ...thread, unseen: unseen.has(thread.id) }} />
          ))}
        </ul>
      ) : (
        <p className="muted">
          {text ? (
            <Trans>No discussion titles match “{text}”.</Trans>
          ) : mine ? (
            <Trans>No discussions yet on the apps in your library.</Trans>
          ) : (
            <Trans>No discussions yet. Ask a question or share a tip.</Trans>
          )}
        </p>
      )}
      {more && (
        <button type="button" className="button secondary" onClick={() => (mine ? setLimit((n) => n + 20) : all.loadMore(20))}>
          <Trans>Load more</Trans>
        </button>
      )}
    </>
  );
}

/** One discussion in a list across apps, as on the site: its app's icon (or General's), the app's name and consoles, title, who started it, comments and last activity. */
function ActivityRow({ thread }: { thread: Summary }) {
  const { open, current } = useOpenThread();
  const { thread: threadAhead } = useDiscussionsAhead();
  const { catalog, consoleNames } = useLauncher();
  const entry = thread.entry;
  const icon = entry ? (entry.icon ?? catalog[entry.id]?.artwork) : undefined;
  const consoles = [...new Set((entry?.consoles ?? []).map((c) => consoleNames[c] ?? c.toUpperCase()))].slice(0, 2);
  const active = relativeTime(thread.lastActivityAt);
  const author = thread.author;
  return (
    <li className="discussion-row activity">
      <button
        type="button"
        className="discussion-row-link"
        data-thread={thread.id}
        aria-current={current === thread.id || undefined}
        onClick={() => open(thread.id)}
        onPointerEnter={() => threadAhead(thread.id)}
        onFocus={() => threadAhead(thread.id)}
      >
        {entry ? (
          <Artwork className="discussion-row-icon" src={icon} sources={icon ? iconSources(icon) : undefined} name={entry.name} />
        ) : (
          <span className="discussion-row-icon general" aria-hidden="true">
            <MessagesSquare size={20} />
          </span>
        )}
        <span className="discussion-row-body">
          <span className="discussion-row-place">
            <span className="discussion-row-app">{entry?.name ?? i18n._(CATEGORY_LABELS.general)}</span>
            {consoles.length > 0 && <span>{consoles.join(", ")}</span>}
          </span>
          <span className="discussion-row-title">
            <Flags thread={{ ...thread, pinned: false }} />
            {thread.title}
          </span>
          <span className="discussion-row-meta">
            {[
              t({ message: `Started by ${author}`, comment: "Who started a discussion, in a list of discussions" }),
              plural(thread.comments, { one: "# comment", other: "# comments" }),
              t({ message: `active ${active}`, comment: "When a discussion last had a comment, as in “active 2 days ago”" }),
            ].join(" · ")}
          </span>
        </span>
      </button>
    </li>
  );
}

function Followed() {
  const list = usePaginatedQuery(fns.followed, {}, { initialNumItems: 20 });
  const results = list.results as Summary[];
  if (list.status === "LoadingFirstPage") return <Spinner label={t`Loading discussions…`} />;
  return (
    <>
      {results.length === 0 ? (
        <p className="muted">
          <Trans>Discussions you start or comment in show here, with their new comments.</Trans>
        </p>
      ) : (
        <ul className="discussion-list panel">
          {results.map((thread) => (
            <ActivityRow key={thread.id} thread={thread} />
          ))}
        </ul>
      )}
      {list.status === "CanLoadMore" && (
        <button type="button" className="button secondary" onClick={() => list.loadMore(20)}>
          <Trans>Load more</Trans>
        </button>
      )}
    </>
  );
}

/** The top bar's button to the Community page, with how many followed discussions have new comments. */
export function CommunityButton({ open, onOpen }: { open: boolean; onOpen: () => void }) {
  const { user } = useAccount();
  const live = useLive();
  const { community } = useDiscussionsAhead();
  return (
    <button
      className={`icon community-button${open ? " active" : ""}`}
      aria-label={t`Community`}
      title={t`Community`}
      aria-current={open ? "page" : undefined}
      onClick={onOpen}
      onPointerEnter={community}
      onFocus={community}
    >
      <MessagesSquare size={19} />
      {live && user && (
        <ErrorBoundary fallback={() => null}>
          <NewComments />
        </ErrorBoundary>
      )}
    </button>
  );
}

function NewComments({ className = "downloads-count" }: { className?: string }) {
  const count = useQuery(fns.unseenCount, {}) as number | undefined;
  if (!count) return null;
  return (
    <span className={className} aria-label={plural(count, { one: "# discussion with new comments", other: "# discussions with new comments" })}>
      {count >= 50 ? "50+" : count}
    </span>
  );
}

/**
 * The app a discussion is about, over its title: a strip of its banner, its icon, name and consoles, opening its
 * page. The site sends the art; an app in the catalog here fills in what an older site leaves out.
 */
function DiscussionApp({ entry, onOpen }: { entry: NonNullable<Summary["entry"]>; onOpen: () => void }) {
  const { catalog, consoleNames } = useLauncher();
  const known = catalog[entry.id];
  const icon = entry.icon ?? known?.artwork;
  const hero = entry.hero ?? (known?.libraryArt?.hero || known?.libraryArt?.header);
  const consoles = [...new Set((entry.consoles ?? known?.consoles ?? []).map((c) => consoleNames[c] ?? c.toUpperCase()))];
  const [broken, setBroken] = useState(false);
  return (
    <button type="button" className={`discussion-app${hero && !broken ? " has-art" : ""}`} onClick={onOpen}>
      {hero && !broken && <SourcedImage key={hero} className="discussion-app-art" sources={heroSources(hero)} alt="" priority onFail={() => setBroken(true)} />}
      <Artwork className="discussion-app-icon" src={icon} sources={icon ? iconSources(icon) : undefined} name={entry.name} priority />
      <span className="discussion-app-text">
        <span className="discussion-app-name">{entry.name}</span>
        {consoles.length > 0 && <span className="discussion-app-consoles">{consoles.join(", ")}</span>}
      </span>
      <ChevronRight size={20} aria-hidden="true" className="discussion-app-go" />
    </button>
  );
}
