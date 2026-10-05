# @quiverlauncher/ui

The catalog's components and styles, shared by [Quiver Launcher](https://github.com/tgeorgiadis/quiver-launcher)
and [quiverlauncher.com](https://quiverlauncher.com) so an app looks the same in both: the catalog card and its
parts (artwork, platform icons, score, AI chip, release age), tag and date formatting, and README rendering.

It isn't published anywhere: quiverlauncher.com includes this repository as a git submodule and builds it from
source, with `react`, `lucide-react` and `react-icons` as peers.

```tsx
import { EntryCard, EntryCardContent } from "@quiverlauncher/ui";
import { Markdown } from "@quiverlauncher/ui/markdown"; // large, so it's its own import
import "@quiverlauncher/ui/catalog.css";

// A card that opens the app with a button, as in the launcher:
<EntryCard entry={entry} onOpen={() => open(entry)} action={<button>Get</button>} />

// Or wrapped in your own link, as on the website:
<Link to={`/apps/${entry.slug}`} className="entry-card">
  <EntryCardContent entry={entry} heading="h2" />
</Link>
```

The components don't need a router or a data layer: a card takes an `entry` from
[`@quiverlauncher/api`](../api) (or anything shaped like `CardEntry`).

## Colours

`catalog.css` sets layout and type, and takes every colour from a `--ql-*` variable, so each app keeps its own
palette and light/dark themes. Define these, for example on `:root`:

| Variable | Used for |
| --- | --- |
| `--ql-card`, `--ql-card-line`, `--ql-card-shadow` | A card's background, border, and shadow when hovered |
| `--ql-accent` | A hovered or focused card's border |
| `--ql-cover-1` … `--ql-cover-4` | Backgrounds behind card art, in turn |
| `--ql-artwork-icon`, `--ql-artwork-shadow` | The placeholder icon, and the shadow under icon art |
| `--ql-arrow-line`, `--ql-arrow-bg`, `--ql-arrow-fg` | The arrow on a card's cover |
| `--ql-badge-bg`, `--ql-badge-fg` | The "New" (or other) badge |
| `--ql-card-kind`, `--ql-separator`, `--ql-platform-icons` | The type and console line, its dots, and platform icons |
| `--ql-ai-chip-line`, `--ql-ai-chip-fg` | The AI chip |
| `--ql-based-on-label`, `--ql-chip-fg`, `--ql-chip-bg`, `--ql-chip-line` | "Based on" and its game chips |
| `--ql-card-meta`, `--ql-release-none` | The bottom row's text, and "No releases" |
| `--ql-positive`, `--ql-caution`, `--ql-negative`, `--ql-score-muted` | Scores, and a stale release (caution) |
| `--ql-line`, `--ql-line-strong` | Dividers and markdown rules, tables and quote bars |
| `--ql-md-text`, `--ql-md-heading`, `--ql-md-quote`, `--ql-md-th` | Markdown text, headings, quotes and table headers |
| `--ql-link`, `--ql-link-underline` | Markdown links |
| `--ql-code-bg`, `--ql-pre-bg` | Inline code and code blocks |

## Developing

`pnpm --filter @quiverlauncher/ui test` runs its tests.

MIT licensed.
