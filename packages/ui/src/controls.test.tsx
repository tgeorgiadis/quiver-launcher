// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render } from "@testing-library/react";
import { Button, Dialog, Field, Panel, buttonClass } from "./controls";

afterEach(cleanup);

describe("Button", () => {
  it("is a neutral button that doesn't submit a form unless asked to", () => {
    const { getByRole } = render(<Button>Open folder</Button>);
    const button = getByRole("button", { name: "Open folder" });
    expect(button.className).toBe("ql-button");
    expect(button.getAttribute("type")).toBe("button");
  });

  it("takes its look from the variant", () => {
    expect(buttonClass("main")).toBe("ql-button main");
    expect(buttonClass("danger", { wide: true, className: "mine" })).toBe("ql-button danger wide mine");
    const { getByRole } = render(<Button variant="text">Learn more</Button>);
    expect(getByRole("button").className).toBe("ql-button text");
  });

  it("can't be pressed while busy, and says what's happening", () => {
    const onClick = vi.fn();
    const { getByRole } = render(
      <Button variant="main" busy busyLabel="Installing…" onClick={onClick}>
        Install
      </Button>,
    );
    const button = getByRole("button", { name: "Installing…" }) as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    expect(button.getAttribute("aria-busy")).toBe("true");
    fireEvent.click(button);
    expect(onClick).not.toHaveBeenCalled();
  });

  it("is a link with href, and goes nowhere when off", () => {
    const { getByText, rerender } = render(
      <Button variant="main" href="/download/">
        Download
      </Button>,
    );
    const link = getByText("Download");
    expect(link.tagName).toBe("A");
    expect(link.getAttribute("href")).toBe("/download/");
    rerender(
      <Button variant="main" href="/download/" disabled>
        Download
      </Button>,
    );
    expect(getByText("Download").hasAttribute("href")).toBe(false);
    expect(getByText("Download").getAttribute("aria-disabled")).toBe("true");
  });
});

describe("Field", () => {
  it("labels its control and shows the hint", () => {
    const { getByLabelText, getByText } = render(
      <Field label="Language" hint="Used across Quiver">
        <select>
          <option>English</option>
        </select>
      </Field>,
    );
    expect(getByLabelText(/Language/).tagName).toBe("SELECT");
    expect(getByText("Used across Quiver").className).toBe("ql-field-hint");
  });

  it("shows what's wrong in place of the hint", () => {
    const { getByRole, queryByText } = render(
      <Field label="Name" hint="Shown on your profile" error="Enter a name">
        <input aria-invalid />
      </Field>,
    );
    expect(getByRole("alert").textContent).toBe("Enter a name");
    expect(queryByText("Shown on your profile")).toBeNull();
  });
});

describe("Panel", () => {
  it("is a section with its title as a heading", () => {
    const { getByRole, container } = render(
      <Panel title="Apps folders" heading="h2" aria-label="Apps folders">
        <p>Body</p>
      </Panel>,
    );
    expect(getByRole("heading", { level: 2, name: "Apps folders" }).className).toBe("ql-panel-title");
    expect(container.querySelector("section.ql-panel")).not.toBeNull();
  });
});

describe("Dialog", () => {
  const ask = (props: Partial<Parameters<typeof Dialog>[0]> = {}) => (
    <Dialog open title="Remove this app?" confirmLabel="Remove" cancelLabel="Cancel" onConfirm={() => {}} onCancel={() => {}} {...props}>
      Its saves stay on this computer.
    </Dialog>
  );

  it("shows nothing until it's open", () => {
    const { queryByText } = render(ask({ open: false }));
    expect(queryByText("Remove this app?")).toBeNull();
  });

  it("has the title, the sentence, and Cancel before the main button", () => {
    const { getByRole, getAllByRole, getByText } = render(ask());
    expect(getByRole("dialog", { hidden: true }).getAttribute("aria-labelledby")).toBe(getByText("Remove this app?").id);
    expect(getAllByRole("button", { hidden: true }).map((b) => b.textContent)).toEqual(["Cancel", "Remove"]);
    expect(getByText("Remove").className).toBe("ql-button main");
  });

  it("cancels from its button", () => {
    const onCancel = vi.fn();
    const { getByText } = render(ask({ onCancel }));
    fireEvent.click(getByText("Cancel"));
    expect(onCancel).toHaveBeenCalledOnce();
  });

  it("is busy until confirming is done", async () => {
    let done = () => {};
    const onConfirm = vi.fn(() => new Promise<void>((resolve) => (done = resolve)));
    const { getByText } = render(ask({ tone: "danger", onConfirm, busyLabel: "Removing…" }));
    expect(getByText("Remove").className).toBe("ql-button danger");
    fireEvent.click(getByText("Remove"));
    expect((getByText("Removing…") as HTMLButtonElement).disabled).toBe(true);
    expect((getByText("Cancel") as HTMLButtonElement).disabled).toBe(true);
    await act(async () => done());
    expect((getByText("Remove") as HTMLButtonElement).disabled).toBe(false);
  });
});
