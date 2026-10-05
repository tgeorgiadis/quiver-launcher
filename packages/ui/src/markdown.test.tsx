// @vitest-environment jsdom
import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Markdown, resolveUrl } from "./markdown";

const bases = {
  rawBase: "https://raw.githubusercontent.com/o/r/HEAD/",
  htmlBase: "https://github.com/o/r/blob/HEAD/",
};

describe("resolveUrl", () => {
  it("resolves relative images against the raw host and links against the web view", () => {
    expect(resolveUrl("docs/shot.png", "src", bases)).toBe(
      "https://raw.githubusercontent.com/o/r/HEAD/docs/shot.png",
    );
    expect(resolveUrl("./BUILDING.md", "href", bases)).toBe(
      "https://github.com/o/r/blob/HEAD/BUILDING.md",
    );
    expect(resolveUrl("/assets/logo.png", "src", bases)).toBe(
      "https://raw.githubusercontent.com/o/r/HEAD/assets/logo.png",
    );
  });

  it("keeps anchors and absolute URLs, and serves GitHub blob images raw", () => {
    expect(resolveUrl("#install", "href", bases)).toBe("#install");
    expect(resolveUrl("https://example.com/x", "href", bases)).toBe(
      "https://example.com/x",
    );
    expect(
      resolveUrl("https://github.com/o/r/blob/main/a.png", "src", bases),
    ).toBe("https://raw.githubusercontent.com/o/r/main/a.png");
  });

  it("drops unsafe schemes", () => {
    expect(resolveUrl("javascript:alert(1)", "href", bases)).toBe("");
  });
});

describe("Markdown", () => {
  it("renders README HTML but strips scripts and event handlers", () => {
    const { container } = render(
      <Markdown
        {...bases}
        text={`<p align="center"><img src="logo.png" onerror="alert(1)"></p>\n\n<script>alert(1)</script>\n\n[Guide](guide.md)`}
      />,
    );
    const img = container.querySelector("img");
    expect(img?.getAttribute("src")).toBe(
      "https://raw.githubusercontent.com/o/r/HEAD/logo.png",
    );
    expect(img?.hasAttribute("onerror")).toBe(false);
    expect(container.querySelector("p")?.getAttribute("align")).toBe("center");
    expect(container.querySelector("script")).toBeNull();
    const link = container.querySelector("a");
    expect(link?.getAttribute("href")).toBe(
      "https://github.com/o/r/blob/HEAD/guide.md",
    );
    expect(link?.getAttribute("target")).toBe("_blank");
  });
});

describe("README headings", () => {
  it("start at h3 and close gaps the author left, keeping the original look", () => {
    // ### Build closes #### Windows and is a sibling of ### Setup again.
    const { container } = render(
      <Markdown text={"# Title\n\n### Setup\n\n#### Windows\n\n### Build"} />,
    );
    const headings = [...container.querySelectorAll("h3, h4, h5, h6")].map(
      (h) => `${h.tagName.toLowerCase()}.${h.className}`,
    );
    expect(headings).toEqual(["h3.md-h1", "h4.md-h3", "h5.md-h4", "h4.md-h3"]);
  });
  it("keeps sibling sections at the same level", () => {
    const { container } = render(
      <Markdown text={"# A\n\n## B\n\n# C\n\n## D"} />,
    );
    expect(
      [...container.querySelectorAll("h3, h4")].map((h) => h.tagName),
    ).toEqual(["H3", "H4", "H3", "H4"]);
  });
});

describe("theme images", () => {
  it("keeps only the dark-mode logo, as GitHub's dark theme does", () => {
    const { container } = render(
      <Markdown
        {...bases}
        text={[
          "![Ship of Harkinian](docs/shiptitle.darkmode.png#gh-dark-mode-only)",
          "![Ship of Harkinian](docs/shiptitle.lightmode.png#gh-light-mode-only)",
        ].join("\n")}
      />,
    );
    const srcs = [...container.querySelectorAll("img")].map((i) =>
      i.getAttribute("src"),
    );
    expect(srcs).toEqual([
      "https://raw.githubusercontent.com/o/r/HEAD/docs/shiptitle.darkmode.png",
    ]);
  });

  it("shows a <picture>'s dark-scheme source", () => {
    const { container } = render(
      <Markdown
        {...bases}
        text={[
          "<picture>",
          '  <source media="(prefers-color-scheme: dark)" srcset="./docs/poweredbylus.darkmode.png">',
          '  <img alt="Powered by libultraship" src="./docs/poweredbylus.lightmode.png">',
          "</picture>",
        ].join("\n")}
      />,
    );
    const imgs = container.querySelectorAll("img");
    expect(imgs).toHaveLength(1);
    expect(imgs[0].getAttribute("src")).toBe(
      "https://raw.githubusercontent.com/o/r/HEAD/docs/poweredbylus.darkmode.png",
    );
    expect(imgs[0].getAttribute("alt")).toBe("Powered by libultraship");
  });
});

describe("lone images", () => {
  const centred = (text: string) => {
    const { container } = render(<Markdown {...bases} text={text} />);
    // Per image: whether it's in a centred lone-image block.
    return [...container.querySelectorAll("img")].map(
      (img) => !!img.closest(".md-lone-image"),
    );
  };
  it("centres an image on its own, plain or linked", () => {
    expect(
      centred(
        '<img width="1360" height="768" alt="Conker" src="banner.png" />',
      ),
    ).toEqual([true]);
    expect(centred("[![Logo](logo.png)](https://example.com)")).toEqual([true]);
  });
  it("leaves badge rows, images in text and author alignment alone", () => {
    expect(centred("![a](a.svg) ![b](b.svg)")).toEqual([false, false]);
    expect(centred("Built with ![x](x.png) inside a sentence.")).toEqual([
      false,
    ]);
    expect(centred('<img align="right" src="side.png">')).toEqual([false]);
  });
});

// READMEs come from anyone's repository, so this is the line between their
// HTML and the page: nothing in one may run script or restyle the page.
describe("untrusted README HTML", () => {
  const html = (text: string) =>
    render(<Markdown {...bases} text={text} />).container;

  it("drops script URLs from links, images and a <picture>'s sources", () => {
    const container = html(
      [
        '<a href="javascript:alert(1)">a</a>',
        '<a href="JaVaScRiPt:alert(1)">b</a>',
        '<img src="javascript:alert(1)">',
        "<picture>",
        '  <source media="(prefers-color-scheme: dark)" srcset="javascript:alert(1)">',
        '  <img alt="x" src="ok.png">',
        "</picture>",
      ].join("\n\n"),
    );
    for (const el of container.querySelectorAll("[href], [src], [srcset]"))
      for (const attr of ["href", "src", "srcset"])
        expect(el.getAttribute(attr) ?? "").not.toMatch(/javascript:/i);
  });

  it("removes frames, forms, styles and inline SVG script", () => {
    const container = html(
      [
        '<iframe src="https://example.com"></iframe>',
        '<form action="https://example.com"><button>Go</button></form>',
        '<div style="position:fixed;inset:0">cover</div>',
        "<style>body{display:none}</style>",
        '<svg><script>alert(1)</script></svg>',
        '<object data="x.swf"></object><embed src="x.swf">',
      ].join("\n\n"),
    );
    expect(
      container.querySelector("iframe, form, style, svg, script, object, embed"),
    ).toBeNull();
    expect(container.querySelector("[style]")).toBeNull();
  });

  it("prefixes ids and names so a README can't clobber the page's globals", () => {
    const container = html('<a id="root" name="root">x</a>');
    const link = container.querySelector("a")!;
    expect(link.getAttribute("id") ?? "").not.toBe("root");
    expect(link.getAttribute("name") ?? "").not.toBe("root");
  });
});
