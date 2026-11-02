/**
 * Announcements on screen (announcements.ts): a button in the top bar with
 * how many are waiting, its list, and the newest in a banner above the
 * library or Browse. Both layouts get the same, and a controller reaches the
 * button by moving up into the top bar.
 */
import { useEffect, useState } from "react";
import { Megaphone, X } from "lucide-react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { Announcement } from "@quiverlauncher/api";
import { useLauncher } from "./store";
import { dayLabel } from "./sidebar";
import { track, trackScreen } from "./telemetry";
import { bannerAnnouncement, keptDismissals, sorted, useAnnouncementList } from "./announcements";
import { closesOutside } from "./overlay";

/** Dismissing and showing again, saved on this computer. */
function useDismissals() {
  const { settings, setSettings } = useLauncher();
  const dismissed = settings.dismissedAnnouncements ?? [];
  const save = (ids: string[]) => setSettings({ ...settings, dismissedAnnouncements: ids.length ? ids : undefined });
  return {
    dismissed,
    dismiss: (...ids: string[]) => {
      ids.forEach((id) => track("announcement_dismissed", { id }));
      save([...new Set([...dismissed, ...ids])]);
    },
    restore: (id: string) => save(dismissed.filter((d) => d !== id)),
  };
}

/** Forgets dismissals of announcements the site no longer lists. */
export function useTidyDismissals() {
  const list = useAnnouncementList();
  const { settings, setSettings } = useLauncher();
  const dismissed = settings.dismissedAnnouncements;
  useEffect(() => {
    if (!list || !dismissed) return;
    const kept = keptDismissals(dismissed, list);
    if (kept.length !== dismissed.length) setSettings({ ...settings, dismissedAnnouncements: kept.length ? kept : undefined });
  }, [list, dismissed]); // eslint-disable-line react-hooks/exhaustive-deps
}

/** The text with its links, which open in the browser (main.tsx). */
function Message({ announcement }: { announcement: Announcement }) {
  const parts = announcement.parts?.length ? announcement.parts : [{ text: announcement.message }];
  return parts.map((part, i) =>
    part.href ? (
      <a key={i} href={part.href} onClick={() => track("announcement_link_opened", { id: announcement.id })}>
        {part.text}
      </a>
    ) : (
      part.text
    ),
  );
}

/** In the top bar, once there's anything: how many haven't been dismissed. */
export function AnnouncementsButton({ onOpen }: { onOpen: () => void }) {
  const list = useAnnouncementList();
  const { dismissed } = useDismissals();
  if (!list?.length) return null;
  const waiting = sorted(list, dismissed).shown.length;
  return (
    <button
      className="icon announcements-button"
      aria-label={waiting ? t`Announcements: ${plural(waiting, { one: "# new", other: "# new" })}` : t`Announcements`}
      title={t`Announcements`}
      onClick={onOpen}
    >
      <Megaphone size={18} />
      {waiting > 0 && (
        <span className="downloads-count" aria-hidden="true">
          {waiting}
        </span>
      )}
    </button>
  );
}

/**
 * Above the list: the newest announcement, until it's dismissed. In the
 * Sidebar layout it's a strip across the window under the top bar instead of
 * a box, so it reads as part of the window rather than pushing a box in.
 */
export function AnnouncementBanner({ onOpen, edge = false }: { onOpen: () => void; edge?: boolean }) {
  const list = useAnnouncementList();
  const { dismissed, dismiss } = useDismissals();
  const newest = list && bannerAnnouncement(list, dismissed);
  if (!newest) return null;
  return (
    <div className={`banner announcement-banner${edge ? " edge" : ""}`} role="status" aria-label={t`Announcement`}>
      <Megaphone size={18} aria-hidden="true" />
      <p>
        <Message announcement={newest} />
      </p>
      {list.length > 1 && (
        <button onClick={onOpen}>
          <Trans>All announcements</Trans>
        </button>
      )}
      <button onClick={() => dismiss(newest.id)}>
        <Trans comment="Hide an announcement">Dismiss</Trans>
      </button>
    </div>
  );
}

/** Every recent announcement, newest first, to read again or dismiss. */
export function AnnouncementsDialog({ onClose }: { onClose: () => void }) {
  const list = useAnnouncementList() ?? [];
  const { dismissed, dismiss, restore } = useDismissals();
  const [showDismissed, setShowDismissed] = useState(false);
  const { shown, dismissed: hidden } = sorted(list, dismissed);
  const count = hidden.length;
  useEffect(() => void trackScreen({ screen: "announcements" }), []);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);
  const item = (a: Announcement, gone: boolean) => (
    <li key={a.id} className={gone ? "announcement dismissed" : "announcement"}>
      <div>
        <p className="muted">
          {dayLabel(a.publishedAt)}
          {a.editedAt ? (
            <>
              {" · "}
              <Trans comment="An announcement changed after it was published">edited</Trans>
            </>
          ) : null}
        </p>
        <p>
          <Message announcement={a} />
        </p>
      </div>
      {gone ? (
        <button onClick={() => restore(a.id)}>
          <Trans>Show again</Trans>
        </button>
      ) : (
        <button
          className="announcement-dismiss"
          aria-label={t({ message: "Dismiss", comment: "Hide an announcement" })}
          title={t({ message: "Dismiss", comment: "Hide an announcement" })}
          onClick={() => dismiss(a.id)}
        >
          <X size={16} />
        </button>
      )}
    </li>
  );
  return (
    <div className="overlay" {...closesOutside(onClose)}>
      <section className="detail choose announcements" role="dialog" aria-label={t`Announcements`} onClick={(e) => e.stopPropagation()}>
        <div className="detail-body">
          <h2>
            <Trans>Announcements</Trans>
          </h2>
          {shown.length ? (
            <ul className="announcement-list">{shown.map((a) => item(a, false))}</ul>
          ) : (
            <p className="muted">
              <Trans>You&apos;re all caught up.</Trans>
            </p>
          )}
          {showDismissed && hidden.length > 0 && <ul className="announcement-list">{hidden.map((a) => item(a, true))}</ul>}
          <div className="row">
            {hidden.length > 0 && (
              <button onClick={() => setShowDismissed(!showDismissed)}>
                {showDismissed ? t`Hide dismissed` : t`Show dismissed (${count})`}
              </button>
            )}
            {shown.length > 1 && (
              <button onClick={() => dismiss(...shown.map((a) => a.id))}>
                <Trans>Dismiss all</Trans>
              </button>
            )}
            <button className="primary" autoFocus onClick={onClose}>
              <Trans>Done</Trans>
            </button>
          </div>
        </div>
      </section>
    </div>
  );
}
