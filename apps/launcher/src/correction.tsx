/**
 * Telling maintainers something on an app's page is wrong or missing (Report
 * a problem, in its header), as on the website's app page: a topic and a few words, sent to the website's
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
import { fullDate, hasEasySetup } from "@quiverlauncher/ui";
import { useAccount, type OwnCorrection } from "./account";
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
import { Dialog } from "./library";
import { moveFocus } from "./spatial";
import { useLauncher } from "./store";
import { track } from "./telemetry";

// Put in the language when shown, not here, where the module loads before any is picked.
const TOPIC_LABELS: Record<CorrectionTopic, MessageDescriptor> = {
  quiver: msg`Quiver installing or launching it`,
  description: msg`Description or details`,
  downloads: msg`Releases or downloads`,
  gameFiles: msg`Easy setup`,
  games: msg`Linked games, their names or details`,
  links: msg`Links`,
  other: msg`Something else`,
};

/** How long after Report a problem a sign-in still opens its dialog: longer, and it was for something else. */
const SIGN_IN_WAIT = 5 * 60_000;

// Sent from the header's dialog: the Overview's line about it asks again.
const SENT = "quiver-correction-sent";

/**
 * The app page's Report a problem, beside its other actions: a dialog with the form. Signed out, it signs in first and
 * the dialog opens after.
 */
export function ReportProblem({ entry, installed, onSignIn }: { entry: Entry; installed?: string; onSignIn: () => void }) {
  const { user } = useAccount();
  const [open, setOpen] = useState(false);
  const [sent, setSent] = useState(false);
  // Asked for while signed out: opens once they've signed in, if that's soon. When it was asked, else 0.
  const [waiting, setWaiting] = useState(0);
  const button = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (!waiting || !user) return;
    setWaiting(0);
    // A sign-in given up on, then done much later for something else, opens nothing.
    if (Date.now() - waiting < SIGN_IN_WAIT) setOpen(true);
  }, [waiting, user]);
  // Another app's page in its place starts again.
  useEffect(() => (setOpen(false), setSent(false), setWaiting(0)), [entry.id]);
  const close = () => {
    setOpen(false);
    setSent(false);
    requestAnimationFrame(() => button.current && moveFocus(button.current));
  };
  return (
    <>
      <button
        ref={button}
        type="button"
        onClick={() => {
          track("correction_started", { signedIn: Boolean(user) });
          if (user) return setOpen(true);
          setWaiting(Date.now());
          onSignIn();
        }}
      >
        <Flag size={17} aria-hidden="true" /> <Trans comment="App page button: tell the maintainers something on the page is wrong or missing">Report a problem</Trans>
      </button>
      {open && user && (
        <Dialog label={t`Report a problem`} className="correction-dialog" onClose={close}>
          {sent ? (
            <ThanksForReporting onDone={close} />
          ) : (
            <CorrectionForm
              entry={entry}
              userId={user.id}
              installed={installed}
              onSent={() => {
                setSent(true);
                window.dispatchEvent(new Event(SENT));
              }}
              onClose={close}
            />
          )}
        </Dialog>
      )}
    </>
  );
}

function ThanksForReporting({ onDone }: { onDone: () => void }) {
  const done = useRef<HTMLButtonElement>(null);
  useEffect(() => void (done.current && moveFocus(done.current)), []);
  return (
    <div className="correction-thanks">
      <p className="correction-sent" role="status">
        <CircleCheck size={16} aria-hidden="true" /> <Trans>Thanks! A maintainer will take a look.</Trans>
      </p>
      <button ref={done} type="button" className="button" onClick={onDone}>
        <Trans comment="Closes the thanks after a problem was reported">Done</Trans>
      </button>
    </div>
  );
}

/**
 * Under an app's Project details, only once the player has reported a problem with it: where that stands, and the
 * maintainer's reply once decided. Seeing how it was decided here counts as seeing it.
 */
export function CorrectionStatusPanel({ entry }: { entry: Entry }) {
  const { user, ownCorrection, markCorrectionsSeen } = useAccount();
  const [latest, setLatest] = useState<OwnCorrection | null>(null);
  const [round, setRound] = useState(0);
  useEffect(() => {
    const again = () => setRound((r) => r + 1);
    window.addEventListener(SENT, again);
    return () => window.removeEventListener(SENT, again);
  }, []);
  useEffect(() => {
    if (!user) return setLatest(null);
    let live = true;
    ownCorrection(entry.id).then((c) => live && setLatest(c));
    return () => void (live = false);
  }, [user?.id, entry.id, round]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (latest?.unseen) void markCorrectionsSeen([latest.id]);
  }, [latest?.id, latest?.unseen]); // eslint-disable-line react-hooks/exhaustive-deps
  if (!latest) return null;
  return (
    <section className="panel content-panel correction-prompt" aria-label={t`Your problem report`}>
      <CorrectionStatus correction={latest} />
    </section>
  );
}

/** Where the player's last correction to the app stands, with the maintainer's reply once decided. */
function CorrectionStatus({ correction: c }: { correction: OwnCorrection }) {
  const sent = fullDate(c.createdAt);
  return (
    <div className={`correction-status ${c.status}`}>
      <p className={c.status === "open" ? "muted" : undefined}>
        {c.status === "open"
          ? t`You reported a problem on ${sent}. A maintainer will take a look.`
          : c.status === "done"
            ? t`The problem you reported on ${sent} was fixed. Thanks!`
            : t`A maintainer looked at the problem you reported on ${sent} and left the page as it is.`}
      </p>
      {c.reply && <blockquote className="correction-reply">{c.reply}</blockquote>}
    </div>
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
      aria-label={t`Report a problem`}
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
      <PanelFormHead title={t`Report a problem`} onClose={cancel} />
      <label className="field">
        <Trans>What&apos;s it about?</Trans>
        <select value={topic} onChange={(e) => setTopic(e.target.value as CorrectionTopic | "")}>
          <option value="">{t`Choose a topic (optional)`}</option>
          {CORRECTION_TOPICS.filter((id) => id !== "gameFiles" || hasEasySetup(entry)).map((id) => (
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
