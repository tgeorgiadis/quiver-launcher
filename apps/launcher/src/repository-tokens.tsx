/**
 * Settings: tokens for the repository hosts other than github.com and
 * gitlab.com, which have their own sections. A token is kept in the
 * keychain and sent only to the host it belongs to.
 */
import { useState } from "react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { defaultHost, providerById, providerForHost } from "@quiverlauncher/api";
import { native } from "./native";
import { useLauncher } from "./store";

export function RepositoryTokens() {
  const { tokens, saveToken } = useLauncher();
  const hosts = Object.keys(tokens)
    .filter((host) => host !== "github.com" && host !== "gitlab.com")
    .sort();
  const [host, setHost] = useState("");
  const [draft, setDraft] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function add() {
    const clean = host
      .trim()
      .toLowerCase()
      .replace(/^(?:https?:\/\/)?(?:www\.)?/i, "")
      .split("/")[0];
    if (!clean || !draft.trim()) return;
    setSaving(true);
    setMessage(null);
    try {
      await saveToken(clean, draft.trim());
      setHost("");
      setDraft("");
      setMessage(t`Saved. Requests to ${clean} use your token now.`);
    } catch {
      setMessage(t`Couldn't save the token. Try again.`);
    } finally {
      setSaving(false);
    }
  }

  return (
    <section className="settings-section" aria-label={t`Other repository tokens`}>
      <h3>
        <Trans>Other repository tokens</Trans>
      </h3>
      <p className="muted">
        <Trans>
          Apps you add from a self-hosted forge, Codeberg, Bitbucket or a release feed use the token you save for that host. A token is kept in this
          computer&apos;s keychain and sent only to the host it belongs to.
        </Trans>
      </p>
      {hosts.map((h) => (
        <HostToken key={h} host={h} />
      ))}
      <div className="row">
        <input aria-label={t`Host`} placeholder={t`git.example.com`} value={host} onChange={(e) => setHost(e.target.value)} spellCheck={false} />
        <input
          type="password"
          aria-label={t`Token`}
          placeholder={t`Token`}
          autoComplete="off"
          spellCheck={false}
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
        />
        <button className="primary" disabled={saving || !host.trim() || !draft.trim()} onClick={() => void add()}>
          {saving ? t`Saving…` : t`Save token`}
        </button>
      </div>
      {message && (
        <p className="muted" role="status">
          {message}
        </p>
      )}
    </section>
  );
}

function HostToken({ host }: { host: string }) {
  const { tokens, saveToken, library } = useLauncher();
  const saved = tokens[host] ?? "";
  const [draft, setDraft] = useState(saved);
  const [message, setMessage] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  // A host the player already added an app from knows its provider; otherwise, a known host does.
  const added = library.find((i) => i.custom && (i.custom.host ?? defaultHost(i.custom.provider)) === host)?.custom?.provider;
  const provider = providerForHost(host) ?? (added ? providerById(added) : undefined);
  const help = provider?.token;

  async function save(token: string) {
    setSaving(true);
    setMessage(null);
    try {
      await saveToken(host, token || undefined);
      setDraft(token);
      setMessage(token ? t`Saved.` : t`Removed.`);
    } catch {
      setMessage(t`Couldn't save the token. Try again.`);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="row">
      <strong>{host}</strong>
      {provider && <span className="muted">{provider.name}</span>}
      <input
        type="password"
        aria-label={t`Token for ${host}`}
        autoComplete="off"
        spellCheck={false}
        value={draft}
        onChange={(e) => setDraft(e.target.value)}
      />
      <button className="primary" disabled={saving || draft.trim() === saved} onClick={() => void save(draft.trim())}>
        {saving ? t`Checking…` : t`Save token`}
      </button>
      {saved && (
        <button disabled={saving} onClick={() => void save("")}>
          <Trans>Remove token</Trans>
        </button>
      )}
      {help && provider && (
        <button onClick={() => void native.openUrl(help.createUrl({ provider: provider.id, host, repository: "" }))}>
          <Trans>Create a token</Trans>
        </button>
      )}
      {message && (
        <span className="muted" role="status">
          {message}
        </span>
      )}
    </div>
  );
}
