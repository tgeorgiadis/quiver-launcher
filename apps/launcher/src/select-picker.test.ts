// @vitest-environment jsdom
import { afterEach, beforeAll, expect, test, vi } from "vitest";
import { closePicker, pickerPanel, startSelectLists } from "./select-picker";

vi.mock("./zoom", () => ({ boxZoom: () => 1, windowBox: () => ({ width: 1024, height: 768 }) }));

beforeAll(() => {
  // jsdom lays nothing out and has no scrolling.
  Element.prototype.scrollIntoView = () => {};
  startSelectLists();
});
afterEach(() => {
  closePicker(false);
  document.body.innerHTML = "";
});

function dropdown(...labels: string[]) {
  document.body.innerHTML = `<select aria-label="Sort">${labels.map((l) => `<option value="${l.toLowerCase()}">${l}</option>`).join("")}</select>`;
  return document.querySelector("select")!;
}
const press = (target: Element, init: KeyboardEventInit) => target.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ...init }));
const options = () => [...pickerPanel()!.querySelectorAll<HTMLButtonElement>("[role=option]")];

test("a click opens Quiver's list in place of the system's, the chosen option focused", () => {
  const select = dropdown("Name", "Newest", "Size");
  select.value = "newest";
  const down = new MouseEvent("mousedown", { bubbles: true, cancelable: true, button: 0 });
  select.dispatchEvent(down);
  expect(down.defaultPrevented).toBe(true);
  expect(pickerPanel()?.getAttribute("role")).toBe("listbox");
  expect(pickerPanel()?.getAttribute("aria-label")).toBe("Sort");
  expect(options().map((o) => o.textContent)).toEqual(["Name", "Newest", "Size"]);
  expect(document.activeElement).toBe(options()[1]);
  // A second click on the dropdown closes it.
  select.dispatchEvent(new MouseEvent("mousedown", { bubbles: true, cancelable: true, button: 0 }));
  expect(pickerPanel()).toBeNull();
});

test("picking sends the change a select's own list does, and the focus goes back", () => {
  const select = dropdown("Name", "Newest", "Size");
  const changed = vi.fn();
  select.addEventListener("change", () => changed(select.value));
  press(select, { key: " " });
  options()[2].click();
  expect(changed).toHaveBeenCalledWith("size");
  expect(pickerPanel()).toBeNull();
  expect(document.activeElement).toBe(select);
});

test("the keys that open a dropdown open it; a letter goes to the next option with it", () => {
  const select = dropdown("Name", "Newest", "Size");
  for (const key of ["Enter", "F4"]) {
    press(select, { key });
    expect(pickerPanel()).not.toBeNull();
    closePicker();
  }
  press(select, { key: "ArrowDown", altKey: true });
  press(document.activeElement!, { key: "n" });
  expect(document.activeElement?.textContent).toBe("Newest");
  press(document.activeElement!, { key: "End" });
  expect(document.activeElement?.textContent).toBe("Size");
});

test("in an open dialog the list goes inside it, out of a modal's reach otherwise", () => {
  document.body.innerHTML = `<dialog open><select><option>One</option><option>Two</option></select></dialog>`;
  const select = document.querySelector("select")!;
  press(select, { key: "Enter" });
  expect(pickerPanel()?.parentElement).toBe(document.querySelector("dialog"));
});

test("a disabled dropdown and a list box keep their own ways", () => {
  const select = dropdown("Name", "Size");
  select.disabled = true;
  select.dispatchEvent(new MouseEvent("mousedown", { bubbles: true, cancelable: true, button: 0 }));
  expect(pickerPanel()).toBeNull();
  select.disabled = false;
  select.multiple = true;
  press(select, { key: "Enter" });
  expect(pickerPanel()).toBeNull();
});
