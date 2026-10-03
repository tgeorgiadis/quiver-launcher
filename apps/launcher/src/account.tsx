/**
 * Optional sign-in through quiverlauncher.com (Convex Auth, the same backend
 * as the website). Signed in, the library syncs and reviews can be left.
 */
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { ConvexReactClient, useConvexAuth, useMutation, useQuery } from "convex/react";
import { makeFunctionReference, type FunctionReference } from "convex/server";
import { ConvexError } from "convex/values";
import { ConvexAuthProvider, useAuthActions, type TokenStorage } from "@convex-dev/auth/react";
import { useSignInWithPassword, useSignUpWithPassword } from "@convex-dev/auth/providers/password/react";
import type { Os } from "@quiver/api";
import { native, type Config } from "./native";
import type { Change, ServerItem } from "./sync";

export type ReviewInput = {
  entryId: string;
  result: "runs" | "issues" | "broken";
  body?: string;
  platform: Exclude<Os, "unknown">;
  entryReleaseId?: string;
};

export type Provider = "github" | "discord";

export type Account = {
  /** False until the saved session has been checked. */
  ready: boolean;
  user: { id: string; name: string } | null;
  /** The account's library, live; undefined while signed out or loading. */
  items: ServerItem[] | undefined;
  /** Resolves to an error message, or null when signed in. */
  signIn: (username: string, password: string, create: boolean) => Promise<string | null>;
  /** Signs in with GitHub or Discord in the system browser; same result as signIn. */
  signInWith: (provider: Provider) => Promise<string | null>;
  signOut: () => Promise<void>;
  save: (changes: Change[]) => Promise<void>;
  review: (review: ReviewInput) => Promise<void>;
};

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
  review: ref("mutation", "reviews:save"),
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
  const me = useQuery(fns.me, isAuthenticated ? {} : "skip") as { _id: string; displayName: string } | null | undefined;
  const items = useQuery(fns.list, isAuthenticated ? {} : "skip") as ServerItem[] | undefined;
  const save = useMutation(fns.save);
  const review = useMutation(fns.review);
  const login = useSignInWithPassword(fns.signIn as never);
  const register = useSignUpWithPassword(fns.signUp as never);
  const { signOut, setSession } = useAuthActions();
  const account: Account = {
    ready: !isLoading,
    user: me ? { id: me._id, name: me.displayName } : null,
    items: me ? items : undefined,
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
    save: async (changes) => void (await save({ changes })),
    review: async (r) => void (await review(r)),
  };
  return <AccountContext.Provider value={account}>{children}</AccountContext.Provider>;
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
    if (!token) return setUser(null), setItems(undefined);
    let live = true;
    const poll = () =>
      call("/library")
        .then((r) => live && (setUser(r.user), setItems(r.items)))
        .catch(() => {})
        .finally(() => live && setTimeout(poll, 400));
    poll();
    return () => void (live = false);
  }, [token]); // eslint-disable-line react-hooks/exhaustive-deps
  const account: Account = {
    ready: token !== undefined,
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
    review: (r) => call("/reviews", r),
  };
  return <AccountContext.Provider value={account}>{children}</AccountContext.Provider>;
}
