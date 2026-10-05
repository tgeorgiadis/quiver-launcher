/** README and release-notes rendering, shared by Quiver Launcher and quiverlauncher.com. */
import ReactMarkdown, { defaultUrlTransform } from "react-markdown";
import rehypeRaw from "rehype-raw";
import rehypeSanitize, { defaultSchema } from "rehype-sanitize";
import remarkGfm from "remark-gfm";

const alignable = ["div", "p", "h1", "h2", "h3", "h4", "img", "td", "th"];
const schema = {
  ...defaultSchema,
  attributes: {
    ...defaultSchema.attributes,
    ...Object.fromEntries(
      alignable.map((tag) => [
        tag,
        [...(defaultSchema.attributes?.[tag] ?? []), "align"],
      ]),
    ),
    img: [...(defaultSchema.attributes?.img ?? []), "align", "width", "height"],
  },
};

type HastNode = {
  type: string;
  tagName?: string;
  properties?: { className?: unknown; [key: string]: unknown };
  children?: HastNode[];
};

const LIGHT_ONLY = "#gh-light-mode-only";
const DARK_ONLY = "#gh-dark-mode-only";
/** The first URL in a srcset, whichever way the parser stored it. */
function firstSrc(srcSet: unknown) {
  const text = Array.isArray(srcSet) ? srcSet.join(",") : String(srcSet ?? "");
  return text.split(",")[0]?.trim().split(/\s+/)[0] ?? "";
}
const isDarkSource = (node: HastNode) =>
  node.type === "element" &&
  node.tagName === "source" &&
  /prefers-color-scheme:\s*dark/i.test(String(node.properties?.media ?? ""));

/**
 * READMEs often show a logo twice, one for GitHub's light theme and one for
 * its dark theme, and GitHub hides the one that doesn't match. This site is
 * always dark, so keep only the dark one: drop images marked
 * #gh-light-mode-only, and give a <picture> its dark-scheme source.
 */
function rehypeDarkThemeImages() {
  const visit = (node: HastNode): HastNode | null => {
    if (node.type === "element" && node.tagName === "img") {
      const src = String(node.properties?.src ?? "");
      if (src.endsWith(LIGHT_ONLY)) return null;
      if (src.endsWith(DARK_ONLY))
        node.properties = {
          ...node.properties,
          src: src.slice(0, -DARK_ONLY.length),
        };
      return node;
    }
    if (node.type === "element" && node.tagName === "picture") {
      const img = node.children?.find(
        (c) => c.type === "element" && c.tagName === "img",
      );
      if (!img) return null;
      const dark = node.children?.find(isDarkSource);
      const src = dark && firstSrc(dark.properties?.srcSet);
      // The picture becomes its image, showing the dark source if it has one.
      return {
        ...img,
        properties: { ...img.properties, ...(src ? { src } : {}) },
      };
    }
    if (node.children)
      node.children = node.children
        .map(visit)
        .filter((c): c is HastNode => c !== null);
    return node;
  };
  return (tree: HastNode) => {
    visit(tree);
  };
}

/**
 * READMEs sit under the page's own h1 and section h2, so their headings start
 * at h3, and each is at most one level deeper than the one before it. That
 * keeps the page outline in order even when an author jumps from # to ###.
 * The md-hN class keeps each heading's original look.
 */
const isBlank = (node: HastNode) =>
  node.type === "text" && !String((node as { value?: unknown }).value).trim();
const hasAlign = (node?: HastNode) => !!node?.properties?.align;

/**
 * Centres an image that stands alone in a paragraph, on its own or as a
 * link. Big README images are capped narrower than the panel, so left-aligned
 * they look stranded. Rows of badges, images in text and anything the author
 * aligned themselves are left as they are.
 */
/** The image a node shows on its own: an image, or a link around just one. */
function loneImage(node: HastNode | null | undefined) {
  if (node?.type !== "element") return undefined;
  if (node.tagName === "img") return node;
  const inner = (node.children ?? []).filter((c) => !isBlank(c));
  return node.tagName === "a" &&
    inner.length === 1 &&
    inner[0].tagName === "img"
    ? inner[0]
    : undefined;
}
function rehypeCentreLoneImages() {
  return (tree: HastNode) => {
    // An <img> on its own line is an HTML block, not in a paragraph: give it
    // one, so it centres like a Markdown image.
    tree.children = tree.children?.map((child) => {
      const image = loneImage(child);
      return image && !hasAlign(image) && !hasAlign(child)
        ? {
            type: "element",
            tagName: "p",
            properties: { className: ["md-lone-image"] },
            children: [child],
          }
        : child;
    });
    const walk = (node: HastNode) => {
      if (node.type === "element" && node.tagName === "p" && !hasAlign(node)) {
        const content = (node.children ?? []).filter((c) => !isBlank(c));
        const image = content.length === 1 ? loneImage(content[0]) : undefined;
        if (image && !hasAlign(image)) {
          const existing = node.properties?.className;
          node.properties = {
            ...node.properties,
            className: [
              ...(Array.isArray(existing) ? existing : []),
              "md-lone-image",
            ],
          };
        }
      }
      node.children?.forEach(walk);
    };
    walk(tree);
  };
}

function rehypeNestHeadings() {
  return (tree: HastNode) => {
    // The open sections: each author heading level and the level it became.
    const open: { original: number; level: number }[] = [];
    const walk = (node: HastNode) => {
      const match = node.type === "element" && /^h([1-6])$/.exec(node.tagName!);
      if (match) {
        const original = Number(match[1]);
        while (open.length && open[open.length - 1].original >= original)
          open.pop();
        const level = Math.min((open[open.length - 1]?.level ?? 2) + 1, 6);
        node.tagName = `h${level}`;
        const existing = node.properties?.className;
        node.properties = {
          ...node.properties,
          className: [
            ...(Array.isArray(existing) ? existing : []),
            `md-h${original}`,
          ],
        };
        open.push({ original, level });
      }
      node.children?.forEach(walk);
    };
    walk(tree);
  };
}

const GITHUB_BLOB = /^https:\/\/github\.com\/([^/]+)\/([^/]+)\/blob\/(.+)$/;

/** Resolves README-relative URLs: images against the raw file host, links against the web view. Unsafe schemes are dropped. */
export function resolveUrl(
  url: string,
  key: string,
  bases: { rawBase?: string; htmlBase?: string },
) {
  const safe = defaultUrlTransform(url);
  if (!safe || safe.startsWith("#")) return safe;
  const isImage = key === "src";
  if (/^[a-z][a-z0-9+.-]*:|^\/\//i.test(safe)) {
    const blob = isImage ? GITHUB_BLOB.exec(safe) : null;
    return blob
      ? `https://raw.githubusercontent.com/${blob[1]}/${blob[2]}/${blob[3]}`
      : safe;
  }
  const base = isImage ? bases.rawBase : bases.htmlBase;
  if (!base) return safe;
  try {
    return new URL(safe.replace(/^\/+/, ""), base).href;
  } catch {
    return "";
  }
}

export function Markdown({
  text,
  rawBase,
  htmlBase,
  className = "",
}: {
  text: string;
  rawBase?: string;
  htmlBase?: string;
  className?: string;
}) {
  return (
    <div className={`markdown ${className}`}>
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        rehypePlugins={[
          rehypeRaw,
          // Before sanitising, which drops <picture> and <source>.
          rehypeDarkThemeImages,
          [rehypeSanitize, schema],
          rehypeNestHeadings,
          // After sanitising, so its class isn't stripped.
          rehypeCentreLoneImages,
        ]}
        urlTransform={(url, key) => resolveUrl(url, key, { rawBase, htmlBase })}
        components={{
          a: ({ node: _node, href, ...props }) =>
            href?.startsWith("#") ? (
              <a href={href} {...props} />
            ) : (
              <a href={href} target="_blank" rel="noreferrer" {...props} />
            ),
          img: ({ node: _node, alt, ...props }) => (
            <img alt={alt ?? ""} loading="lazy" {...props} />
          ),
        }}
      >
        {text}
      </ReactMarkdown>
    </div>
  );
}
