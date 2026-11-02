/**
 * A picture of the player's behind the library's grid, as Quiver Launcher 3 had: one image they pick in Settings, at
 * their opacity. It fills the window under the top bar and stays put as the cards scroll over it. The Sidebar layout,
 * Browse and an app's page have their own backgrounds.
 */
import { useEffect, useState } from "react";
import { native } from "./native";
import { useLauncher } from "./store";

export const DEFAULT_BACKGROUND_OPACITY = 0.3;

// Read once: the library goes each time Browse opens, and comes back with its picture already there.
let read: { path: string; image: Promise<string>; url?: string } | null = null;

function imageAt(path: string) {
  if (read?.path === path) return read.image;
  const old = read;
  const image = native.readBackground(path).then((bytes) => URL.createObjectURL(new Blob([bytes])));
  const reading = { path, image } as NonNullable<typeof read>;
  read = reading;
  // Moved or deleted, most likely: looked for again next time, as it may be back.
  image.then(
    (url) => void (reading.url = url),
    () => void (read === reading && (read = null)),
  );
  if (old?.url) URL.revokeObjectURL(old.url);
  return image;
}

/** The picture at `path`, to show: undefined until it's read (or with none), null if it can't be. */
export function useBackgroundImage(path?: string) {
  const known = () => (path && read?.path === path ? read.url : undefined);
  const [loaded, setLoaded] = useState<{ path: string; url: string | null }>();
  useEffect(() => {
    if (!path || known()) return;
    let live = true;
    imageAt(path).then(
      (url) => live && setLoaded({ path, url }),
      () => live && setLoaded({ path, url: null }),
    );
    return () => void (live = false);
  }, [path]); // eslint-disable-line react-hooks/exhaustive-deps
  return known() ?? (loaded?.path === path ? loaded?.url : undefined);
}

/** Behind everything in the page, over the window's own background (app.css). */
export function LibraryBackground() {
  const { settings } = useLauncher();
  const url = useBackgroundImage(settings.libraryBackground);
  if (!url) return null;
  return (
    <div className="library-background" aria-hidden="true">
      <img src={url} alt="" style={{ opacity: settings.libraryBackgroundOpacity ?? DEFAULT_BACKGROUND_OPACITY }} />
    </div>
  );
}
