/**
 * Play's question in TV mode, which of an app's programs starts (store.tsx's `choosingProgram`), with the controller:
 * the programs the player picked under Play with, or, when the app's folder doesn't say, the ones in it, and the pick
 * kept as its program. The desktop's is App.tsx's ProgramPrompt.
 */
import { useEffect, useState } from "react";
import { Play } from "lucide-react";
import { useLauncher } from "../../store";
import { useInputHandler } from "../context";
import { LAYER } from "../input/router";

export function ProgramPrompt() {
  const { choosingProgram: choice, chooseProgram, installs, catalog, play, setLaunch } = useLauncher();
  const [index, setIndex] = useState(0);
  useEffect(() => setIndex(0), [choice]);
  const programs = choice?.programs ?? [];

  const start = (program: string) => {
    if (!choice) return;
    chooseProgram(null);
    if (choice.remember && installs[choice.id]) setLaunch(choice.id, { ...installs[choice.id].launch, executable: program });
    void play(choice.id, program);
  };

  useInputHandler(
    {
      onAction(action) {
        if (action === "up") setIndex((i) => Math.max(0, i - 1));
        else if (action === "down") setIndex((i) => Math.min(programs.length - 1, i + 1));
        else if (action === "accept") start(programs[index]);
        else if (action === "back") chooseProgram(null);
      },
      hints: () => [
        { action: "accept", label: "Start this" },
        { action: "back", label: "Cancel" },
      ],
    },
    LAYER.dialog,
    programs.length > 0,
  );

  if (!choice || !programs.length) return null;
  const name = catalog[choice.id]?.projectName ?? "it";
  return (
    <div className="tv-dialog-backdrop" role="dialog" aria-modal="true" aria-label="Choose a program">
      <section className="tv-dialog">
        <div className="tv-dialog-body">
          <h2>Which program starts {name}?</h2>
          <p className="tv-dialog-lead">
            {choice.remember
              ? "Its folder has more than one program, and the catalog doesn't say which starts it here. Play remembers the one you pick."
              : "The programs you picked under Play with in its menu."}
          </p>
          <ul className="tv-asset-list">
            {programs.map((path, i) => (
              <li key={path} className={i === index ? "focused" : ""} onMouseMove={() => setIndex(i)} onClick={() => start(path)}>
                <Play aria-hidden="true" />
                <span>{path}</span>
              </li>
            ))}
          </ul>
        </div>
      </section>
    </div>
  );
}
