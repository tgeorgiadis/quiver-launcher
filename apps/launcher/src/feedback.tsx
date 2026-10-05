/**
 * What players say about how an app runs, as on the website's Player feedback
 * tab: the list, and sharing your own (signed in; anyone can start, and
 * signing in comes first).
 */
import { useEffect, useState } from "react";
import { CircleCheck, CircleX, MessageSquare, Pencil, TriangleAlert } from "lucide-react";
import type { Entry, Feedback, Release } from "@quiverlauncher/api";
import { OS_NAMES, fullDate } from "@quiverlauncher/ui";
import { useLauncher } from "./store";
import { useAccount } from "./account";

export type Result = Feedback["result"];
/** What the player asked to do: give feedback with this answer, or edit what they gave. */
export type Intent = Result | "edit";

const RESULTS: Record<Result, { label: string; tone: string; Icon: typeof CircleCheck }> = {
  runs: { label: "Runs well", tone: "positive", Icon: CircleCheck },
  issues: { label: "Runs with issues", tone: "caution", Icon: TriangleAlert },
  broken: { label: "Doesn't run", tone: "negative", Icon: CircleX },
};

export function ResultBadge({ result }: { result: Result }) {
  const { label, tone, Icon } = RESULTS[result];
  return (
    <span className={`result-badge ${tone}`}>
      <Icon size={15} /> {label}
    </span>
  );
}

/** Why others can't see the player's own feedback, if they can't. */
const ownStatus = (own: Feedback) =>
  own.moderatorHidden
    ? "A moderator hid your feedback, so others can't see it."
    : own.underReview
      ? "Your feedback is waiting for a moderator to review it before others can see it."
      : null;

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
export function ReportPrompt({ count, own, onShare }: { count: number; own: Feedback | null | undefined; onShare: (intent: Intent) => void }) {
  if (own)
    return (
      <section className="panel report-prompt done">
        <p>
          <CircleCheck size={16} aria-hidden /> {ownStatus(own) ?? "Thanks for sharing how it ran."}
        </p>
        <button type="button" className="text-button" onClick={() => onShare("edit")}>
          Update your feedback
        </button>
      </section>
    );
  return (
    <section className="panel report-prompt" aria-label="How did it run">
      <h2>Played it? How did it run?</h2>
      <p className="muted">{count ? "Your feedback helps the next player know what to expect." : "Nobody has said yet. Be the first."}</p>
      <div className="report-prompt-answers">
        {(["runs", "issues", "broken"] as const).map((result) => (
          <button key={result} type="button" className={`report-answer ${RESULTS[result].tone}`} onClick={() => onShare(result)}>
            {{ runs: "Runs well", issues: "Has issues", broken: "Doesn't run" }[result]}
          </button>
        ))}
      </div>
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
  // The form, with the answer picked to open it, if any.
  const [form, setForm] = useState<{ picked?: Result } | null>(null);
  function load(cursor: string | null) {
    client.reviews(entry.slug, cursor).then(
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
    setForm({ picked: intent === "edit" ? undefined : intent });
    onIntentDone();
  }, [user, intent]); // eslint-disable-line react-hooks/exhaustive-deps
  const empty = list !== undefined && list.length === 0 && !next;
  return (
    <section className="feedback" aria-label="Player feedback">
      {user && form ? (
        <ReviewEditor
          entry={entry}
          existing={own}
          picked={form.picked}
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
            <CircleCheck size={16} /> {ownStatus(own) ?? "You've shared how it ran. Has anything changed since?"}
          </p>
          <button type="button" className="button small secondary" onClick={() => setForm({})}>
            <Pencil size={13} /> Update your notes
          </button>
        </div>
      ) : (
        <div className="share-prompt panel">
          <MessageSquare size={24} />
          <h3>{empty ? "Nobody has shared how it runs yet" : "Your experience helps the next person"}</h3>
          <p>{!user ? "Sign in to share how it runs for you." : empty ? "Be the first to say how it went." : "Tell others how it ran after following the setup."}</p>
          <button type="button" className="button" onClick={() => onShare("edit")}>
            <MessageSquare size={15} /> Share how it ran
          </button>
        </div>
      )}
      {list === undefined ? (
        <div className="more" role="status" aria-label="Loading player notes">
          <span className="spinner" />
        </div>
      ) : (
        list.map((review) => (
          <article key={review.id} className="review panel">
            <div className="review-header">
              <div className="review-author">
                <span className="avatar">{review.author.slice(0, 1).toUpperCase()}</span>
                <div>
                  <strong>
                    {review.author}
                    {user?.id === review.userId && <span className="you-tag">You</span>}
                  </strong>
                  <small>
                    {[fullDate(review.createdAt), review.platform && OS_NAMES[review.platform], review.version && `tested on ${review.version}`]
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
          Load more
        </button>
      )}
    </section>
  );
}

const CHOICES: [Result, string, string][] = [
  ["runs", "It worked well", "I followed the setup on this page and had no problems."],
  ["issues", "It ran, but I had issues", "Crashes, poor performance, broken controls or features, or I had to do extra setup this page didn't mention."],
  ["broken", "It didn't run", "It wouldn't install, open, or start."],
];

/**
 * The website's form: how it went, where, which release, and a note. Saving
 * again replaces what the player said before. The app needn't be installed.
 */
function ReviewEditor({
  entry,
  existing,
  picked,
  releases,
  onCancel,
  onSaved,
}: {
  entry: Entry;
  existing: Feedback | null | undefined;
  picked?: Result;
  releases: Release[];
  onCancel: () => void;
  onSaved: () => void;
}) {
  const { config, installs, catalog } = useLauncher();
  const { review } = useAccount();
  const install = installs[entry.id];
  // A release the site withdrew can't be named.
  const installed = install?.releaseId && !catalog[entry.id]?.withdrawn?.some((w) => w.version === install.version) ? install.releaseId : undefined;
  const [result, setResult] = useState<Result>(picked ?? "runs");
  const [platform, setPlatform] = useState(config.os === "unknown" ? "" : config.os);
  const [releaseId, setReleaseId] = useState(installed && releases.some((r) => r.id === installed) ? installed : "");
  const [body, setBody] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  // What they said before, when it arrives.
  useEffect(() => {
    if (!existing) return;
    setResult(picked ?? existing.result);
    setBody(existing.body);
    if (existing.platform && existing.platform !== "unknown") setPlatform(existing.platform);
    setReleaseId(existing.entryReleaseId ?? "");
  }, [existing]); // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <form
      className="review-editor panel"
      onSubmit={(e) => {
        e.preventDefault();
        if (!platform) return setMessage("Choose the platform you tried.");
        setBusy(true);
        setMessage("");
        review({
          entryId: entry.id,
          result,
          body: body.trim(),
          platform: platform as Exclude<typeof config.os, "unknown">,
          ...(releaseId ? { entryReleaseId: releaseId } : {}),
        }).then(onSaved, (error) => {
          setMessage(error instanceof Error ? error.message : String(error));
          setBusy(false);
        });
      }}
    >
      <h3>{existing ? "Update your notes" : "How did it go?"}</h3>
      <p className="muted">Tell others how it worked for you after following the setup on this page.</p>
      <div className="result-options">
        {CHOICES.map(([value, title, text]) => (
          <button
            key={value}
            type="button"
            aria-pressed={result === value}
            className={result === value ? `selected ${RESULTS[value].tone}` : ""}
            onClick={() => setResult(value)}
          >
            <strong>{title}</strong>
            <small>{text}</small>
          </button>
        ))}
      </div>
      <div className="form-row">
        <label className="field">
          Tested on
          <select required value={platform} onChange={(e) => setPlatform(e.target.value as typeof platform)}>
            <option value="">Choose a platform</option>
            {Object.entries(OS_NAMES).map(([id, name]) => (
              <option key={id} value={id}>
                {name}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          Tested release
          <select value={releaseId} onChange={(e) => setReleaseId(e.target.value)}>
            <option value="">Not sure</option>
            {releases.map((r) => (
              <option key={r.id} value={r.id}>
                {r.version}
              </option>
            ))}
          </select>
        </label>
      </div>
      <label className="field">
        Note
        <textarea
          maxLength={500}
          rows={4}
          value={body}
          placeholder="What worked well, and what didn't? Did you do anything to get it running or fix a problem?"
          onChange={(e) => setBody(e.target.value)}
        />
      </label>
      <div className="review-editor-actions">
        <button className="button" disabled={busy}>
          {busy ? "Saving…" : existing ? "Update notes" : "Share"}
        </button>
        <button type="button" className="button secondary" disabled={busy} onClick={onCancel}>
          Cancel
        </button>
      </div>
      {message && <p role="status">{message}</p>}
    </form>
  );
}
