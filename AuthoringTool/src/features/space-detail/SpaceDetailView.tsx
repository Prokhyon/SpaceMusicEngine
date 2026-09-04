import { useEffect, useMemo, useState } from "react";
import type { Coordinate, SpaceDefinition } from "../../model/types";
import { useProjectStore } from "../../store/useProjectStore";
import { CubeView } from "./CubeView";
import { TableView } from "./TableView";
import { LayersColumn } from "./LayerPanel";

type DisplayMode = "cube" | "table";

export function SpaceDetailView({ space }: { space: SpaceDefinition }) {
  const openGraph = useProjectStore((s) => s.openGraph);
  const updateSpace = useProjectStore((s) => s.updateSpace);
  const removeSpace = useProjectStore((s) => s.removeSpace);
  const addAxis = useProjectStore((s) => s.addAxis);
  const removeAxis = useProjectStore((s) => s.removeAxis);
  const updateAxis = useProjectStore((s) => s.updateAxis);
  const presets = useProjectStore((s) => s.project.presets);

  const [mode, setMode] = useState<DisplayMode>("cube");
  const [selectedCoordinate, setSelectedCoordinate] = useState<Coordinate | null>(null);
  // Indices des axes affichés dans le cube (max 3) ; le reste est figé par slider.
  const [displayedAxes, setDisplayedAxes] = useState<number[]>([]);
  const [fixedValues, setFixedValues] = useState<Record<number, number>>({});

  const axisCount = space.axes.length;

  // Garde les sélections cohérentes quand les axes changent.
  useEffect(() => {
    setDisplayedAxes((prev) => {
      const valid = prev.filter((i) => i < axisCount);
      const missing = [];
      for (let i = 0; i < axisCount && valid.length + missing.length < Math.min(3, axisCount); i++) {
        if (!valid.includes(i)) missing.push(i);
      }
      return [...valid, ...missing].slice(0, Math.min(3, axisCount));
    });
    setSelectedCoordinate((c) => (c && c.length === axisCount ? c : null));
    setFixedValues((f) => {
      const next: Record<number, number> = {};
      for (let i = 0; i < axisCount; i++) {
        next[i] = Math.min(f[i] ?? 0, space.axes[i].levelCount);
      }
      return next;
    });
  }, [axisCount, space.axes]);

  const hiddenAxes = useMemo(
    () => space.axes.map((_, i) => i).filter((i) => !displayedAxes.includes(i)),
    [space.axes, displayedAxes],
  );

  const cubeAvailable = axisCount >= 1;

  function swapDisplayedAxis(slot: number, newAxisIndex: number) {
    setDisplayedAxes((prev) => {
      const next = [...prev];
      const existingSlot = next.indexOf(newAxisIndex);
      if (existingSlot >= 0) {
        // Échange les deux axes plutôt que de dupliquer.
        next[existingSlot] = next[slot];
      }
      next[slot] = newAxisIndex;
      return next;
    });
    setSelectedCoordinate(null);
  }

  return (
    <div className="space-detail">
      <header className="detail-header">
        <button onClick={openGraph}>← Map</button>
        <input
          className="space-name-input"
          value={space.name}
          onChange={(e) => updateSpace(space.spaceId, { name: e.target.value })}
        />
        <div className="transport">
          <label>
            <input
              type="number"
              min={1}
              value={space.bpm}
              onChange={(e) => updateSpace(space.spaceId, { bpm: Number(e.target.value) })}
            />
            BPM
          </label>
          <label>
            <input
              type="number"
              min={1}
              value={space.beatsPerBar}
              onChange={(e) => updateSpace(space.spaceId, { beatsPerBar: Number(e.target.value) })}
            />
            temps/mesure
          </label>
          <label>
            <input
              type="number"
              min={1}
              value={space.barsPerLoop}
              onChange={(e) => updateSpace(space.spaceId, { barsPerLoop: Number(e.target.value) })}
            />
            mesures/boucle
          </label>
        </div>
        <div className="preset-select">
          Preset
          <select
            value={space.presetId ?? ""}
            onChange={(e) => updateSpace(space.spaceId, { presetId: e.target.value || undefined })}
          >
            <option value="">— aucun —</option>
            {presets.map((p) => (
              <option key={p.presetId} value={p.presetId}>
                {p.name}
              </option>
            ))}
          </select>
        </div>
        <div className="spacer" />
        {selectedCoordinate && (
          <div className="coord-chips">
            {space.axes.map((axis, i) => (
              <span key={axis.axisId} className="coord-chip">
                {axis.axisId}={selectedCoordinate[i]}
              </span>
            ))}
          </div>
        )}
        <button
          className="danger"
          onClick={() => {
            removeSpace(space.spaceId);
            openGraph();
          }}
        >
          Supprimer
        </button>
      </header>

      <div className="detail-body">
        <div className="detail-left">
          <div className="viz-area">
            {mode === "cube" && cubeAvailable && displayedAxes.length > 0 ? (
              <>
                <CubeView
                  space={space}
                  displayedAxes={displayedAxes}
                  fixedValues={fixedValues}
                  selectedCoordinate={selectedCoordinate}
                  onSelect={setSelectedCoordinate}
                />
                <div className="viz-hint">Glisser pour tourner · cliquer un point pour sélectionner</div>
              </>
            ) : mode === "cube" ? (
              <div className="table-warning">Ajoutez au moins un axe pour visualiser l'espace.</div>
            ) : (
              <TableView space={space} selectedCoordinate={selectedCoordinate} onSelect={setSelectedCoordinate} />
            )}
            <div className="viz-toggle mode-toggle square">
              <button className={`small${mode === "cube" ? " active" : ""}`} disabled={!cubeAvailable} onClick={() => setMode("cube")}>
                Cube
              </button>
              <button className={`small${mode === "table" ? " active" : ""}`} onClick={() => setMode("table")}>
                Tableau
              </button>
            </div>
          </div>

          <aside className="axes-panel">
            <div className="panel-header">
              <h3 className="section-title">Axes</h3>
              <button className="small" onClick={() => addAxis(space.spaceId)}>
                + Axe
              </button>
            </div>
            {space.axes.map((axis, i) => (
              <div key={i} className="axis-row">
                <input value={axis.axisId} onChange={(e) => updateAxis(space.spaceId, i, { axisId: e.target.value })} />
                <label>
                  0–
                  <input
                    type="number"
                    min={1}
                    max={16}
                    value={axis.levelCount}
                    onChange={(e) =>
                      updateAxis(space.spaceId, i, { levelCount: Math.max(1, Math.min(16, Number(e.target.value))) })
                    }
                  />
                </label>
                <button className="danger small" onClick={() => removeAxis(space.spaceId, i)}>
                  ✕
                </button>
              </div>
            ))}
            {axisCount === 0 && <p className="hint">Ajoutez au moins un axe pour visualiser l'espace.</p>}

            {mode === "cube" && axisCount > 3 && (
              <div className="slice-controls">
                <h4>Axes affichés (cube)</h4>
                {displayedAxes.map((axisIndex, slot) => (
                  <label key={slot} className="slice-select">
                    {["X", "Y", "Z"][slot]}
                    <select value={axisIndex} onChange={(e) => swapDisplayedAxis(slot, Number(e.target.value))}>
                      {space.axes.map((axis, i) => (
                        <option key={i} value={i}>
                          {axis.axisId}
                        </option>
                      ))}
                    </select>
                  </label>
                ))}
                <h4>Axes figés (tranche)</h4>
                {hiddenAxes.map((axisIndex) => (
                  <label key={axisIndex} className="slice-slider">
                    {space.axes[axisIndex].axisId} = {fixedValues[axisIndex] ?? 0}
                    <input
                      type="range"
                      min={0}
                      max={space.axes[axisIndex].levelCount}
                      step={1}
                      value={fixedValues[axisIndex] ?? 0}
                      onChange={(e) => {
                        const v = Number(e.target.value);
                        setFixedValues((f) => ({ ...f, [axisIndex]: v }));
                        setSelectedCoordinate(null);
                      }}
                    />
                  </label>
                ))}
              </div>
            )}
          </aside>
        </div>

        <LayersColumn space={space} selectedCoordinate={selectedCoordinate} />
      </div>
    </div>
  );
}
