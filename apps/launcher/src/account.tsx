/**
 * Optional sign-in through quiverlauncher.com (Convex Auth, the same backend
 * as the website). Signed in, the library syncs and reviews can be left.
 */
import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { ConvexReactClient, useConvexAuth, useMutation, useQuery } from "convex/react";
import { makeFunctionReference, type FunctionReference } from "convex/server";
import { ConvexError } from "convex/values";
import { ConvexAuthProvider, useAuthActions, type TokenStorage } from "@convex-dev/auth/react";
import { useSignInWithPassword, useSignUpWithPassword } from "@convex-dev/auth/providers/password/react";
import type { Feedback, Os } from "@quiver/api";
import { native, type Config } from "./native";
import type { Change, Collection, ServerCollection, ServerItem } from "./sync";

export type ReviewInput = {
  entryId: string;
  result: "runs" | "issues" | "broken";
  body?: string;
  platform: Exclude<Os, "unknown">;
  entryReleaseId?: string;
};

export type Provider = "github" | "discord";

export type Account = {
  /** False until the saved session, and the account it's for, have been checked. */
  ready: boolean;
  user: AccountUser | null;
  /** The account's library, live; undefined while signed out or loading. */
  items: ServerItem[] | undefined;
  /** Resolves to an error message, or null when signed in. */
  signIn: (username: string, password: string, create: boolean) => Promise<string | null>;
  /** Signs in with GitHub or Discord in the system browser; same result as signIn. */
  signInWith: (provider: Provider) => Promise<string | null>;
  signOut: () => Promise<void>;
  /** The account's library collections, live; undefined while signed out, loading or not on the site yet. */
  collections: ServerCollection[] | undefined;
  /** Saves whole collections; the ones refused come back. */
  saveCollections: (collections: Collection[]) => Promise<{ key: string; error: string }[]>;
  /** Saves changes; the ones refused come back. */
  save: (changes: Change[]) => Promise<{ key: string; error: string }[]>;
  /** Saves the player's feedback on an app, replacing any they gave before; rejects with a message to show. */
  review: (review: ReviewInput) => Promise<void>;
  /** The player's own feedback on an app, null signed out or when they haven't given any. */
  ownReview: (entryId: string) => Promise<Feedback | null>;
  /** Shares a playlist publicly (again: updates it); resolves to its slug. Rejects with a message to show. */
  shareList: (list: ShareInput) => Promise<string>;
  /** Stops sharing a playlist. */
  unshareList: (slug: string) => Promise<void>;
  /** Turns anonymous usage data on or off for the account, wherever they sign in (the website's users.setAnalytics). */
  setAnalytics: (enabled: boolean) => Promise<void>;
};

/** The signed-in account, as the website's users.me has it. */
export type AccountUser = {
  id: string;
  name: string;
  role?: string;
  provider?: string;
  createdAt?: number;
  /** Usage data turned off for the account (telemetry.ts follows it while signed in). */
  analyticsOptOut?: boolean;
};

/** A playlist to share: the catalog apps on it, by entry id. */
export type ShareInput = { collectionKey: string; name: string; description?: string; apps: { entryId: string }[] };

const SHARE_FAILED = "Couldn't share this playlist. Try again.";

const REVIEW_FAILED = "Couldn't save your feedback. Try again.";

const AccountContext = createContext<Account | null>(null);
export const useAccount = () => useContext(AccountContext)!;

/** Tokens live in the OS keychain (Rust side), not the webview's storage. */
const keychain: TokenStorage = {
  getItem: (key) => native.secretGet(key),
  setItem: (key, value) => native.secretSet(key, value),
  removeItem: (key) => native.secretSet(key, null),
};

export function AccountProvider({ config, children }: { config: Config; children: ReactNode }) {
  if (config.accountApi) return <TestAccount base={config.accountApi} returnTo={config.returnTo}>{children}</TestAccount>;
  return <ConvexAccount url={config.convex} returnTo={config.returnTo}>{children}</ConvexAccount>;
}

const ref = <T extends "query" | "mutation">(type: T, name: string) =>
  makeFunctionReference<T>(name) as unknown as FunctionReference<T>;
const fns = {
  refreshSession: ref("mutation", "auth:refreshSession"),
  signOut: ref("mutation", "auth:signOut"),
  signIn: ref("mutation", "auth:signInWithPassword"),
  signUp: ref("mutation", "auth:signUpWithPassword"),
  me: ref("query", "users:me"),
  list: ref("query", "library:list"),
  save: ref("mutation", "library:save"),
  collections: ref("query", "libraryCollections:list"),
  saveCollections: ref("mutation", "libraryCollections:save"),
  review: ref("mutation", "reviews:save"),
  ownReview: ref("query", "reviews:own"),
  shareList: ref("mutation", "sharedLists:share"),
  unshareList: ref("mutation", "sharedLists:unshare"),
  setAnalytics: ref("mutation", "users:setAnalytics"),
  github: [ref("mutation", "auth:startSignInGithub"), ref("mutation", "auth:completeSignInGithub")],
  discord: [ref("mutation", "auth:startSignInDiscord"), ref("mutation", "auth:completeSignInDiscord")],
};

/**
 * Starts a sign-in in the system browser, waits for it to return to the
 * launcher's loopback address, and redeems the code.
 */
async function browserSignIn<T>(
  start: (redirectTo: string) => Promise<{ redirect: string; state: string }>,
  complete: (code: string, state: string) => Promise<T | null>,
  returnTo: string,
): Promise<{ error: string } | { session: T }> {
  try {
    const { redirect, state } = await start(returnTo);
    const back = await native.browserSignIn(redirect);
    if ("error" in back) return { error: OAUTH_ERRORS[back.error] ?? OAUTH_ERRORS.oauth_error };
    const session = await complete(back.code, state);
    return session ? { session } : { error: OAUTH_ERRORS.expired };
  } catch (e) {
    const message = e instanceof ConvexError && typeof e.data === "string" ? e.data : typeof e === "string" ? e : null;
    return { error: message ?? OAUTH_ERRORS.oauth_error };
  }
}

const OAUTH_ERRORS: Record<string, string> = {
  access_denied: "Sign-in was cancelled.",
  expired: "That sign-in took too long. Try again.",
  oauth_error: "Couldn't sign in. Check your connection and try again.",
};

function ConvexAccount({ url, returnTo, children }: { url: string; returnTo: string; children: ReactNode }) {
  const client = useMemo(() => new ConvexReactClient(url), [url]);
  return (
    // The website's auth functions, without its generated API types (the site is private).
    <ConvexAuthProvider client={client} api={fns as never} storage={keychain}>
      <ConvexAccountState client={client} returnTo={returnTo}>
        {children}
      </ConvexAccountState>
    </ConvexAuthProvider>
  );
}

type Tokens = Parameters<ReturnType<typeof useAuthActions>["setSession"]>[0];

function ConvexAccountState({ client, returnTo, children }: { client: ConvexReactClient; returnTo: string; children: ReactNode }) {
  const { isAuthenticated, isLoading } = useConvexAuth();
  const me = useQuery(fns.me, isAuthenticated ? {} : "skip") as
    | { _id: string; _creationTime: number; displayName: string; role?: string; provider?: string; analyticsOptOut?: boolean }
    | null
    | undefined;
  const items = useQuery(fns.list, isAuthenticated ? {} : "skip") as ServerItem[] | undefined;
  const collections = useOptionalQuery<ServerCollection[]>(client, fns.collections, isAuthenticated);
  const save = useMutation(fns.save);
  const saveCollections = useMutation(fns.saveCollections);
  const review = useMutation(fns.review);
  const setAnalytics = useMutation(fns.setAnalytics);
  const login = useSignInWithPassword(fns.signIn as never);
  const register = useSignUpWithPassword(fns.signUp as never);
  const { signOut, setSession } = useAuthActions();
  // At the start, signed in, ready once the account itself has loaded, so its settings (such as usage data) are known; then it stays ready.
  const settled = useRef(false);
  const ready = settled.current || (!isLoading && (!isAuthenticated || me !== undefined));
  settled.current = ready;
  const account: Account = {
    ready,
    user: me
      ? {
          id: me._id,
          name: me.displayName,
          role: me.role,
          provider: me.provider,
          createdAt: me._creationTime,
          analyticsOptOut: me.analyticsOptOut,
        }
      : null,
    items: me ? items : undefined,
    collections: me ? collections : undefined,
    saveCollections: (list) => saveCollections({ collections: list.map(toServer) }),
    async signIn(username, password, create) {
      const result = await (create ? register.signUp : login.signIn)({ username, password });
      return result.status === "complete" ? null : messageOf(result);
    },
    async signInWith(provider) {
      const [start, complete] = fns[provider];
      const result = await browserSignIn(
        (redirectTo) => client.mutation(start, { redirectTo }),
        async (code, state) => {
          const r = await client.mutation(complete, { code, state });
          return r.status === "complete" ? (r.tokens as Tokens) : null;
        },
        returnTo,
      );
      if ("error" in result) return result.error;
      await setSession(result.session);
      return null;
    },
    signOut: async () => void (await signOut()),
    save: (changes) => save({ changes }),
    async review(r) {
      try {
        await review(r);
      } catch (e) {
        // The site's reasons ("Note must be 500 characters or fewer") come as the error's data.
        throw new Error(e instanceof ConvexError && typeof e.data === "string" ? e.data : REVIEW_FAILED);
      }
    },
    ownReview: (entryId) => (me ? (client.query(fns.ownReview, { entryId }) as Promise<Feedback | null>) : Promise.resolve(null)),
    async shareList(list) {
      try {
        return ((await client.mutation(fns.shareList, list)) as { slug: string }).slug;
      } catch (e) {
        throw new Error(e instanceof ConvexError && typeof e.data === "string" ? e.data : SHARE_FAILED);
      }
    },
    unshareList: async (slug) => void (await client.mutation(fns.unshareList, { slug })),
    setAnalytics: async (enabled) => void (await setAnalytics({ enabled })),
  };
  return <AccountContext.Provider value={account}>{children}</AccountContext.Provider>;
}

/** What the site stores of a collection; apps on this computer only never leave it. null clears a field there. */
const toServer = ({ key, name, tags, consoles, installed, order, removed, apps, projectTypes, ai, follows }: Collection) => ({
  key, name, tags, consoles, ...(installed ? { installed } : {}), order, removed: Boolean(removed),
  apps: (apps ?? []).filter((id) => !id.startsWith("local:")),
  projectTypes: projectTypes?.length ? projectTypes : null,
  ai: ai ?? null,
  follows: follows ?? null,
});

/**
 * A live query that may not exist on the site yet (a newer launcher against
 * an older site): undefined instead of an error.
 */
function useOptionalQuery<T>(client: ConvexReactClient, query: FunctionReference<"query">, on: boolean): T | undefined {
  const [value, setValue] = useState<T>();
  useEffect(() => {
    if (!on) return setValue(undefined);
    const watch = client.watchQuery(query, {});
    const read = () => {
      try {
        setValue(watch.localQueryResult() as T | undefined);
      } catch {
        setValue(undefined);
      }
    };
    read();
    return watch.onUpdate(read);
  }, [client, query, on]);
  return value;
}

/** The website's wording for sign-in errors (AuthPage.tsx there). */
const AUTH_ERRORS: Record<string, string> = {
  INVALID_CREDENTIALS: "Username or password is incorrect.",
  USER_NOT_FOUND: "Username or password is incorrect.",
  USERNAME_TAKEN: "That username is already in use.",
  PASSWORD_TOO_SHORT: "Use at least 10 characters.",
  PASSWORD_TOO_LONG: "Use no more than 100 characters.",
  PASSWORD_TOO_COMMON: "Choose a less common password.",
  PASSWORD_HAS_SURROUNDING_WHITESPACE: "Password cannot begin or end with whitespace.",
  RATE_LIMITED: "Too many attempts. Wait a moment and try again.",
  ACCOUNT_SUSPENDED: "This account is suspended.",
  SIGN_UPS_BUSY: "We're getting a lot of new accounts right now. Try again in a few minutes.",
};

function messageOf(result: unknown) {
  const error = (result as { userError?: { error: string; cause?: unknown } }).userError;
  // Errors the site's own callbacks throw arrive as OTHER_ERROR with the ConvexError as `cause`.
  const cause = error?.cause instanceof ConvexError && typeof error.cause.data === "string" ? error.cause.data : null;
  return cause ?? AUTH_ERRORS[error?.error ?? ""] ?? "Couldn't sign in. Check your connection and try again.";
}

/**
 * A stand-in with the same contract over plain HTTP, for end-to-end tests
 * (QUIVER_ACCOUNT_API), since the site's backend is private.
 */
function TestAccount({ base, returnTo, children }: { base: string; returnTo: string; children: ReactNode }) {
  const [token, setToken] = useState<string | null | undefined>(undefined);
  const [user, setUser] = useState<Account["user"]>(null);
  const [items, setItems] = useState<ServerItem[] | undefined>();
  const [collections, setCollections] = useState<ServerCollection[] | undefined>();
  // The saved session's account has been asked for once.
  const [polled, setPolled] = useState(false);
  // A poll that started before the usage data setting changed may answer with the old one.
  const changes = useRef(0);
  const call = (path: string, body?: unknown) =>
    fetch(base + path, {
      method: body ? "POST" : "GET",
      headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
      body: body ? JSON.stringify(body) : undefined,
    }).then((r) => (r.ok ? r.json() : Promise.reject(new Error(String(r.status)))));
  const adopt = async (token: string) => {
    await native.secretSet("testToken", token);
    setToken(token);
  };
  useEffect(() => void native.secretGet("testToken").then((t) => setToken(t ?? null)), []);
  useEffect(() => {
    if (!token) return setUser(null), setItems(undefined), setCollections(undefined);
    let live = true;
    const poll = () => {
      const at = changes.current;
      return call("/library")
        .then((r) => live && at === changes.current && (setUser(r.user), setItems(r.items), setCollections(r.collections)))
        .catch(() => {})
        .finally(() => live && (setPolled(true), setTimeout(poll, 400)));
    };
    poll();
    return () => void (live = false);
  }, [token]); // eslint-disable-line react-hooks/exhaustive-deps
  const account: Account = {
    ready: token !== undefined && (token === null || polled),
    user,
    items,
    async signIn(username, password, create) {
      const r = await fetch(base + "/signin", { method: "POST", body: JSON.stringify({ username, password, create }) });
      if (!r.ok) return "That username and password don't match.";
      await adopt((await r.json()).token);
      return null;
    },
    async signInWith(provider) {
      const post = (path: string, body: unknown) =>
        fetch(base + path, { method: "POST", body: JSON.stringify(body) }).then((r) => (r.ok ? r.json() : null));
      const result = await browserSignIn<string>(
        (redirectTo) => post("/oauth/start", { provider, redirectTo }),
        async (code, state) => (await post("/oauth/complete", { code, state }))?.token ?? null,
        returnTo,
      );
      if ("error" in result) return result.error;
      await adopt(result.session);
      return null;
    },
    async signOut() {
      await native.secretSet("testToken", null);
      setToken(null);
    },
    save: (changes) => call("/library/save", { changes }),
    collections,
    saveCollections: (list) => call("/collections/save", { collections: list.map(toServer) }),
    async review(r) {
      const response = await fetch(base + "/reviews", {
        method: "POST",
        headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
        body: JSON.stringify(r),
      });
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? REVIEW_FAILED);
    },
    ownReview: (entryId) => (token ? call("/reviews/own", { entryId }) : Promise.resolve(null)),
    async shareList(list) {
      const response = await fetch(base + "/lists/share", {
        method: "POST",
        headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
        body: JSON.stringify(list),
      });
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? SHARE_FAILED);
      return (await response.json()).slug;
    },
    unshareList: (slug) => call("/lists/unshare", { slug }),
    async setAnalytics(enabled) {
      changes.current++;
      setUser((u) => u && { ...u, analyticsOptOut: enabled ? undefined : true });
      await call("/analytics", { enabled }).finally(() => changes.current++);
    },
  };
  return <AccountContext.Provider value={account}>{children}</AccountContext.Provider>;
}
