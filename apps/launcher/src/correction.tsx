/**
 * Telling maintainers something on an app's page is wrong or missing, as on
 * the website's app page: a topic and a few words, sent to the website's
 * Corrections queue (corrections.submit) with this launcher's version and
 * system. Signed in only, so the site can hold each account to a rate.
 */
import { useEffect, useRef, useState } from "react";
import { CircleCheck, Flag } from "lucide-react";
import { msg, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { MessageDescriptor } from "@lingui/core";
import { i18n } from "@quiverlauncher/i18n";
import type { Entry } from "@quiverlauncher/api";
import { useAccount } from "./account";
import {
  CORRECTION_MAX,
  CORRECTION_TOPICS,
  LAUNCHER_TOPICS,
  clearCorrectionDraft,
  correctionClient,
  readCorrectionDraft,
  saveCorrectionDraft,
  type CorrectionTopic,
} from "./corrections";
import { PanelFormHead } from "./detail";
import { CharacterCount } from "./feedback";
import { moveFocus } from "./spatial";
import { useLauncher } from "./store";
import { track } from "./telemetry";

// Put in the language when shown, not here, where the module loads before any is picked.
const TOPIC_LABELS: Record<CorrectionTopic, MessageDescriptor> = {
  install: msg`Installing it`,
  launch: msg`Launching it`,
  gameFiles: msg`Finding my game files`,
  description: msg`Description or details`,
  downloads: msg`Releases or downloads`,
  setup: msg`Setup instructions`,
  games: msg`Games, their names or consoles`,
  links: msg`Links`,
  other: msg`Something else`,
};

/**
 * Under an app's Project details: a line asking whether anything's wrong, and the form once asked. Signed out, the
 * button signs in first and the form opens after.
 */
export function SuggestCorrection({ entry, installed, onSignIn }: { entry: Entry; installed?: string; onSignIn: () => void }) {
  const { user } = useAccount();
  const [open, setOpen] = useState(() => Boolean(user && readCorrectionDraft(user.id, entry.id)));
  const [sent, setSent] = useState(false);
  // Asked for while signed out: opens once they've signed in.
  const [waiting, setWaiting] = useState(false);
  const toggle = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (waiting && user) (setWaiting(false), setOpen(true));
  }, [waiting, user]);
  // Another app's page in its place starts again.
  useEffect(() => {
    setSent(false);
    setOpen(Boolean(user && readCorrectionDraft(user.id, entry.id)));
  }, [entry.id]); // eslint-disable-line react-hooks/exhaustive-deps
  const close = () => {
    setOpen(false);
    requestAnimationFrame(() => toggle.current && moveFocus(toggle.current));
  };
  if (open && user)
    return (
      <CorrectionForm
        entry={entry}
        userId={user.id}
        installed={installed}
        onSent={() => {
          setSent(true);
          close();
        }}
        onClose={close}
      />
    );
  return (
    <section className="panel content-panel correction-prompt" aria-label={t`Suggest a correction`}>
      {sent ? (
        <p className="correction-sent" role="status">
          <CircleCheck size={16} aria-hidden="true" /> <Trans>Thanks! A maintainer will take a look.</Trans>
        </p>
      ) : (
        <p className="muted">
          <Trans>Something wrong or missing on this page?</Trans>
        </p>
      )}
      <button
        ref={toggle}
        type="button"
        className="link-button"
        onClick={() => {
          track("correction_started", { signedIn: Boolean(user) });
          if (user) return setOpen(true);
          setWaiting(true);
          onSignIn();
        }}
      >
        <Flag size={14} aria-hidden="true" /> {user ? t`Suggest a correction` : t`Sign in to suggest a correction`}
      </button>
    </section>
  );
}

function CorrectionForm({
  entry,
  userId,
  installed,
  onSent,
  onClose,
}: {
  entry: Entry;
  userId: string;
  installed?: string;
  onSent: () => void;
  onClose: () => void;
}) {
  const { config } = useLauncher();
  const { suggestCorrection } = useAccount();
  const [draft] = useState(() => readCorrectionDraft(userId, entry.id));
  const [topic, setTopic] = useState<CorrectionTopic | "">(draft?.topic ?? "");
  const [body, setBody] = useState(draft?.body ?? "");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const form = useRef<HTMLFormElement>(null);
  useEffect(() => saveCorrectionDraft(userId, entry.id, { topic, body }), [userId, entry.id, topic, body]);
  // The focus into the form as it opens, for a controller or the keys.
  useEffect(() => {
    const first = form.current?.querySelector("select");
    if (first) moveFocus(first);
  }, []);
  // Escape or Back closes it, ahead of the page going back. Not from under a dialog or menu.
  useEffect(() => {
    const key = (e: KeyboardEvent) => {
      if (e.key !== "Escape" || !form.current?.contains(document.activeElement) || document.querySelector('[role="dialog"], [role="menu"], .menu.floating'))
        return;
      e.stopImmediatePropagation();
      onClose();
    };
    window.addEventListener("keydown", key, true);
    return () => window.removeEventListener("keydown", key, true);
  }, [onClose]);
  const cancel = () => {
    clearCorrectionDraft(userId, entry.id);
    onClose();
  };
  return (
    <form
      ref={form}
      className="panel-form correction-form"
      aria-label={t`Suggest a correction`}
      onSubmit={(e) => {
        e.preventDefault();
        const text = body.trim();
        if (text.length < 5) return setMessage(t`Say what's wrong or missing.`);
        setBusy(true);
        setMessage("");
        suggestCorrection({
          entryId: entry.id,
          body: text,
          ...(topic ? { topic } : {}),
          client: correctionClient(config, installed),
        }).then(
          () => {
            track("correction_sent", { topic: topic || "none" });
            clearCorrectionDraft(userId, entry.id);
            onSent();
          },
          (error) => {
            setMessage(error instanceof Error ? error.message : String(error));
            setBusy(false);
          },
        );
      }}
    >
      <PanelFormHead title={t`Suggest a correction`} onClose={cancel} />
      <label className="field">
        <Trans>What&apos;s it about?</Trans>
        <select value={topic} onChange={(e) => setTopic(e.target.value as CorrectionTopic | "")}>
          <option value="">{t`Choose a topic (optional)`}</option>
          {CORRECTION_TOPICS.map((id) => (
            <option key={id} value={id}>
              {i18n._(TOPIC_LABELS[id])}
            </option>
          ))}
        </select>
      </label>
      {topic && LAUNCHER_TOPICS.includes(topic) && (
        <p className="muted correction-hint">
          <Trans>For what the listing gets wrong, like the wrong program or game file. To say how it ran, use Player feedback.</Trans>
        </p>
      )}
      <label className="field">
        <Trans>What&apos;s wrong or missing?</Trans>
        <textarea
          rows={4}
          maxLength={CORRECTION_MAX}
          value={body}
          aria-describedby="correction-count"
          placeholder={t`For example: it starts the level editor instead of the game, or it needs the US version of the game.`}
          onChange={(e) => setBody(e.target.value)}
        />
      </label>
      <CharacterCount id="correction-count" length={body.length} limit={CORRECTION_MAX} />
      <p className="muted correction-note">
        <Trans>Sent with your account name, this launcher&apos;s version and your system, so maintainers can check it.</Trans>
      </p>
      <div className="review-editor-actions">
        <button className="button" disabled={busy}>
          {busy ? t`Sending…` : t({ message: "Send", comment: "Button: send a correction to the maintainers" })}
        </button>
        <button type="button" className="button secondary" disabled={busy} onClick={cancel}>
          <Trans>Cancel</Trans>
        </button>
      </div>
      {message && <p role="status">{message}</p>}
    </form>
  );
}
