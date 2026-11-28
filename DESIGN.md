---
name: Quiver
description: One look for Quiver Launcher and quiverlauncher.com, a dark shelf where the games' art is the light.
colors:
  night-navy: "#090f17"
  panel-navy: "#131922"
  card-navy: "#141a23"
  field-slate: "#212831"
  hairline: "#262d36"
  mist-text: "#e8eaee"
  bright-white: "#ffffff"
  quiet-grey: "#8f959d"
  quiver-sky: "#38bdf8"
  sky-ink: "#04121b"
  link-sky: "#7dd3fc"
  sage-good: "#8ac3a2"
  amber-caution: "#d7b56a"
  coral-danger: "#e38c87"
  day-page: "#f4f5f7"
  day-panel: "#ffffff"
  day-field: "#e8eaee"
  day-hairline: "#dcdee2"
  day-text: "#121821"
  day-strong: "#0b0c0e"
  day-muted: "#5b616b"
  day-sky: "#0369a1"
  day-link: "#075985"
  day-good: "#2e7d52"
  day-caution: "#8a5d00"
  day-danger: "#b93a30"
typography:
  display:
    fontFamily: "Inter Variable, Inter, system-ui, sans-serif"
    fontSize: "38px"
    fontWeight: 700
    lineHeight: 1.2
  headline-detail:
    fontFamily: "Inter Variable, Inter, system-ui, sans-serif"
    fontSize: "38px"
    fontWeight: 600
    lineHeight: 1.2
  headline:
    fontFamily: "Inter Variable, Inter, system-ui, sans-serif"
    fontSize: "22px"
    fontWeight: 600
    lineHeight: 1.2
  section:
    fontFamily: "Inter Variable, Inter, system-ui, sans-serif"
    fontSize: "18px"
    fontWeight: 600
    lineHeight: 1.2
  title:
    fontFamily: "Inter Variable, Inter, system-ui, sans-serif"
    fontSize: "16px"
    fontWeight: 600
    lineHeight: 1.2
  body:
    fontFamily: "Inter Variable, Inter, system-ui, sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.5
  control:
    fontFamily: "Inter Variable, Inter, system-ui, sans-serif"
    fontSize: "13px"
    fontWeight: 500
    lineHeight: 1.2
  secondary:
    fontFamily: "Inter Variable, Inter, system-ui, sans-serif"
    fontSize: "12px"
    fontWeight: 400
    lineHeight: 1.5
  label:
    fontFamily: "Inter Variable, Inter, system-ui, sans-serif"
    fontSize: "11px"
    fontWeight: 600
    lineHeight: 1.2
    letterSpacing: "0.06em"
  code:
    fontFamily: "ui-monospace, Cascadia Code, SFMono-Regular, Consolas, monospace"
    fontSize: "12px"
    fontWeight: 400
    lineHeight: 1.5
rounded:
  small: "4px"
  control: "8px"
  panel: "12px"
  hero: "16px"
  pill: "999px"
spacing:
  "4": "4px"
  "8": "8px"
  "12": "12px"
  "16": "16px"
  "24": "24px"
  "32": "32px"
  "48": "48px"
components:
  button-main:
    backgroundColor: "{colors.quiver-sky}"
    textColor: "{colors.sky-ink}"
    typography: "{typography.control}"
    rounded: "{rounded.control}"
    padding: "8px 16px"
    height: "36px"
  button-main-hover:
    backgroundColor: "{colors.link-sky}"
    textColor: "{colors.sky-ink}"
  button:
    backgroundColor: "{colors.field-slate}"
    textColor: "{colors.mist-text}"
    typography: "{typography.control}"
    rounded: "{rounded.control}"
    padding: "8px 16px"
    height: "36px"
  button-hover:
    textColor: "{colors.bright-white}"
  button-danger:
    backgroundColor: "{colors.field-slate}"
    textColor: "{colors.coral-danger}"
    rounded: "{rounded.control}"
    padding: "8px 16px"
    height: "36px"
  button-text:
    textColor: "{colors.link-sky}"
    typography: "{typography.control}"
  field:
    backgroundColor: "{colors.field-slate}"
    textColor: "{colors.mist-text}"
    typography: "{typography.control}"
    rounded: "{rounded.control}"
    padding: "0 12px"
    height: "36px"
  search:
    backgroundColor: "{colors.field-slate}"
    textColor: "{colors.mist-text}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "0 14px"
    height: "44px"
  panel:
    backgroundColor: "{colors.panel-navy}"
    textColor: "{colors.mist-text}"
    rounded: "{rounded.panel}"
    padding: "16px"
  dialog:
    backgroundColor: "{colors.panel-navy}"
    textColor: "{colors.mist-text}"
    rounded: "{rounded.panel}"
    padding: "24px"
    width: "440px"
  card:
    backgroundColor: "{colors.card-navy}"
    textColor: "{colors.mist-text}"
    rounded: "{rounded.control}"
  chip:
    textColor: "{colors.quiet-grey}"
    typography: "{typography.label}"
    rounded: "{rounded.pill}"
    padding: "1px 6px"
---

# Design System: Quiver

## Overview

**Creative North Star: "The Lit Shelf"**

Quiver is a dark, quiet shelf, and the games' art is the light on it. The interface is Night Navy and soft grey so that box art, covers and screenshots carry the colour. One sky-blue action, Quiver Sky, says what to do next. Everything else stays neutral until a hand reaches for it. The same shelf stands at a desk, on the couch with a controller, on a phone and on quiverlauncher.com. The launcher and the website are one system, built from one token file (`packages/ui/src/tokens.css`).

The mood is calm and dark, and the art leads. Density is moderate: a library or catalog is a grid of cards that scan quickly, and the chrome around it is thin, flat and quiet. Components are quiet until touched. At rest they are flat fills and hairlines. Hover, keyboard focus and controller focus bring Quiver Sky and, for a card, a small lift. Dark is the default. The light theme keeps every role and swaps only the values.

The system already has its rules written down in the website repository's `docs/standards/design.md`, and `scripts/check-standards.mjs` counts the places that break them. This file records the same system for design tools. Where the two disagree, the standard wins and this file gets fixed.

**Key Characteristics:**

- Night Navy rooms, Quiver Sky as the single accent, art as the colour.
- One typeface, Inter Variable, on a nine-step scale from 11px to 38px.
- Strict scales for space, corners and shadow. A value off a scale is a bug.
- Flat at rest. Borders or a tonal step separate things, not shadows.
- Components are quiet until touched, then lit with Quiver Sky.
- The launcher and the website look and read the same.

## Colors

The palette is a deep, cool navy ramp with one cool sky accent and three soft status colours. In CSS each colour is written by its job (`var(--muted)`), never as a hex code or a shade name.

### Primary

- **Quiver Sky** (`--accent`): the single thing to do on a screen (Get, Download, Save) and the selected thing (the current tab, a chosen option's check, focus rings, a card's lit border). The light theme uses Day Sky.
- **Link Sky** (`--link`): links in running text and text buttons; also the main button's hover fill. The light theme uses Day Link.
- **Sky Ink** (`--on-accent`): text and icons on a Quiver Sky fill. White in the light theme.

### Neutral

- **Night Navy** (`--bg`): the page, the room the shelf stands in. Day Page in the light theme.
- **Panel Navy** (`--panel`): a section sitting on the page, dialogs, menus. White in the light theme.
- **Card Navy** (`--card`): a catalog card or a list item, one tonal step off the page. White in the light theme.
- **Field Slate** (`--field`): inputs, dropdowns and the quiet neutral button. Day Field in the light theme.
- **Hairline** (`--line`): 1px borders and dividers. Day Hairline in the light theme.
- **Mist Text** (`--fg`): body text. Day Text in the light theme.
- **Bright White** (`--strong`): headings, names, a hovered button's label. Day Strong in the light theme.
- **Quiet Grey** (`--muted`): secondary text such as dates, counts, hints and chip labels. Day Muted in the light theme.

### Status

- **Sage Good** (`--green`): runs well, verified, done.
- **Amber Caution** (`--caution`): needs attention, may not work.
- **Coral Danger** (`--danger`): destructive or failed. It is the text colour of the danger button and of field errors.

### Named Rules

**The One Light Rule.** Quiver Sky marks one main action per view. Two filled sky buttons side by side are a mistake. Everything else is neutral until hovered, focused or selected.

**The Job, Not Shade Rule.** CSS names a colour's job (`var(--muted)`), never a hex code and never a shade (`--gray-63`). A colour changes in `tokens.css` and nowhere else.

**The Never Alone Rule.** Colour is never the only signal. A status carries a word or an icon as well ("Runs well", "Doesn't run").

**The On-Art Exception.** Text, icons and scrims laid over artwork (a featured slide, a hero, a video) are fixed white and black, since the art is the same in both themes; so are the system's own colours, such as the window close button's red. These are the only literal colours.

## Typography

**Body Font:** Inter Variable (with Inter, then system-ui), bundled with its optical sizes so large headings get Inter's display cut on their own.

**Code Font:** the system's monospace (ui-monospace, then Cascadia Code, SFMono-Regular and Consolas), only for code, file paths, hashes and command lines.

**Character:** One neutral, highly legible family carries everything. Hierarchy comes from size and weight, never from a second typeface, so the art stays the most expressive thing on screen.

### Hierarchy

- **Display** (700, 38px, 1.2): one hero line per page at most.
- **Headline, detail** (600, 38px, 1.2): the title of an app or game page, over its art.
- **Headline** (600, 22px, 1.2): page headings, the page's one `h1`.
- **Section** (600, 18px, 1.2): section headings (`h2`).
- **Title** (600, 16px, 1.2): card and panel titles. Long reading text (READMEs, release notes) is 16px at 1.5.
- **Body** (400, 14px, 1.5): body text and descriptions, at most 72 characters a line.
- **Control** (500, 13px): buttons (600), inputs, tabs, menus, table cells.
- **Secondary** (400, 12px, 1.5): dates, counts, hints under a field.
- **Label** (600, 11px, 0.06em, uppercase): small capital labels and counts in chips. The smallest size there is.

### Named Rules

**The Nine Sizes Rule.** Text uses 11, 12, 13, 14, 16, 18, 22, 28 or 38px. Half sizes (12.5, 13.5) are not sizes, and nothing is below 11px.

**The Capitals Rule.** Uppercase is only for 11px labels, with 0.06em letter-spacing.

**The Couch Exception.** TV mode is read from across a room, so its page titles are 48px and its jump-to-letter 80px. These are the only sizes above 38px, and only in TV mode's own rules (`[data-tv]`).

**The One Heading Rule.** Every screen has one `h1`, and levels never skip. Where the design shows the screen's name another way (the launcher's Library and Discover tabs, an app page's Manage tab), the heading is there for screen readers only.

## Layout

- **Spacing** follows the 4, 8, 12, 16, 24, 32 and 48px scale for padding, margin and gap alike.
- **Widths:** page content is at most 1280px wide; reading text at most 72 characters.
- **Breakpoints:** 760px (phone) and 1080px (laptop). Check at 375px and 1440px.
- **The header** has the logo on the left, the sections next to it, and the account and Download on the right. It is the same on every page, the download page included.
- **Lists of apps** are a grid of cards, or a shelf (one scrolling row with a "See all" link). The same list is never shown both ways.
- **Forms** line their fields up on one left edge, with each label above its field and the main button last.
- **Loading** shows the page's outline as a skeleton, never a spinner on an empty page.
- **TV mode** (launcher) is full screen and driven by a controller. Focus is always visible, and targets are large enough to read from the couch.
- **Targets** are at least 24 by 24px. A smaller mark, such as a slideshow dot, sits in a 24px button.

## Elevation & Depth

The system is flat at rest. A panel is set apart by a Hairline border or by a tonal step (Night Navy, then Panel Navy, then Card Navy), never both plus a shadow. Shadow means something floats above the page or is being touched.

### Shadow Vocabulary

- **Menu** (`box-shadow: 0 4px 16px var(--shadow)`): menus, popovers, dropdown lists.
- **Dialog** (`box-shadow: 0 16px 48px var(--shadow)`): dialogs.
- **Card lift** (`transform: translateY(-4px)`, Quiver Sky border, `box-shadow: 0 15px 30px` of the card shadow): a catalog card under the pointer or holding the focus, so a controller's focus can be seen across a room.

### Named Rules

**The Flat Shelf Rule.** Things in the page cast no shadow. The only shadows are the menu, the dialog, and the card lifted by hover or focus.

## Shapes

Corners are gently rounded and come from one scale:

- **Small** (4px): small things inside others, such as chips in a card or code.
- **Control** (8px): controls and cards.
- **Panel** (12px): panels and dialogs.
- **Hero** (16px): the hero and large art.
- **Pill** (999px): chips and pills.
- **Round** (50%): avatars.

Borders are 1px Hairline. App icons and the Quiver mark keep their own shapes. The catalog card is still 9px in `catalog.css` and moves to 8px as it is touched.

## Components

### Buttons

Quiet until touched. Every button is 36px tall with 13px/600 labels, 8px corners and an icon gap of 8px.

- **Main:** Quiver Sky fill with Sky Ink text, one per view. On hover it brightens to Link Sky.
- **Button:** Field Slate fill with Mist Text. On hover the fill lightens by 10% of the text colour and the label turns Bright White.
- **Danger:** neutral fill with Coral Danger text. On hover a 16% wash of Coral Danger comes in.
- **Text button:** reads as an underlined Link Sky link, for low-key actions beside or under real buttons.
- **App action:** in the launcher, an app page's own action (Play, Install, Add) is 46px tall with 16px labels, as the one thing the page is for and a controller's first target. Nothing else is taller than 36px.
- **States:** pressed moves it down 1px; keyboard focus is a 2px Quiver Sky outline at a 2px offset; disabled is 50% opacity with a not-allowed cursor; busy changes the label to what is happening ("Installing…").

### Chips

- **Style:** pill outline in a soft border, Label type, `1px 6px` padding: small facts about an app (Easy setup, AI-assisted, platform icons).
- **Easy setup** is the one highlighted chip, with its own icon and colour.

### Cards / Containers

- **Catalog card** (`EntryCard`, shared by both apps): Card Navy, a Hairline border and Control corners, with the cover art above the type, console, name and status. Covers without art cycle through four tinted grounds. Hover or focus lifts it 4px with a Quiver Sky border and the card shadow.
- **Panel:** Panel Navy, Hairline border, Panel corners, 16px padding and a 16px Title, with no shadow.
- **App page header:** both apps keep one order: its consoles and kind, its name, what it is, the games it's based on, then its tags, then its buttons. The launcher keeps only the tags the header doesn't already say (not its consoles or kind) and puts them quietly after the version it's based on. The main button is the only one in colour. The website shows the rest as filled and outlined secondary buttons; the launcher keeps them muted, with no fill until hovered, so Play or Add leads. They stay few: what's seldom wanted (hiding it from Discover, reporting a problem) goes in a More (⋯) menu at their end. What each app puts in the buttons can differ (the website links the repository; the launcher installs).
- **App page sidebar:** beside an app page's tabs, a column with the app's wide cover, then its details and How it runs (its Score over a bar of the three answers). The website keeps it on every tab. Both keep it slim, 300px with 30px between it and the tab, each fact's name over its value, so the eye stays on the tab. The launcher shows it beside every tab once the page is at least 1180px wide, and it stays in view while the tab scrolls when the whole column fits the window. Narrower, and in TV mode, the details sit over the README on Overview. More like this is a shelf under the page, as wide as the content over it.
- **Discussions open where their list is:** never on a page of their own. From an app's Discussions tab, the discussion takes the list's place in the tab, with Back to the list at its top, and the sidebar shows its facts (who started it, when, its comments, its last activity). From Community, it opens beside the list once there's room (the launcher's Community at least 1080px wide, the website's at 1100px), in view while the list scrolls, with its own scroll; the row it came from is marked. Narrower, and in TV mode, it takes the list's place. Back, B or Escape goes back to the list where it was. A link to one from anywhere else opens it in Community.
- **Release card:** opened, a release shows how to get it before what changed. Its files sit in a box of their own, tinted with the release's tone (blue when verified, grey when unverified, red when blocked), over a small RELEASE NOTES heading. In the launcher, Install leads the box, named with the file it installs; when one file runs on this computer, the others fold under "N other files". The website, which has no Install, lists every file in the box.
- **App page width:** the launcher's tabs keep to a centred column 1200px wide, or four fifths of a large window up to 1600px, leaving room either side.
- **Dialog:** asks one question. It has a Title, one sentence and two buttons, with the main one on the right. 440px wide at most, 24px padding, Panel corners, the dialog shadow, and a dimmed backdrop.

### Inputs / Fields

- **Style:** Field Slate fill, Hairline border, Control corners, 36px tall, 12px side padding, Control type, with the label above it.
- **Search:** a screen's own search (the catalog's, the library's) is the one larger field, and the same in both apps: 44px tall, 14px side padding, Body type, a 16px Quiet Grey search icon at its start, with the field's fill, border, hover and focus. The filters, sort and feed buttons in the same bar on the website are as tall as it, field-styled, with Control type.
- **Hover:** the border turns Quiet Grey.
- **Focus:** the border turns Quiver Sky, with a 1px Quiver Sky ring so it is as thick as other controls' outlines.
- **Error / Disabled:** a Coral Danger border with a 12px Coral Danger message underneath; disabled is 50% opacity. Hints underneath are 12px Quiet Grey.

### Dropdowns

- **Drawn by Quiver, never the browser.** The browser's own list ignores the theme, so every dropdown is Quiver's own: a field-styled button (or a filter-styled one in the catalog bar) opening a Panel Navy list with the menu shadow. The chosen option has a Quiver Sky check, and the option under the pointer or keys is lit. Up, Down, Home, End, typing, Enter and Escape all work.
- **Where it lives.** On the website it is `Select.tsx`. In the launcher the `<select>` stays underneath and `select-picker.ts` draws its list, for a click, the keys and a controller's Confirm alike, inside an open dialog when there is one.

### Navigation

- **Header:** the same on every page (see Layout). Sections are 13px Quiet Grey. The active one is Bright White with a 2px Quiver Sky underline.
- **Launcher tabs:** the launcher's Library and Discover are its header sections, and larger, as the tabs a controller's bumpers move between: 16px/500 with their counts, the active one Bright White on a Field Slate fill with the 2px Quiver Sky underline.
- **Tabs** (`.detail-tabs`) split one page into sections. The current tab is Bright White with the same 2px Quiver Sky underline. On an app's page they sit in a Panel Navy band across the page, flush under the header: the tonal step, not a hairline, marks where the header ends and what the tabs switch begins.
- **Icons** come from lucide-react: 16px in controls, 20px standalone, always with a text label or an `aria-label`.

### Status

- **Score:** a dot or icon plus words ("Runs well", "With issues", "Doesn't run") in Sage Good, Amber Caution or Coral Danger, at 11px on cards. Under How it runs, a 6px bar splits by the same three colours in proportion to what players said.

## Do's and Don'ts

### Do:

- **Do** let the art carry the colour. The interface stays in Night Navy, Panel Navy, Card Navy and the greys.
- **Do** give each view one Quiver Sky main button, placed last in a form and on the right in a dialog.
- **Do** write colours by their job (`var(--accent)`, `var(--muted)`) and take every size, space, corner and shadow from `tokens.css`.
- **Do** give every interactive thing rest, hover, focus (a 2px Quiver Sky outline at a 2px offset), pressed, disabled and busy states.
- **Do** keep text at WCAG AA (4.5:1, or 3:1 at 18px and up) in both themes.
- **Do** make a change in the launcher and the website in the same piece of work, so the same thing looks and reads the same in both.
- **Do** move only the cards as a screen changes: the cards in sight fade in and rise 8px together, while the header, search and filters are simply there. Within a screen, a section's content comes in the same way as another opens (Settings), while the list of sections stays put.
- **Do** bring a screen back as it was left (its content, scroll and pictures) rather than loading it again.
- **Do** keep things where they'll be while a screen loads: a placeholder is as tall as what replaces it, and what sits under a part still on its way waits for it rather than being pushed down when it comes.
- **Do** honour `prefers-reduced-motion`, so movement becomes a simple fade.

### Don't:

- **Don't** put two filled Quiver Sky buttons side by side.
- **Don't** use a hex code, a shade name, or a number that isn't on a scale.
- **Don't** set text below 11px, use half sizes, or add a second typeface.
- **Don't** put uppercase anywhere but 11px labels.
- **Don't** give things in the page a shadow. Only menus, dialogs and the lifted card cast one.
- **Don't** combine a border, a different background and a shadow on one panel.
- **Don't** show the browser's native dropdown list. Use Quiver's own dropdown.
- **Don't** let colour alone carry a status.
- **Don't** use the Quiver logo or icons outside their roles, or alter them (`TRADEMARKS.md`).
