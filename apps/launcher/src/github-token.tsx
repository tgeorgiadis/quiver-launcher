/**
 * Settings: the player's GitHub token. Without one GitHub allows a network 60
 * requests an hour, which every app added from GitHub shares; a token raises
 * that to 5,000. As in Quiver Launcher 3, saving checks the token with GitHub
 * first: one GitHub refuses isn't saved, one that couldn't be checked
 * (offline) is.
 */
import { useEffect, useState } from "react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { i18n } from "@quiverlauncher/i18n";
import { ApiError, createGithub, type RateLimit } from "@quiverlauncher/api";
import { native } from "./native";
import { useLauncher } from "./store";

/** GitHub's page for a new token, with its name filled in. */
export const CREATE_TOKEN_URL = "https://github.com/settings/tokens/new?description=Quiver+Launcher";

const time = (at: number) => new Date(at).toLocaleTimeString(i18n.locale, { hour: "numeric", minute: "2-digit" });

export function GithubTokenSetting() {
  const { config, githubToken, saveGithubToken, github } = useLauncher();
  const saved = githubToken ?? "";
  const [draft, setDraft] = useState(saved);
  const [limit, setLimit] = useState<RateLimit | null>(null);
  const [message, setMessage] = useState<{ text: string; error?: boolean } | null>(null);
  const [checking, setChecking] = useState(false);
  const [guide, setGuide] = useState(false);
  // Asking GitHub how many requests are left doesn't use one up.
  useEffect(() => {
    let live = true;
    github.rateLimit().then(
      (l) => live && setLimit(l),
      () => live && setLimit(null),
    );
    return () => void (live = false);
  }, [github]);
  async function save(token: string) {
    setMessage(null);
    if (token) {
      setChecking(true);
      const refused = await createGithub(config.githubApi, token)
        .rateLimit()
        .then(
          () => false,
          (e: unknown) => e instanceof ApiError && e.status === 401,
        );
      setChecking(false);
      if (refused) return setMessage({ text: t`GitHub didn't accept that token, so it wasn't saved. Check that you copied all of it.`, error: true });
    }
    try {
      await saveGithubToken(token || undefined);
    } catch {
      return setMessage({ text: t`Couldn't save the token. Try again.`, error: true });
    }
    setDraft(token);
    setMessage({ text: token ? t`Saved. Requests to GitHub use your token now.` : t({ message: "Removed.", comment: "The GitHub token was removed" }) });
  }
  const left = limit?.remaining.toLocaleString(i18n.locale);
  const most = limit?.limit.toLocaleString(i18n.locale);
  const until = limit && time(limit.resetAt);
  return (
    <section className="settings-section github-token" aria-label={t`GitHub token`}>
      <h3>
        <Trans>GitHub token</Trans>
      </h3>
      <p className="muted">
        <Trans>
          Apps you add from GitHub get their releases from GitHub itself, which allows this network 60 requests an hour, or 5,000 with a token. Apps from the
          catalog don&apos;t need one.
        </Trans>
      </p>
      {limit && (
        <p className="muted" role="status">
          <Trans>
            {left} of {most} requests left this hour, until {until}.
          </Trans>
        </p>
      )}
      <input
        type="password"
        aria-label={t`GitHub token`}
        placeholder="ghp_… or github_pat_…"
        autoComplete="off"
        spellCheck={false}
        value={draft}
        onChange={(e) => setDraft(e.target.value)}
      />
      <div className="row">
        <button className="primary" disabled={checking || !draft.trim() || draft.trim() === saved} onClick={() => void save(draft.trim())}>
          {checking ? t`Checking…` : t`Save token`}
        </button>
        {saved && (
          <button onClick={() => void save("")}>
            <Trans>Remove token</Trans>
          </button>
        )}
      </div>
      <div className="row">
        <button onClick={() => void native.openUrl(CREATE_TOKEN_URL)}>
          <Trans>Create a token on GitHub</Trans>
        </button>
        <button aria-expanded={guide} onClick={() => setGuide(!guide)}>
          {guide ? t`Hide the steps` : t`How to make one`}
        </button>
      </div>
      {guide && (
        <ol className="muted">
          <li>
            <Trans>Choose Create a token on GitHub, and sign in to GitHub.</Trans>
          </li>
          <li>
            <Trans>Choose when it expires.</Trans>
          </li>
          <li>
            <Trans>Leave every scope unticked: public releases need none.</Trans>
          </li>
          <li>
            <Trans comment="Generate token is the name of GitHub's button, as GitHub shows it in English">
              Choose Generate token, copy it, paste it above and choose Save token.
            </Trans>
          </li>
        </ol>
      )}
      {guide && (
        <p className="muted">
          <Trans>Keep the token private. When it expires, make a new one and save it here.</Trans>
        </p>
      )}
      {message && (
        <p className={message.error ? "job-error" : "muted"} role="status">
          {message.text}
        </p>
      )}
    </section>
  );
}
