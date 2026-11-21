// @vitest-environment jsdom
import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Markdown, readmeImages, resolveUrl } from "./markdown";
import source from "./markdown.tsx?raw";

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

describe("store badges", () => {
  const show = (text: string) => render(<Markdown {...bases} text={text} />).container;
  it("hides launcher and store badges, and the row they leave empty", () => {
    const page = show(
      [
        "# Game",
        "",
        "[![Get it on a launcher](https://launcher.example/badge.svg)](https://www.quiverlauncher.com/app/game)",
        "[![Verified on a store](https://cdn.example/v.svg)](https://store.steampowered.com/app/1)",
        "",
        "A native port.",
      ].join("\n"),
    );
    expect(page.querySelectorAll("img")).toHaveLength(0);
    expect([...page.querySelectorAll("p")].map((p) => p.textContent)).toEqual([
      "A native port.",
    ]);
  });
  it("hides an HTML badge, on a store's subdomain too", () => {
    const page = show(
      '<p align="center"><a href="https://someone.itch.io/game"><img src="itch.svg"></a></p>\n\nText.',
    );
    expect(page.querySelectorAll("img")).toHaveLength(0);
    expect(page.textContent).toContain("Text.");
  });
  it("keeps text links to stores, other badges and the project's own links", () => {
    const page = show(
      [
        "Also on [Steam](https://store.steampowered.com/app/1).",
        "",
        "[![Build](https://img.shields.io/b.svg)](https://github.com/o/r/actions)",
        "[![Discord](d.svg)](https://discord.gg/x) [![Docs](docs.png)](docs/README.md)",
      ].join("\n"),
    );
    expect(page.querySelector('a[href="https://store.steampowered.com/app/1"]')?.textContent).toBe("Steam");
    expect(page.querySelectorAll("img")).toHaveLength(3);
  });
  it("keeps a store badge that's part of a sentence's words", () => {
    const page = show("[Get it ![](icon.png) on GOG](https://www.gog.com/game/x)");
    expect(page.querySelectorAll("img")).toHaveLength(1);
  });
});

describe("alerts", () => {
  const alert = (text: string) =>
    render(<Markdown text={text} />).container.querySelector(".md-alert");

  it("turns a [!IMPORTANT] blockquote into a titled callout, as GitHub does", () => {
    const el = alert("> [!IMPORTANT]\n> This project is made with the help of AI agents.");
    expect(el?.className).toBe("md-alert md-alert-important");
    expect(el?.querySelector("blockquote")).toBeNull();
    const title = el?.querySelector(".md-alert-title");
    expect(title?.textContent).toBe("Important");
    expect(title?.querySelector("svg")).not.toBeNull();
    const body = [...el!.querySelectorAll("p:not(.md-alert-title)")];
    expect(body.map((p) => p.textContent)).toEqual([
      "This project is made with the help of AI agents.",
    ]);
  });

  it("knows every kind, in any case, with the marker on a line of its own", () => {
    for (const kind of ["note", "tip", "warning", "caution"])
      expect(alert(`> [!${kind.toUpperCase()}]\n>\n> Text`)?.className).toBe(
        `md-alert md-alert-${kind}`,
      );
    expect(alert("> [!Tip]\n> Text")?.className).toBe("md-alert md-alert-tip");
  });

  it("headings the callout in the page's language when given one", () => {
    const { container } = render(
      <Markdown text={"> [!WARNING]\n> Text"} alertTitles={{ warning: "Warnung" }} />,
    );
    expect(container.querySelector(".md-alert-title")?.textContent).toBe("Warnung");
  });

  it("leaves other blockquotes as they are", () => {
    expect(alert("> Just a quote")).toBeNull();
    expect(alert("> [!IMPORTANT] on the same line")).toBeNull();
    expect(alert("> [!UNKNOWN]\n> Text")).toBeNull();
  });
});

// quiverlauncher.com's Worker renders READMEs too, bundled by Wrangler, which
// runs no Lingui macros and has no import.meta.glob: either fails it on load.
describe("markdown.tsx", () => {
  it("uses neither Lingui's macros nor @quiverlauncher/i18n", () => {
    expect(source).not.toMatch(/from\s+["'](@lingui\/[^"']*macro|@quiverlauncher\/i18n)["']/);
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

describe("a README's pictures, for a copy kept offline", () => {
  const bases = { rawBase: "https://raw.githubusercontent.com/dev/game/main/", htmlBase: "https://github.com/dev/game/blob/main/" };
  it("lists its Markdown images, HTML <img>s and dark-theme pictures, resolved as they're shown, once each", () => {
    const text = [
      "![Logo](res/logo.png)",
      '<img src="docs/shot.jpg" width="400">',
      '<picture><source media="(prefers-color-scheme: dark)" srcset="res/logo-dark.png"><img src="res/logo.png"></picture>',
      "![badge](https://img.shields.io/badge/x-y-blue.svg)",
      "![blob](https://github.com/dev/game/blob/main/a.gif)",
      "[a link](docs/readme.md) and ![](data:image/png;base64,AAAA)",
    ].join("\n");
    expect(readmeImages(text, bases)).toEqual([
      "https://raw.githubusercontent.com/dev/game/main/res/logo.png",
      "https://img.shields.io/badge/x-y-blue.svg",
      "https://raw.githubusercontent.com/dev/game/main/a.gif",
      "https://raw.githubusercontent.com/dev/game/main/docs/shot.jpg",
      "https://raw.githubusercontent.com/dev/game/main/res/logo-dark.png",
    ]);
  });
  it("shows the kept copy in place of the picture it links to", () => {
    const { container } = render(<Markdown text="![Logo](res/logo.png)" {...bases} images={{ "https://raw.githubusercontent.com/dev/game/main/res/logo.png": "data:image/png;base64,AAAA" }} />);
    expect(container.querySelector("img")?.getAttribute("src")).toBe("data:image/png;base64,AAAA");
  });
});
