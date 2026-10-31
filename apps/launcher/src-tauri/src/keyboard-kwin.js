// Loaded into KWin by Quiver Launcher (keyboard.rs) while it runs on KDE. KWin keeps an active full-screen window
// above every other, Steam's on-screen keyboard included, which never takes the focus: on a Steam Deck's desktop
// the keyboard opened behind a full-screen Quiver. So this tells Quiver when the keyboard shows and when it's gone,
// and Quiver steps out of full screen and back itself: a Flatpak's GTK doesn't take KWin's own change to it.
// Steam's keyboard: Steam's, kept above, and never taking the focus.
const keyboard = (w) => w.resourceClass === "steam" && w.keepAbove && !w.wantsInput;
let shown = false;

function update(gone) {
  const now = workspace.windowList().some((w) => w !== gone && keyboard(w));
  if (now === shown) return;
  shown = now;
  callDBus("com.quiverlauncher.QuiverLauncher.Keyboard", "/Keyboard", "com.quiverlauncher.Keyboard", "Shown", now);
}

workspace.windowAdded.connect(() => update());
workspace.windowRemoved.connect((w) => update(w));
update();
