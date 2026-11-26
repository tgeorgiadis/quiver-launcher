import { describe, expect, it } from "vitest";
import { downloadSlots } from "./download-slots";

/** A download that finishes when `finish` is called. */
function download() {
  let finish!: () => void;
  const done = new Promise<void>((r) => (finish = r));
  return { finish, work: () => done };
}
const tick = () => new Promise((r) => setTimeout(r));

describe("downloadSlots", () => {
  it("runs as many as the limit at once, and the rest in the order asked as slots free", async () => {
    const slots = downloadSlots(() => 2);
    const started: string[] = [];
    const waited: string[] = [];
    const d = { a: download(), b: download(), c: download(), d: download() };
    const runs = Object.entries(d).map(([id, x]) => slots.run(id, () => (started.push(id), x.work()), () => waited.push(id)));
    await tick();
    expect(started).toEqual(["a", "b"]);
    expect(waited).toEqual(["c", "d"]);
    d.b.finish();
    await tick();
    expect(started).toEqual(["a", "b", "c"]);
    d.a.finish();
    d.c.finish();
    d.d.finish();
    await Promise.all(runs);
    expect(started).toEqual(["a", "b", "c", "d"]);
  });

  it("takes a waiting one out of the queue as cancelled, and leaves a running one to the install", async () => {
    const slots = downloadSlots(() => 1);
    const a = download();
    const running = slots.run("a", a.work);
    const waiting = slots.run("b", () => Promise.resolve());
    await tick();
    expect(slots.cancel("a")).toBe(false);
    expect(slots.cancel("b")).toBe(true);
    await expect(waiting).rejects.toBe("cancelled");
    a.finish();
    await running;
  });

  it("starts more straight away when the setting goes up", async () => {
    let limit = 1;
    const slots = downloadSlots(() => limit);
    const started: string[] = [];
    const a = download();
    const b = download();
    void slots.run("a", () => (started.push("a"), a.work()));
    void slots.run("b", () => (started.push("b"), b.work()));
    await tick();
    expect(started).toEqual(["a"]);
    limit = 3;
    slots.changed();
    await tick();
    expect(started).toEqual(["a", "b"]);
  });

  it("frees its slot when a download fails", async () => {
    const slots = downloadSlots(() => 1);
    await expect(slots.run("a", () => Promise.reject("missing"))).rejects.toBe("missing");
    await expect(slots.run("b", () => Promise.resolve(1))).resolves.toBe(1);
  });
});
