/**
 * What players say about how an app runs, as on the website's Player feedback
 * tab: the list, and sharing your own (signed in; anyone can start, and
 * signing in comes first).
 */
import { useEffect, useRef, useState } from "react";
import { ChevronDown, CircleCheck, CircleX, MessageSquare, Pencil, TriangleAlert } from "lucide-react";
import { msg, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { MessageDescriptor } from "@lingui/core";
import { i18n } from "@quiverlauncher/i18n";
import type { Entry, Feedback, Release } from "@quiverlauncher/api";
import { OS_NAMES, Score, fullDate } from "@quiverlauncher/ui";
import { useLauncher } from "./store";
import { useAccount } from "./account";
import { NOTE_LIMIT, clearDraft, readDraft, saveDraft } from "./reviewDraft";
import { moveFocus } from "./spatial";

export type Result = Feedback["result"];
/** What the player asked to do: give feedback with this answer, or edit what they gave. */
export type Intent = Result | "edit";

// The labels put in the language when shown, not here, where the module loads before any is picked.
const RESULTS: Record<Result, { label: MessageDescriptor; tone: string; Icon: typeof CircleCheck }> = {
  runs: { label: msg`Runs well`, tone: "positive", Icon: CircleCheck },
  issues: { label: msg`Runs with issues`, tone: "caution", Icon: TriangleAlert },
  broken: { label: msg`Doesn't run`, tone: "negative", Icon: CircleX },
};

export function ResultBadge({ result }: { result: Result }) {
  const { label, tone, Icon } = RESULTS[result];
  return (
    <span className={`result-badge ${tone}`}>
      <Icon size={15} /> {i18n._(label)}
    </span>
  );
}

/** Why others can't see the player's own feedback, if they can't. */
const ownStatus = (own: Feedback) =>
  own.moderatorHidden
    ? t`A moderator hid your feedback, so others can't see it.`
    : own.underReview
      ? t`Your feedback is waiting for a moderator to review it before others can see it.`
      : null;

/** The release a player tried, among the facts under their name. */
const testedOn = (version: string) => t({ message: `tested on ${version}`, comment: "Under a player's name on their feedback, after the date and platform" });

/** The player's own feedback on an app: undefined while loading, null signed out or none. */
export function useOwnFeedback(entry: Entry, on: boolean) {
  const { user, ownReview } = useAccount();
  const [own, setOwn] = useState<Feedback | null | undefined>();
  const [round, setRound] = useState(0);
  useEffect(() => {
    if (!on || !user) {
      setOwn(null);
      return;
    }
    let live = true;
    ownReview(entry.id).then(
      (r) => live && setOwn(r),
      () => live && setOwn(null),
    );
    return () => void (live = false);
  }, [entry.id, user?.id, on, round]); // eslint-disable-line react-hooks/exhaustive-deps
  return { own, refresh: () => setRound((r) => r + 1) };
}

/** Beside the other tabs, as on the website: one tap to say how it ran. */
export function ReportPrompt({ entry, count, own, onShare }: { entry: Entry; count: number; own: Feedback | null | undefined; onShare: (intent: Intent) => void }) {
  const { user } = useAccount();
  if (user && readDraft(user.id, entry.id))
    return (
      <section className="panel report-prompt done draft">
        <p>
          <Pencil size={16} aria-hidden /> <Trans>Your feedback isn't shared yet. What you wrote is still here.</Trans>
        </p>
        <button type="button" className="text-button" onClick={() => onShare("edit")}>
          <Trans>Finish your feedback</Trans>
        </button>
      </section>
    );
  if (own)
    return (
      <section className="panel report-prompt done">
        <p>
          <CircleCheck size={16} aria-hidden /> {ownStatus(own) ?? t`Thanks for sharing how it ran.`}
        </p>
        <button type="button" className="text-button" onClick={() => onShare("edit")}>
          <Trans>Update your feedback</Trans>
        </button>
      </section>
    );
  return (
    <section className="panel report-prompt" aria-label={t`How did it run`}>
      <h2>
        <Trans>Played it? How did it run?</Trans>
      </h2>
      <p className="muted">{count ? t`Your feedback helps the next player know what to expect.` : t`Nobody has said yet. Be the first.`}</p>
      <div className="report-prompt-answers">
        {(["runs", "issues", "broken"] as const).map((result) => (
          <button key={result} type="button" className={`report-answer ${RESULTS[result].tone}`} onClick={() => onShare(result)}>
            {{ runs: t`Runs well`, issues: t`Has issues`, broken: t`Doesn't run` }[result]}
          </button>
        ))}
      </div>
    </section>
  );
}

/**
 * Beside an app's page, on every tab: what players said of how it runs (all of it is on the Player feedback tab under
 * it); opened, the player's own say (ReportPrompt), which closes again once they've answered, the tab's form taking it from there.
 * Closed to start with, so the page's top stays simple, unless the player left feedback unfinished, which it shows to
 * come back to.
 */
export function FeedbackBox({
  entry,
  count,
  own,
  onShare,
}: {
  entry: Entry;
  count: number;
  own: Feedback | null | undefined;
  onShare: (intent: Intent) => void;
}) {
  const { user } = useAccount();
  const [open, setOpen] = useState(() => Boolean(user && readDraft(user.id, entry.id)));
  const name = entry.projectName;
  return (
    <section className="panel feedback-box" data-open={open || undefined} aria-label={t`What players said`}>
      <div className="feedback-box-head">
        <Score runs={entry.recommended} issues={entry.reportIssues} broken={entry.reportBroken} />
        <button
          type="button"
          className="feedback-box-toggle"
          aria-expanded={open}
          aria-label={open ? t`Hide your feedback for ${name}` : t`Say how ${name} ran for you`}
          title={open ? t`Hide` : t`Say how it ran for you`}
          onClick={() => setOpen(!open)}
        >
          <ChevronDown size={18} aria-hidden="true" />
        </button>
      </div>
      {open && <ReportPrompt entry={entry} count={count} own={own} onShare={(intent) => (setOpen(false), onShare(intent))} />}
    </section>
  );
}

export function FeedbackTab({
  entry,
  releases,
  own,
  onSaved,
  intent,
  onIntentDone,
  onShare,
}: {
  entry: Entry;
  /** The releases loaded so far, to say which one was tested. */
  releases: Release[] | undefined;
  own: Feedback | null | undefined;
  onSaved: () => void;
  intent: Intent | null;
  onIntentDone: () => void;
  onShare: (intent: Intent) => void;
}) {
  const { client } = useLauncher();
  const { user } = useAccount();
  const [list, setList] = useState<Feedback[] | undefined>();
  const [next, setNext] = useState<string | null>(null);
  const [round, setRound] = useState(0);
  // The form, with the answer picked to open it, if any; `asked` counts the presses that asked for it (an answer in the
  // box at the page's top, a button here), each taking the focus to its answer.
  const [form, setForm] = useState<{ picked?: Result; asked: number } | null>(null);
  const ask = (picked?: Result) => setForm((f) => ({ picked, asked: (f?.asked ?? 0) + 1 }));
  function load(cursor: string | null) {
    // Ten at a time: the first page is the one address the site keeps for
    // every launcher, so it rarely reaches the site's database.
    client.reviews(entry.slug, cursor, 10).then(
      (page) => {
        setList((l) => (cursor ? [...(l ?? []), ...page.items] : page.items));
        setNext(page.isDone ? null : page.nextCursor);
      },
      () => setList((l) => l ?? []),
    );
  }
  useEffect(() => {
    load(null);
  }, [client, entry.slug, round]); // eslint-disable-line react-hooks/exhaustive-deps
  // Asked for before signing in, or from elsewhere on the page: the form opens once signed in.
  useEffect(() => {
    if (!user || !intent) return;
    ask(intent === "edit" ? undefined : intent);
    onIntentDone();
  }, [user, intent]); // eslint-disable-line react-hooks/exhaustive-deps
  // Unsent feedback from before (another tab, or a restart) opens the form again, leaving the focus be.
  useEffect(() => {
    if (user && readDraft(user.id, entry.id)) setForm((f) => f ?? { asked: 0 });
  }, [user?.id, entry.id]); // eslint-disable-line react-hooks/exhaustive-deps
  const empty = list !== undefined && list.length === 0 && !next;
  return (
    <section className="feedback" aria-label={t`Player feedback`}>
      {user && form ? (
        <ReviewEditor
          userId={user.id}
          entry={entry}
          existing={own}
          picked={form.picked}
          asked={form.asked}
          releases={releases ?? []}
          onCancel={() => setForm(null)}
          onSaved={() => {
            setForm(null);
            onSaved();
            setRound((r) => r + 1);
          }}
        />
      ) : user && own ? (
        <div className="share-prompt compact panel">
          <p>
            <CircleCheck size={16} /> {ownStatus(own) ?? t`You've shared how it ran. Has anything changed since?`}
          </p>
          <button type="button" className="button small secondary" onClick={() => ask()}>
            <Pencil size={13} /> <Trans>Update your notes</Trans>
          </button>
        </div>
      ) : (
        <div className="share-prompt panel">
          <MessageSquare size={24} />
          <h3>{empty ? t`Nobody has shared how it runs yet` : t`Your experience helps the next person`}</h3>
          <p>{!user ? t`Sign in to share how it runs for you.` : empty ? t`Be the first to say how it went.` : t`Tell others how it ran after following the setup.`}</p>
          <button type="button" className="button" onClick={() => onShare("edit")}>
            <MessageSquare size={15} /> <Trans>Share how it ran</Trans>
          </button>
        </div>
      )}
      {list === undefined ? (
        <div className="more" role="status" aria-label={t`Loading player notes`}>
          <span className="spinner" />
        </div>
      ) : (
        list.map((review) => (
          // A stop of its own, for a controller's d-pad (and Tab) to read down the list, which has no buttons.
          <article key={review.id} className="review panel" tabIndex={0}>
            <div className="review-header">
              <div className="review-author">
                <span className="avatar">{review.author.slice(0, 1).toUpperCase()}</span>
                <div>
                  <strong>
                    {review.author}
                    {user?.id === review.userId && <span className="you-tag">
                        <Trans comment="Tag by the player's own name on their feedback">You</Trans>
                      </span>}
                  </strong>
                  <small>
                    {[fullDate(review.createdAt), review.platform && OS_NAMES[review.platform], review.version && testedOn(review.version)]
                      .filter(Boolean)
                      .join(" · ")}
                  </small>
                </div>
              </div>
              <ResultBadge result={review.result} />
            </div>
            {review.body && <p className="review-body">{review.body}</p>}
          </article>
        ))
      )}
      {next && (
        <button type="button" className="button secondary" onClick={() => load(next)}>
          <Trans>Load more</Trans>
        </button>
      )}
    </section>
  );
}

// Put in the language when shown, as RESULTS are.
const CHOICES: [Result, MessageDescriptor, MessageDescriptor][] = [
  ["runs", msg`It worked well`, msg`I followed the setup on this page and had no problems.`],
  ["issues", msg`It ran, but I had issues`, msg`Crashes, poor performance, broken controls or features, or I had to do extra setup this page didn't mention.`],
  ["broken", msg`It didn't run`, msg`It wouldn't install, open, or start.`],
];

/**
 * The website's form: how it went, where, which release, and a note. Saving
 * again replaces what the player said before. The app needn't be installed.
 */
function ReviewEditor({
  userId,
  entry,
  existing,
  picked,
  asked,
  releases,
  onCancel,
  onSaved,
}: {
  userId: string;
  entry: Entry;
  existing: Feedback | null | undefined;
  picked?: Result;
  /** More with each press that asks for the form (FeedbackTab), which takes the focus to its answer. */
  asked: number;
  releases: Release[];
  onCancel: () => void;
  onSaved: () => void;
}) {
  const { config, installs, catalog } = useLauncher();
  const { review } = useAccount();
  const install = installs[entry.id];
  // A release the site withdrew can't be named.
  const installed = install?.releaseId && !catalog[entry.id]?.withdrawn?.some((w) => w.version === install.version) ? install.releaseId : undefined;
  // Unsent changes from earlier (another tab, or before a restart) come back.
  const [draft] = useState(() => readDraft(userId, entry.id));
  // What they said before from the start, when it's here, so the focus goes to that answer.
  const [result, setResult] = useState<Result>(picked ?? draft?.result ?? existing?.result ?? "runs");
  const [platform, setPlatform] = useState(draft?.platform ?? (config.os === "unknown" ? "" : config.os));
  const [releaseId, setReleaseId] = useState(draft?.releaseId ?? (installed && releases.some((r) => r.id === installed) ? installed : ""));
  const [body, setBody] = useState(draft?.body ?? "");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  // Changed here since opening (or kept from before): what's kept if they leave.
  const [changed, setChanged] = useState(!!draft);
  // What they said before, when it arrives, unless they've changed it since.
  useEffect(() => {
    if (!existing || changed) return;
    setResult(picked ?? existing.result);
    setBody(existing.body);
    if (existing.platform && existing.platform !== "unknown") setPlatform(existing.platform);
    setReleaseId(existing.entryReleaseId ?? "");
  }, [existing]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (changed) saveDraft(userId, entry.id, { result, platform, releaseId, body });
  }, [changed, userId, entry.id, result, platform, releaseId, body]);
  // Asked for by a press: its answer, if it named one (picked again in the box while the form's open, too), and the
  // focus on the answer, eased into view.
  const answers = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!asked) return;
    if (picked) setResult(picked);
    const answer = answers.current?.children[CHOICES.findIndex(([value]) => value === (picked ?? result))];
    if (answer instanceof HTMLElement) moveFocus(answer);
  }, [asked]); // eslint-disable-line react-hooks/exhaustive-deps
  const edit =
    <T,>(set: (value: T) => void) =>
    (value: T) => {
      setChanged(true);
      set(value);
    };
  return (
    <form
      className="review-editor panel"
      onSubmit={(e) => {
        e.preventDefault();
        if (!platform) return setMessage(t`Choose the platform you tried.`);
        setBusy(true);
        setMessage("");
        review({
          entryId: entry.id,
          result,
          body: body.trim(),
          platform: platform as Exclude<typeof config.os, "unknown">,
          ...(releaseId ? { entryReleaseId: releaseId } : {}),
        }).then(() => {
          clearDraft(userId, entry.id);
          onSaved();
        }, (error) => {
          setMessage(error instanceof Error ? error.message : String(error));
          setBusy(false);
        });
      }}
    >
      <h3>{existing ? t`Update your notes` : t`How did it go?`}</h3>
      <p className="muted">
        <Trans>Tell others how it worked for you after following the setup on this page.</Trans>
      </p>
      <div className="result-options" ref={answers}>
        {CHOICES.map(([value, title, text]) => (
          <button
            key={value}
            type="button"
            aria-pressed={result === value}
            className={result === value ? `selected ${RESULTS[value].tone}` : ""}
            onClick={() => edit(setResult)(value)}
          >
            <strong>{i18n._(title)}</strong>
            <small>{i18n._(text)}</small>
          </button>
        ))}
      </div>
      <div className="form-row">
        <label className="field">
          <Trans comment="The platform the player tried the app on">Tested on</Trans>
          <select required value={platform} onChange={(e) => edit(setPlatform)(e.target.value as typeof platform)}>
            <option value="">{t`Choose a platform`}</option>
            {Object.entries(OS_NAMES).map(([id, name]) => (
              <option key={id} value={id}>
                {name}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <Trans>Tested release</Trans>
          <select value={releaseId} onChange={(e) => edit(setReleaseId)(e.target.value)}>
            <option value="">{t({ message: "Not sure", comment: "Which release the player tested" })}</option>
            {releases.map((r) => (
              <option key={r.id} value={r.id}>
                {r.version}
              </option>
            ))}
          </select>
        </label>
      </div>
      <label className="field">
        <Trans comment="The player's note about how the app ran">Note</Trans>
        <textarea
          maxLength={NOTE_LIMIT}
          rows={4}
          value={body}
          aria-describedby="review-note-count"
          placeholder={t`What worked well, and what didn't? Did you do anything to get it running or fix a problem?`}
          onChange={(e) => edit(setBody)(e.target.value)}
        />
      </label>
      <CharacterCount id="review-note-count" length={body.length} limit={NOTE_LIMIT} />
      <div className="review-editor-actions">
        <button className="button" disabled={busy}>
          {busy ? t`Saving…` : existing ? t`Update notes` : t({ message: "Share", comment: "Button: share the player's feedback" })}
        </button>
        <button
          type="button"
          className="button secondary"
          disabled={busy}
          onClick={() => {
            clearDraft(userId, entry.id);
            onCancel();
          }}
        >
          <Trans>Cancel</Trans>
        </button>
      </div>
      {message && <p role="status">{message}</p>}
    </form>
  );
}

/** "120/500" under the note, and a clear message once it's full, so typing never just stops. */
function CharacterCount({ id, length, limit }: { id: string; length: number; limit: number }) {
  const full = length >= limit;
  return (
    <div id={id} className={`character-count${full ? " full" : ""}`}>
      <span aria-live="polite">{full ? t`You've reached the ${limit}-character limit.` : ""}</span>
      <span>
        {length}/{limit}
      </span>
    </div>
  );
}
