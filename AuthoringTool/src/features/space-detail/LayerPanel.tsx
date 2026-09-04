import { useState } from "react";
import type { Coordinate, LayerDefinition, SpaceDefinition } from "../../model/types";
import {
  LAYER_VOLUME_MODES,
  QUANTIZATION_MODES,
  layerVolumeAt,
  type LayerVolumeMode,
  type QuantizationMode,
} from "../../model/types";
import { useProjectStore } from "../../store/useProjectStore";
import { IntervalInput } from "../../components/MetricInputs";
import { pickClip } from "../../persistence/clipPicker";

interface LayersColumnProps {
  space: SpaceDefinition;
  selectedCoordinate: Coordinate | null;
}

type ViewMode = "all" | "selected";

/** Colonne de droite de la vue Détail : liste des couches + éditeur flottant. */
export function LayersColumn({ space, selectedCoordinate }: LayersColumnProps) {
  const addLayer = useProjectStore((s) => s.addLayer);
  const [viewMode, setViewMode] = useState<ViewMode>("selected");
  const [editingIndex, setEditingIndex] = useState<number | null>(null);

  const hasSelection = !!selectedCoordinate;
  const effectiveMode: ViewMode = hasSelection ? viewMode : "all";

  const rows = space.layers.map((layer, index) => {
    const volume = selectedCoordinate ? layerVolumeAt(layer, space, selectedCoordinate) : null;
    return { layer, index, volume, active: volume === null ? null : volume > 0 };
  });
  const visibleRows = effectiveMode === "selected" ? rows.filter((r) => r.active) : rows;

  const editing = editingIndex !== null ? space.layers[editingIndex] : null;

  return (
    <div className="layers-column">
      <div className="layers-header">
        <h3 className="section-title">Couches</h3>
        <button className="small" onClick={() => addLayer(space.spaceId)}>
          + Couche
        </button>
      </div>
      <div className="layers-mode">
        <button className={effectiveMode === "all" ? "active" : ""} onClick={() => setViewMode("all")}>
          Toutes les couches
        </button>
        <button
          className={effectiveMode === "selected" ? "active" : ""}
          disabled={!hasSelection}
          onClick={() => hasSelection && setViewMode("selected")}
        >
          Point sélectionné
        </button>
      </div>
      {!hasSelection && <div className="no-selection-hint">Sélectionnez un point du cube pour filtrer par combinaison.</div>}

      <div className="layer-list">
        {space.layers.length === 0 && <p className="hint">Aucune couche dans cet espace.</p>}
        {visibleRows.map(({ layer, index, volume, active }) => (
          <div
            key={index}
            className={`layer-row${editingIndex === index ? " editing" : ""}`}
            onClick={() => setEditingIndex(index)}
          >
            <span className={`layer-dot ${active === null ? "" : active ? "on" : "off"}`} />
            <span className="name">{layer.layerId}</span>
            {layer.volumeMode === "VolumeEnvelope" && volume !== null && (
              <span className="volume-badge">{Math.round(volume * 100)}%</span>
            )}
            <span className="mode-label">{layer.volumeMode === "Stepped" ? "Stepped" : "Envelope"}</span>
            <span className="chevron">›</span>
          </div>
        ))}
        {effectiveMode === "selected" && visibleRows.length === 0 && space.layers.length > 0 && (
          <p className="hint">Aucune couche active pour cette combinaison.</p>
        )}
      </div>

      {editing && (
        <LayerEditorCard
          space={space}
          layer={editing}
          onClose={() => setEditingIndex(null)}
          onRemoved={() => setEditingIndex(null)}
        />
      )}
    </div>
  );
}

function LayerEditorCard({
  space,
  layer,
  onClose,
  onRemoved,
}: {
  space: SpaceDefinition;
  layer: LayerDefinition;
  onClose: () => void;
  onRemoved: () => void;
}) {
  const updateLayer = useProjectStore((s) => s.updateLayer);
  const removeLayer = useProjectStore((s) => s.removeLayer);
  const updateAxisFilter = useProjectStore((s) => s.updateAxisFilter);

  const patchEnvelope = (patch: Partial<LayerDefinition["volumeEnvelope"]>) =>
    updateLayer(space.spaceId, layer.layerId, { volumeEnvelope: { ...layer.volumeEnvelope, ...patch } });

  async function chooseClip() {
    try {
      const choice = await pickClip();
      if (choice) updateLayer(space.spaceId, layer.layerId, { clipPath: choice.clipPath, clipGuid: choice.clipGuid });
    } catch {
      // hors Tauri (navigateur) : édition manuelle du champ
    }
  }

  return (
    <div className="layer-editor-card">
      <div className="card-header">
        <input
          className="layer-id-input"
          value={layer.layerId}
          onChange={(e) => updateLayer(space.spaceId, layer.layerId, { layerId: e.target.value })}
        />
        <button className="small" onClick={onClose}>
          ✕
        </button>
      </div>

      <label className="field">
        Clip audio
        <div className="clip-row">
          <input
            value={layer.clipPath}
            placeholder="Music/forest_pads.wav"
            onChange={(e) => updateLayer(space.spaceId, layer.layerId, { clipPath: e.target.value, clipGuid: undefined })}
          />
          <button className="small" onClick={() => void chooseClip()}>
            …
          </button>
        </div>
      </label>

      <label className="field">
        Volume de base : {layer.baseVolume.toFixed(2)}
        <input
          type="range"
          min={0}
          max={1}
          step={0.01}
          value={layer.baseVolume}
          onChange={(e) => updateLayer(space.spaceId, layer.layerId, { baseVolume: Number(e.target.value) })}
        />
      </label>

      <label className="field">
        Quantization d'entrée
        <select
          value={layer.entryQuantization}
          onChange={(e) =>
            updateLayer(space.spaceId, layer.layerId, { entryQuantization: e.target.value as QuantizationMode })
          }
        >
          {QUANTIZATION_MODES.map((m) => (
            <option key={m} value={m}>
              {m}
            </option>
          ))}
        </select>
      </label>

      <label className="field">
        Mode de volume
        <select
          value={layer.volumeMode}
          onChange={(e) => updateLayer(space.spaceId, layer.layerId, { volumeMode: e.target.value as LayerVolumeMode })}
        >
          {LAYER_VOLUME_MODES.map((m) => (
            <option key={m} value={m}>
              {m === "Stepped" ? "Stepped (filtres par axe)" : "VolumeEnvelope (enveloppe continue)"}
            </option>
          ))}
        </select>
      </label>

      {layer.volumeMode === "VolumeEnvelope" ? (
        <div className="filters">
          <h4>Enveloppe de volume</h4>
          <label className="field">
            Axe pilote
            <select value={layer.volumeEnvelope.axisId} onChange={(e) => patchEnvelope({ axisId: e.target.value })}>
              <option value="">— aucun (volume plein) —</option>
              {space.axes.map((axis) => (
                <option key={axis.axisId} value={axis.axisId}>
                  {axis.axisId}
                </option>
              ))}
            </select>
          </label>
          <label className="check">
            <input
              type="checkbox"
              checked={layer.volumeEnvelope.fadeInEnabled}
              onChange={(e) => patchEnvelope({ fadeInEnabled: e.target.checked })}
            />
            Fade-in (côté bas)
          </label>
          {layer.volumeEnvelope.fadeInEnabled && (
            <div className="field-row">
              <label>
                début
                <input
                  type="number"
                  min={0}
                  max={1}
                  step={0.05}
                  value={layer.volumeEnvelope.fadeInStart}
                  onChange={(e) => patchEnvelope({ fadeInStart: Number(e.target.value) })}
                />
              </label>
              <label>
                plein à
                <input
                  type="number"
                  min={0}
                  max={1}
                  step={0.05}
                  value={layer.volumeEnvelope.fadeInEnd}
                  onChange={(e) => patchEnvelope({ fadeInEnd: Number(e.target.value) })}
                />
              </label>
            </div>
          )}
          <label className="check">
            <input
              type="checkbox"
              checked={layer.volumeEnvelope.fadeOutEnabled}
              onChange={(e) => patchEnvelope({ fadeOutEnabled: e.target.checked })}
            />
            Fade-out (côté haut)
          </label>
          {layer.volumeEnvelope.fadeOutEnabled && (
            <div className="field-row">
              <label>
                plein jusqu'à
                <input
                  type="number"
                  min={0}
                  max={1}
                  step={0.05}
                  value={layer.volumeEnvelope.fadeOutStart}
                  onChange={(e) => patchEnvelope({ fadeOutStart: Number(e.target.value) })}
                />
              </label>
              <label>
                silence à
                <input
                  type="number"
                  min={0}
                  max={1}
                  step={0.05}
                  value={layer.volumeEnvelope.fadeOutEnd}
                  onChange={(e) => patchEnvelope({ fadeOutEnd: Number(e.target.value) })}
                />
              </label>
            </div>
          )}
        </div>
      ) : (
        <div className="filters">
          <h4>Filtres d'axes</h4>
          {space.axes.map((axis, axisIndex) => {
            const filter = layer.axisFilters[axisIndex];
            if (!filter) return null;
            return (
              <div key={axis.axisId} className="filter-row">
                <label className="filter-toggle">
                  <input
                    type="checkbox"
                    checked={filter.enabled}
                    onChange={(e) =>
                      updateAxisFilter(space.spaceId, layer.layerId, axisIndex, { enabled: e.target.checked })
                    }
                  />
                  {axis.axisId}
                </label>
                {filter.enabled ? (
                  <span className="filter-range">
                    <input
                      type="number"
                      min={0}
                      max={axis.levelCount}
                      value={filter.minLevel}
                      onChange={(e) =>
                        updateAxisFilter(space.spaceId, layer.layerId, axisIndex, { minLevel: Number(e.target.value) })
                      }
                    />
                    –
                    <input
                      type="number"
                      min={0}
                      max={axis.levelCount}
                      value={filter.maxLevel}
                      onChange={(e) =>
                        updateAxisFilter(space.spaceId, layer.layerId, axisIndex, { maxLevel: Number(e.target.value) })
                      }
                    />
                  </span>
                ) : (
                  <span className="passes-all">passe tout</span>
                )}
              </div>
            );
          })}
        </div>
      )}

      <div className="filters">
        <label className="check">
          <input
            type="checkbox"
            checked={layer.loopTailEnabled}
            onChange={(e) => updateLayer(space.spaceId, layer.layerId, { loopTailEnabled: e.target.checked })}
          />
          Loop tail (résonance de fin de boucle)
        </label>
        {layer.loopTailEnabled && (
          <label className="field-inline">
            Durée
            <IntervalInput
              value={layer.loopTailLength}
              onChange={(loopTailLength) => updateLayer(space.spaceId, layer.layerId, { loopTailLength })}
            />
          </label>
        )}
      </div>

      <button
        className="danger"
        onClick={() => {
          removeLayer(space.spaceId, layer.layerId);
          onRemoved();
        }}
      >
        Supprimer la couche
      </button>
    </div>
  );
}
