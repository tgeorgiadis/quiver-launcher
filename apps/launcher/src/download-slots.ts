/**
 * How many downloads run at once (Settings › App updates): Update all, or several installs asked together, start that
 * many and the rest wait their turn, in the order they were asked. Each running download shares the connection and
 * the disk with the others, so starting every one at once finished none sooner and kept the computer busier.
 */
export const DOWNLOADS_AT_ONCE = 3;
/** What Settings offers. */
export const DOWNLOADS_AT_ONCE_CHOICES = [1, 2, 3, 4, 5, 10] as const;

type Waiting = { id: string; go: () => void; stop: () => void };

/** Slots for downloads: `limit` is read each time one could start, so a changed setting counts straight away. */
export function downloadSlots(limit: () => number) {
  let running = 0;
  const waiting: Waiting[] = [];
  const next = () => {
    while (waiting.length && running < Math.max(1, limit())) {
      running++;
      waiting.shift()!.go();
    }
  };
  return {
    /**
     * Runs `work` once a slot is free. `onWait` is called if it has to wait first. Rejects with "cancelled", as an
     * install does, when `cancel` takes it out of the queue.
     */
    async run<T>(id: string, work: () => Promise<T>, onWait?: () => void): Promise<T> {
      await new Promise<void>((go, stop) => {
        waiting.push({ id, go, stop: () => stop("cancelled") });
        next();
        if (waiting.some((w) => w.id === id)) onWait?.();
      });
      try {
        return await work();
      } finally {
        running--;
        next();
      }
    },
    /** Takes a waiting download out of the queue: true if it was waiting, false if it isn't (it may be running). */
    cancel(id: string) {
      const at = waiting.findIndex((w) => w.id === id);
      if (at < 0) return false;
      waiting.splice(at, 1)[0].stop();
      return true;
    },
    /** A changed setting: as many more start as it now allows. */
    changed: next,
  };
}
