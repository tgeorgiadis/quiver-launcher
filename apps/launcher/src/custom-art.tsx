/**
 * The player's own pictures for an app, one for each place the catalog has
 * one. Each is an https address or a file from this computer, which the
 * launcher keeps a copy of (custom_art.rs). Only a cover's address syncs: the
 * account has the one field for it.
 */
import { useState } from "react";
import { Trans } from "@lingui/react/macro";
import { msg, t } from "@lingui/core/macro";
import type { MessageDescriptor } from "@lingui/core";
import { convertFileSrc } from "@tauri-apps/api/core";
import { X } from "lucide-react";
import { i18n } from "@quiverlauncher/i18n";
import type { Entry } from "@quiverlauncher/api";
import { native } from "./native";

export type ArtSlot = "box" | "cover" | "banner" | "icon" | "logo";
export type Pictures = Partial<Record<ArtSlot, string>>;

/** A picture kept in the launcher's data folder, by this before its file's name. */
const FILE = "file:";
const isFile = (value: string) => value.startsWith(FILE);

/** The address to show a picture from. */
const artSrc = (value: string) => (isFile(value) ? convertFileSrc(value.slice(FILE.length), "quiverart") : value);

export const isArtAddress = (value: string) => /^https:\/\/\S+$/.test(value);

/** The files `pictures` use, to keep. */
export const artFiles = (pictures: (Pictures | undefined)[]) =>
  pictures.flatMap((p) => Object.values(p ?? {}).flatMap((value) => (isFile(value) ? [value.slice(FILE.length)] : [])));

type Overrides = { cover?: string; pictures?: Pictures };

/**
 * The entry with the player's pictures. A cover also shows across the app's
 * page, unless they gave that a picture of its own.
 */
export function withPictures(entry: Entry, { cover: address, pictures: own = {} }: Overrides): Partial<Pick<Entry, "libraryArt" | "artwork">> {
  const src = (value?: string) => (value ? artSrc(value) : undefined);
  const cover = src(own.cover ?? address);
  const [box, banner, logo, icon] = [src(own.box), src(own.banner), src(own.logo), src(own.icon)];
  const art = {
    ...(cover ? { header: cover, hero: cover } : {}),
    ...(box ? { capsule: box } : {}),
    ...(banner ? { hero: banner } : {}),
    ...(logo ? { logo } : {}),
  };
  return {
    ...(Object.keys(art).length ? { libraryArt: { ...entry.libraryArt, ...art } } : {}),
    ...(icon ? { artwork: icon } : {}),
  };
}

const SLOTS: { slot: ArtSlot; label: MessageDescriptor; address: MessageDescriptor; hint: MessageDescriptor; shape: string; catalog: (entry: Entry) => string | undefined }[] = [
  { slot: "box", label: msg`Box art`, address: msg`Box art address`, hint: msg`Portrait, about 600×900. The library's cards and TV mode.`, shape: "2 / 3", catalog: (e) => e.libraryArt?.capsule },
  { slot: "cover", label: msg`Cover`, address: msg`Cover address`, hint: msg`Wide, about 920×430. The Cover view, and anywhere there's no box art.`, shape: "920 / 430", catalog: (e) => e.libraryArt?.header },
  { slot: "banner", label: msg`Banner`, address: msg`Banner address`, hint: msg`Very wide, about 1920×620. Behind the app's page and Continue playing.`, shape: "1920 / 620", catalog: (e) => e.libraryArt?.hero },
  { slot: "icon", label: msg`Icon`, address: msg`Icon address`, hint: msg`Square. The app's page, the Icon view and its shortcuts.`, shape: "1", catalog: (e) => e.artwork },
  { slot: "logo", label: msg`Logo`, address: msg`Logo address`, hint: msg`A transparent PNG. For its Steam shortcut.`, shape: "16 / 9", catalog: (e) => e.libraryArt?.logo },
];

/** What the form holds for a picture: an address being typed, or a file's name with FILE before it. */
export type ArtValues = Record<ArtSlot, string>;

export const artValues = ({ cover, pictures = {} }: Overrides): ArtValues => ({
  box: pictures.box ?? "",
  cover: pictures.cover ?? cover ?? "",
  banner: pictures.banner ?? "",
  icon: pictures.icon ?? "",
  logo: pictures.logo ?? "",
});

/** The form's pictures to save: a cover's address apart, as it syncs. */
export function fromArtValues(values: ArtValues): Required<Pick<Overrides, "pictures">> & Pick<Overrides, "cover"> {
  const pictures: Pictures = {};
  for (const { slot } of SLOTS) {
    const value = values[slot].trim();
    if (value && !(slot === "cover" && !isFile(value))) pictures[slot] = value;
  }
  const cover = values.cover.trim();
  return { pictures, cover: cover && !isFile(cover) ? cover : undefined };
}

/** Whether every picture is an address that can be used, or a file. */
export const artValuesOk = (values: ArtValues) => Object.values(values).every((value) => !value.trim() || isFile(value) || isArtAddress(value.trim()));

function ArtField({ field, value, fallback, canPick, onChange }: { field: (typeof SLOTS)[number]; value: string; fallback?: string; canPick: boolean; onChange: (value: string) => void }) {
  const [failed, setFailed] = useState<string>();
  const [problem, setProblem] = useState<string>();
  const typed = value.trim();
  const preview = typed && (isFile(typed) || isArtAddress(typed)) ? artSrc(typed) : !typed ? fallback : undefined;
  const label = i18n._(field.label);
  return (
    <div className="art-field" data-slot={field.slot}>
      <div className="art-preview" style={{ aspectRatio: field.shape }}>
        {preview && failed !== preview && <img key={preview} src={preview} alt="" onError={() => setFailed(preview)} />}
      </div>
      <div className="art-inputs">
        <span className="field-label">{label}</span>
        <div className="art-input">
          {isFile(typed) ? (
            <span className="art-file">
              <Trans>Picture from this computer</Trans>
            </span>
          ) : (
            <input aria-label={i18n._(field.address)} placeholder="https://…" value={value} onChange={(e) => onChange(e.target.value)} />
          )}
          {typed && (
            <button type="button" className="panel-close" aria-label={t`Clear ${label}`} title={t`Clear ${label}`} onClick={() => onChange("")}>
              <X size={14} aria-hidden="true" />
            </button>
          )}
        </div>
        {canPick && (
          <button
            type="button"
            className="link-button"
            aria-label={t`Choose a file for ${label}`}
            onClick={() => {
              setProblem(undefined);
              native.pickArtwork(t`Choose a picture`, field.slot).then(
                (file) => file && onChange(`${FILE}${file}`),
                (e) => setProblem(String(e)),
              );
            }}
          >
            <Trans>Choose a file…</Trans>
          </button>
        )}
        <p className="muted small">{i18n._(field.hint)}</p>
        {problem && <p className="job-error">{problem}</p>}
        {preview && failed === preview && (
          <p className="job-error">
            <Trans>That picture didn&apos;t load.</Trans>
          </p>
        )}
      </div>
    </div>
  );
}

/** A field for each of an app's pictures, showing the catalog's where it has none of the player's. */
export function ArtFields({ values, catalog, canPick, onChange }: { values: ArtValues; catalog?: Entry; canPick: boolean; onChange: (values: ArtValues) => void }) {
  return (
    <>
      {SLOTS.map((field) => (
        <ArtField key={field.slot} field={field} value={values[field.slot]} fallback={catalog && field.catalog(catalog)} canPick={canPick} onChange={(value) => onChange({ ...values, [field.slot]: value })} />
      ))}
      <p className="muted small">
        <Trans>A cover&apos;s address follows your account. The other pictures, and any from a file, stay on this computer.</Trans>
      </p>
    </>
  );
}
