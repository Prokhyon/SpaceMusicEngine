import { useEffect, useState } from "react";
import { useActiveSpace, useProjectStore } from "./store/useProjectStore";
import { loadProject, saveProject } from "./persistence/projectFile";
import { GraphView } from "./features/graph/GraphView";
import { SpaceDetailView } from "./features/space-detail/SpaceDetailView";
import { PresetsView } from "./features/presets/PresetsView";
import "./App.css";

function App() {
  const view = useProjectStore((s) => s.view);
  const projectName = useProjectStore((s) => s.project.name);
  const setProjectName = useProjectStore((s) => s.setProjectName);
  const filePath = useProjectStore((s) => s.filePath);
  const dirty = useProjectStore((s) => s.dirty);
  const newProject = useProjectStore((s) => s.newProject);
  const openGraph = useProjectStore((s) => s.openGraph);
  const openPresets = useProjectStore((s) => s.openPresets);
  const activeSpace = useActiveSpace();
  const [error, setError] = useState<string | null>(null);

  async function run(action: () => Promise<void>) {
    try {
      setError(null);
      await action();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key === "s") {
        e.preventDefault();
        void run(() => saveProject(e.shiftKey));
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  return (
    <div className="app">
      <header className="app-toolbar">
        <span className="app-title">Loris Audio Authoring</span>
        <input
          className="project-name-input"
          value={projectName}
          onChange={(e) => setProjectName(e.target.value)}
        />
        <span className={`dirty-indicator${dirty ? " dirty" : ""}`} title={filePath ?? "Non sauvegardé"}>
          {dirty ? "● modifié" : filePath ? "sauvegardé" : "nouveau"}
        </span>
        <div className="mode-toggle">
          <button className={view.kind !== "presets" ? "active" : ""} onClick={openGraph}>
            Graphe
          </button>
          <button className={view.kind === "presets" ? "active" : ""} onClick={openPresets}>
            Presets
          </button>
        </div>
        <div className="spacer" />
        <button onClick={() => newProject()}>Nouveau</button>
        <button onClick={() => void run(loadProject)}>Ouvrir…</button>
        <button onClick={() => void run(() => saveProject())}>Sauvegarder</button>
        <button onClick={() => void run(() => saveProject(true))}>Sauvegarder sous…</button>
      </header>
      {error && (
        <div className="error-banner" onClick={() => setError(null)}>
          {error} (cliquer pour fermer)
        </div>
      )}
      <div className="app-content">
        {view.kind === "presets" ? (
          <PresetsView />
        ) : view.kind === "space-detail" && activeSpace ? (
          <SpaceDetailView space={activeSpace} />
        ) : (
          <GraphView />
        )}
      </div>
    </div>
  );
}

export default App;
