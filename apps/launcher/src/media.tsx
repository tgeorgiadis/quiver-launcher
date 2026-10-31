import { useState } from "react";
import { ExternalLink, Play } from "lucide-react";
import { msg, t } from "@lingui/core/macro";
import type { MessageDescriptor } from "@lingui/core";
import { MEDIA_KINDS, type MediaKind, type MediaVideo } from "@quiverlauncher/api";
import { i18n } from "@quiverlauncher/i18n";
import { track } from "./telemetry";

export const MEDIA_KIND_LABELS: Record<MediaKind, MessageDescriptor> = {
  trailer: msg({ message: "Trailer", comment: "A kind of video" }),
  gameplay: msg({ message: "Gameplay", comment: "A kind of video" }),
  review: msg({ message: "Review", comment: "A kind of video" }),
  other: msg({ message: "Other", comment: "A kind of video" }),
};

/** The video's YouTube thumbnail, which needs no player loaded. */
export const thumbnailUrl = (youtubeId: string) => `https://i.ytimg.com/vi/${youtubeId}/hqdefault.jpg`;

/** The video on YouTube itself, at its start time. */
export const youtubeUrl = (video: MediaVideo) => `https://www.youtube.com/watch?v=${video.youtubeId}${video.start ? `&t=${video.start}s` : ""}`;

/**
 * YouTube's privacy-enhanced player, through the website's page for it:
 * YouTube only plays for a page that says which site it's on, and the
 * launcher's own page can't (tauri:// on macOS and Linux).
 */
export const embedUrl = (video: MediaVideo) => `https://quiverlauncher.com/embed/youtube/${video.youtubeId}${video.start ? `?start=${video.start}` : ""}`;

/**
 * An app's videos on its Media tab, as the website shows them: in the order
 * staff set, each a thumbnail until it's played, one playing at a time.
 */
export function MediaTab({ videos, slug }: { videos: MediaVideo[]; slug: string }) {
  const [kind, setKind] = useState<MediaKind | "all">("all");
  const [playing, setPlaying] = useState<number | null>(null);
  const kinds = MEDIA_KINDS.filter((k) => videos.some((v) => v.kind === k));
  const shown = videos.map((video, index) => ({ video, index })).filter(({ video }) => kind === "all" || video.kind === kind);
  return (
    <section className="media-section" aria-label={t`Media`}>
      {kinds.length > 1 && (
        <div className="media-filters" role="group" aria-label={t`Show videos`}>
          {(["all", ...kinds] as const).map((k) => (
            <button key={k} type="button" aria-pressed={kind === k} onClick={() => setKind(k)}>
              {k === "all" ? t({ message: "All", comment: "Shows videos of every kind" }) : i18n._(MEDIA_KIND_LABELS[k])}
              <span className="tab-count">{k === "all" ? videos.length : videos.filter((v) => v.kind === k).length}</span>
            </button>
          ))}
        </div>
      )}
      <div className="media-grid">
        {shown.map(({ video, index }) => {
          const kindName = i18n._(MEDIA_KIND_LABELS[video.kind]);
          const name = video.title || t`${kindName} video`;
          return (
            <figure className="media-card" key={index}>
              <div className="media-frame">
                {playing === index ? (
                  <iframe
                    src={embedUrl(video)}
                    title={name}
                    allow="autoplay; encrypted-media; fullscreen; picture-in-picture"
                    referrerPolicy="strict-origin-when-cross-origin"
                    allowFullScreen
                  />
                ) : (
                  <button
                    type="button"
                    className="media-play"
                    aria-label={t`Play ${name}`}
                    onClick={() => {
                      setPlaying(index);
                      track("media_played", { slug, kind: video.kind, position: index + 1 });
                    }}
                  >
                    <img src={thumbnailUrl(video.youtubeId)} alt="" loading="lazy" />
                    <span className="media-play-icon" aria-hidden>
                      <Play size={22} fill="currentColor" />
                    </span>
                  </button>
                )}
              </div>
              <figcaption>
                <span className="media-kind">{kindName}</span>
                {video.title && <span className="media-title">{video.title}</span>}
                <a className="media-youtube" href={youtubeUrl(video)}>
                  YouTube <ExternalLink size={12} />
                </a>
              </figcaption>
            </figure>
          );
        })}
      </div>
    </section>
  );
}
