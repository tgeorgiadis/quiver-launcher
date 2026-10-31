# Quiver name and logo

The names **Quiver** and **Quiver Launcher**, the **quiverlauncher.com** domain, and the Quiver logo and app icons
are the project's marks. They identify the official app and website, made by Thomas Georgiadis. This page says how
you may use them.

The code's licenses don't give anyone the right to use the marks to make another project look official, endorsed by
or connected to Quiver: not the Business Source License Quiver Launcher 4 is published under, and not the MIT License
each version switches to after a year, or that Quiver Launcher 3 uses. Code that's MIT licensed is free to use, but
a project built from it needs a name and look of its own. Licenses already given for files published before, such as
in Quiver Launcher 3, aren't changed by this page.

## The logo and icon files

The brand assets are the logo and icon files:

- `icon.ico`
- `apps/launcher/public/quiver-icon-96.png`
- everything in `apps/launcher/src-tauri/icons/`
- the Android launcher icons, in `apps/launcher/src-tauri/gen/android/app/src/main/res/mipmap-*/`

They aren't part of the code's license (see [LICENSE](LICENSE)). Instead, this page gives you permission to copy and
use these files to build, test and run your own copy of Quiver Launcher, and to keep them, unchanged, in copies of the
source code you share. Nothing more.

## You may

- Say truthfully that your project is based on, forked from, or works with Quiver Launcher, for example "a fork of
  Quiver Launcher" in your README.
- Share and link to the official, unmodified builds from this repository's releases, under their own name.
- Use the names when you write or talk about the project, such as in reviews, articles, videos and bug reports.
- Suggest changes to the logo or icons in a pull request.

## You may not, in a modified version or another project

- Use "Quiver" in its name, including the app name, window title, installer, package name, repository name or domain,
  or a name or look that could be mistaken for Quiver's.
- Use the Quiver logo or app icons, or artwork that could be mistaken for them.
- Keep the official app and package identifiers. A modified build that keeps them installs over the official app,
  shares its data folder and can take its updates.
- Suggest that it is official, endorsed by, or supported by Quiver.

This applies to versions whose code has become MIT licensed too.

## Giving a fork its own identity

When you release a fork, give it its own name, icon and identifiers. In this app they are:

- `productName` and `identifier` in `apps/launcher/src-tauri/tauri.conf.json`. The identifier is also the name of
  the data folder.
- `applicationId` in `apps/launcher/src-tauri/gen/android/app/build.gradle.kts`.
- The Flatpak ID, `com.quiverlauncher.QuiverLauncher`: the files in `apps/launcher/flatpak/` and their names.
- `API` in `apps/launcher/src-tauri/src/update.rs`, which is where the app looks for its own updates, and the update
  signing key the app checks them with (`QUIVER_UPDATE_PUBKEY`, set when the app is built).
- The `QuiverLauncher` user agents in `apps/launcher/src-tauri/src/`.
- The logo and icon files listed above, plus the name and logo shown in the app itself.

## Where Quiver came from

Quiver Launcher started as a fork of [GithubLauncher](https://github.com/SirDiabo/GithubLauncher) by SirDiabo. That
project's name is SirDiabo's, and this page doesn't cover it.

## Questions

If you're unsure whether a use is fine, or want permission for one this page doesn't allow, open an issue in this
repository and ask.

This page doesn't take away anything the law already lets you do, such as naming Quiver to describe it accurately.
