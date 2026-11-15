/**
 * Where the pictures the player chose for their apps in Quiver Launcher 3 go (old-library.tsx). 3.x had one picture
 * for each app; Quiver Launcher 4 has several kinds, each shown in its own places, so the player says which kinds
 * theirs were more like, any number of them. Each choice shows one of their own pictures in its shape, cropped as a card
 * crops it, with where it shows and its size.
 */
import { useEffect, useState } from "react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { isArtAddress, slotWords, type ArtSlot } from "./custom-art";
import { native, type V3Library } from "./native";

/** The kinds of picture a 3.x picture can become, in the order offered. */
export const OLD_PICTURE_SLOTS: readonly ArtSlot[] = ["icon", "cover", "box"];
/** As before there was a choice: 3.x's pictures were its library's icons. */
export const DEFAULT_OLD_PICTURE_SLOTS: readonly ArtSlot[] = ["icon"];

/** Whether a library has pictures to bring: any app with one. */
export const hasOldPictures = (library: V3Library) => library.apps.some((a) => a.icon && (a.repository || a.catalogId));

/** One of the library's pictures to show in each shape: the first that loads, from an address or a file. */
function useSample(library: V3Library) {
  const pictures = library.apps.flatMap((a) => (a.icon && (a.repository || a.catalogId) ? [a.icon] : []));
  const key = pictures.slice(0, 5).join("\n");
  const [src, setSrc] = useState<string>();
  useEffect(() => {
    let gone = false;
    let made: string | undefined;
    (async () => {
      for (const picture of key.split("\n").filter(Boolean)) {
        if (isArtAddress(picture)) return !gone && setSrc(picture);
        const bytes = await native.readBackground(picture).catch(() => null);
        if (bytes && !gone) {
          made = URL.createObjectURL(new Blob([bytes]));
          return setSrc(made);
        }
      }
    })();
    return () => {
      gone = true;
      if (made) URL.revokeObjectURL(made);
    };
  }, [key]);
  return src;
}

/**
 * The choice, as a box for each kind with a preview. `compact`, for the library's banner: the boxes alone, each with its
 * shape and size in a line, as there's no room for previews there.
 */
export function OldPictures({ library, value, onChange, compact }: { library: V3Library; value: readonly ArtSlot[]; onChange: (slots: ArtSlot[]) => void; compact?: boolean }) {
  const sample = useSample(library);
  const [failed, setFailed] = useState<string>();
  const toggle = (slot: ArtSlot, on: boolean) => onChange(OLD_PICTURE_SLOTS.filter((s) => (s === slot ? on : value.includes(s))));
  return (
    <fieldset className={`old-pictures${compact ? " compact" : ""}`}>
      <legend>
        <Trans>Your custom images from Quiver Launcher 3</Trans>
      </legend>
      <p className="muted">
        <Trans>Quiver Launcher 3 had one image for each app. Quiver Launcher 4 has a few kinds, each shown in different places. Were yours more like icons, covers or box art? Choose what card displays your custom images should be added to.</Trans>
      </p>
      <div className="old-pictures-choices">
        {OLD_PICTURE_SLOTS.map((slot) => {
          const { label, hint, shape } = slotWords(slot);
          const on = value.includes(slot);
          return (
            <label key={slot} className="old-picture" data-slot={slot} data-on={on || undefined}>
              <input type="checkbox" checked={on} onChange={(e) => toggle(slot, e.target.checked)} />
              {!compact && (
                <span className="old-picture-frame" aria-hidden="true">
                  <span className="old-picture-shape" style={{ aspectRatio: shape }}>
                    {sample && failed !== sample && <img src={sample} alt="" onError={() => setFailed(sample)} />}
                  </span>
                </span>
              )}
              <span className="old-picture-words">
                <strong>{label}</strong>
                <small>{hint}</small>
              </span>
            </label>
          );
        })}
      </div>
      {value.length === 0 && <p className="muted small">{t`Your custom images won't come over. You can still give any app pictures later, from Change name or artwork on its page.`}</p>}
    </fieldset>
  );
}
