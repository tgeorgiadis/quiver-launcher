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

export type Account = {
  /** False until the saved session has been checked. */
  ready: boolean;
  user: { id: string; name: string } | null;
  /** The account's library, live; undefined while signed out or loading. */
  items: ServerItem[] | undefined;
  /** Resolves to an error message, or null when signed in. */
  signIn: (username: string, password: string, create: boolean) => Promise<string | null>;
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
  if (config.accountApi) return <TestAccount base={config.accountApi}>{children}</TestAccount>;
  return <ConvexAccount url={config.convex}>{children}</ConvexAccount>;
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
};

function ConvexAccount({ url, children }: { url: string; children: ReactNode }) {
  const client = useMemo(() => new ConvexReactClient(url), [url]);
  return (
    // The website's auth functions, without its generated API types (the site is private).
    <ConvexAuthProvider client={client} api={fns as never} storage={keychain}>
      <ConvexAccountState>{children}</ConvexAccountState>
    </ConvexAuthProvider>
  );
}

function ConvexAccountState({ children }: { children: ReactNode }) {
  const { isAuthenticated, isLoading } = useConvexAuth();
  const me = useQuery(fns.me, isAuthenticated ? {} : "skip") as { _id: string; displayName: string } | null | undefined;
  const items = useQuery(fns.list, isAuthenticated ? {} : "skip") as ServerItem[] | undefined;
  const save = useMutation(fns.save);
  const review = useMutation(fns.review);
  const login = useSignInWithPassword(fns.signIn as never);
  const register = useSignUpWithPassword(fns.signUp as never);
  const { signOut } = useAuthActions();
  const account: Account = {
    ready: !isLoading,
    user: me ? { id: me._id, name: me.displayName } : null,
    items: me ? items : undefined,
    async signIn(username, password, create) {
      const result = await (create ? register.signUp : login.signIn)({ username, password });
      return result.status === "complete" ? null : messageOf(result);
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
function TestAccount({ base, children }: { base: string; children: ReactNode }) {
  const [token, setToken] = useState<string | null | undefined>(undefined);
  const [user, setUser] = useState<Account["user"]>(null);
  const [items, setItems] = useState<ServerItem[] | undefined>();
  const call = (path: string, body?: unknown) =>
    fetch(base + path, {
      method: body ? "POST" : "GET",
      headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
      body: body ? JSON.stringify(body) : undefined,
    }).then((r) => (r.ok ? r.json() : Promise.reject(new Error(String(r.status)))));
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
      const { token } = await r.json();
      await native.secretSet("testToken", token);
      setToken(token);
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
