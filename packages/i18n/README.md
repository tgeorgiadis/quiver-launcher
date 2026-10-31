# @quiverlauncher/i18n

Quiver Launcher's translations, shared with quiverlauncher.com. Every string
is written in English in the code; [Lingui](https://lingui.dev) gathers them
into one catalog per language in [`locales/`](locales), and a string with no
translation yet shows in English.

These catalogs hold the launcher's strings and the catalog components'
(`packages/ui`), which the site shows too. The site's own strings have
catalogs of their own, in its `src/locales/`, with the same languages; it
loads both.

| Language                | File                                     | Glossary                                       |
| ----------------------- | ---------------------------------------- | ---------------------------------------------- |
| English (source)        | [`locales/en.po`](locales/en.po)         |                                                |
| Português (Brasil)      | [`locales/pt-BR.po`](locales/pt-BR.po)   | [`glossary/pt-BR.md`](glossary/pt-BR.md)       |
| Español (Latinoamérica) | [`locales/es-419.po`](locales/es-419.po) | [`glossary/es-419.md`](glossary/es-419.md)     |
| Deutsch                 | [`locales/de.po`](locales/de.po)         | [`glossary/de.md`](glossary/de.md)             |
| Français                | [`locales/fr.po`](locales/fr.po)         | [`glossary/fr.md`](glossary/fr.md)             |
| Русский                 | [`locales/ru.po`](locales/ru.po)         | [`glossary/ru.md`](glossary/ru.md)             |
| Italiano                | [`locales/it.po`](locales/it.po)         | [`glossary/it.md`](glossary/it.md)             |
| 한국어                  | [`locales/ko.po`](locales/ko.po)         | [`glossary/ko.md`](glossary/ko.md)             |

The translations were first made by machine. A native speaker reading one
over and fixing what reads wrong is the most useful contribution there is.

## Translating

Each catalog is a [PO file](https://www.gnu.org/software/gettext/manual/html_node/PO-Files.html):
the English in `msgid`, its translation in `msgstr`. Edit it in a text editor
or in [Poedit](https://poedit.net), which shows what's missing.

```po
#. Interface size
#: ../../apps/launcher/src/settings-page.tsx
msgid "Large"
msgstr "Grande"
```

- `#.` lines say what a short string is for; `#:` where it's used.
- Keep `{name}` placeholders as they are, words around them in whatever order
  the language needs. `{count, plural, one {# app} other {# apps}}` picks a
  form by number: translate the words inside each branch, keep `#` (the
  number) and the branch names. Give the forms your language has: Russian
  `one`, `few`, `many` and `other`; Korean only `other`.
- `<0>…</0>` wraps a link or bold text: keep the tags round the words they
  belong to.
- Leave product and platform names alone: Quiver, Quiver Launcher, GitHub,
  GitLab, Steam, Steam Deck, Flatpak, AppImage.
- Address the player as the English does, friendly and plain.
- A language's glossary in [`glossary/`](glossary) (like
  [pt-BR's](glossary/pt-BR.md)) keeps its words the same throughout: follow
  it, and add to it.

### A new language

Add it to `LANGUAGES` in [`src/languages.ts`](src/languages.ts) (its BCP 47
code and its name in itself), then run `pnpm --filter @quiverlauncher/i18n
extract` to make its file, and `pnpm i18n:extract` in the site for its own.

### In the code

`activate(language, more)` loads a language's catalogs and shows it; the site
passes its own catalogs as `more`. `pick(navigator.languages)` is the nearest
language of ours to a visitor's. Catalogs are read as they are (`parsePo`), so
no build step compiles them.

## Writing strings in the code

The Lingui macros turn these into catalog lookups at build time:

```tsx
import { msg, plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";

<h3><Trans>Restore from backup</Trans></h3>               // text in JSX
<nav aria-label={t`Settings sections`}>                    // attributes, strings in code
t`Moved ${plural(count, { one: "# app", other: "# apps" })}.` // a number's forms
t({ message: "Large", comment: "Interface size" })        // a short word, explained for translators
const SECTIONS = [msg`General`];  …  i18n._(SECTIONS[0])  // defined at load, shown later
```

- A placeholder is a plain name (`{dir}`, not `{config.appsDir}`), so the
  catalog reads `Apps are installed in {dir}`: name it in a `const` first.
- Whole sentences, never pieces joined together, so a language can reorder
  them; a plural as `plural()`, never `n === 1 ? "app" : "apps"`.
- Nothing translated at a module's top level: it runs before a language is
  picked. Use `msg` there and `i18n._()` where it shows.
- Dates and numbers in the language shown: `i18n.locale` for `Intl` and
  `toLocale…String`, never `"en"` or `undefined`.
- Not translated: usage-data event names and properties, logs, stored keys,
  and text from the catalog or GitHub (app names, descriptions, release notes).

After changing strings, run `pnpm --filter @quiverlauncher/i18n extract` and
commit the catalogs with the change.
