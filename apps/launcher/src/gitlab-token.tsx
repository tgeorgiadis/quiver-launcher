/**
 * Settings: the player's GitLab token. GitLab allows a network fewer requests
 * without one, which every app added from GitLab shares; a token raises that.
 * Saving checks the token with GitLab first: one GitLab refuses isn't saved,
 * one that couldn't be checked (offline) is.
 */
import { useEffect, useState } from "react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { i18n } from "@quiverlauncher/i18n";
import { ApiError, createGitlab, type RateLimit } from "@quiverlauncher/api";
import { native } from "./native";
import { useLauncher } from "./store";

/** GitLab's page for a new token, with its name and the scope it needs filled in. */
export const CREATE_TOKEN_URL = "https://gitlab.com/-/user_settings/personal_access_tokens?name=Quiver+Launcher&scopes=read_api";

export function GitlabTokenSetting() {
  const { config, gitlabToken, saveGitlabToken, gitlab } = useLauncher();
  const saved = gitlabToken ?? "";
  const [draft, setDraft] = useState(saved);
  const [limit, setLimit] = useState<RateLimit | null>(null);
  const [message, setMessage] = useState<{ text: string; error?: boolean } | null>(null);
  const [checking, setChecking] = useState(false);
  const [guide, setGuide] = useState(false);
  // Asking GitLab how many requests are left doesn't use one up.
  useEffect(() => {
    let live = true;
    gitlab.rateLimit().then(
      (l) => live && setLimit(l),
      () => live && setLimit(null),
    );
    return () => void (live = false);
  }, [gitlab]);
  async function save(token: string) {
    setMessage(null);
    if (token) {
      setChecking(true);
      const refused = await createGitlab(config.gitlabApi, token, native.gitlabLimit)
        .rateLimit()
        .then(
          () => false,
          (e: unknown) => e instanceof ApiError && e.status === 401,
        );
      setChecking(false);
      if (refused) return setMessage({ text: t`GitLab didn't accept that token, so it wasn't saved. Check that you copied all of it.`, error: true });
    }
    try {
      await saveGitlabToken(token || undefined);
    } catch {
      return setMessage({ text: t`Couldn't save the token. Try again.`, error: true });
    }
    setDraft(token);
    setMessage({ text: token ? t`Saved. Requests to GitLab use your token now.` : t({ message: "Removed.", comment: "The GitLab token was removed" }) });
  }
  const left = limit?.remaining.toLocaleString(i18n.locale);
  const most = limit?.limit.toLocaleString(i18n.locale);
  return (
    <section className="settings-section gitlab-token" aria-label={t`GitLab token`}>
      <h3>
        <Trans>GitLab token</Trans>
      </h3>
      <p className="muted">
        <Trans>
          Apps you add from GitLab get their releases from GitLab itself, which allows fewer requests from this network without a token. Apps from the catalog
          don&apos;t need one.
        </Trans>
      </p>
      {limit && (
        <p className="muted" role="status">
          <Trans>
            {left} of {most} requests left this minute.
          </Trans>
        </p>
      )}
      <input
        type="password"
        aria-label={t`GitLab token`}
        placeholder="glpat-…"
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
          <Trans>Create a token on GitLab</Trans>
        </button>
        <button aria-expanded={guide} onClick={() => setGuide(!guide)}>
          {guide ? t`Hide the steps` : t`How to make one`}
        </button>
      </div>
      {guide && (
        <ol className="muted">
          <li>
            <Trans>Choose Create a token on GitLab, and sign in to GitLab.</Trans>
          </li>
          <li>
            <Trans>Choose when it expires.</Trans>
          </li>
          <li>
            <Trans>Leave the read_api scope ticked: public releases need nothing more.</Trans>
          </li>
          <li>
            <Trans comment="Generate token is the name of GitLab's button, as GitLab shows it in English">
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
