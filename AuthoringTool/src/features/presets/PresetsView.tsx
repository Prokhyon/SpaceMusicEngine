import { useMemo } from "react";
import type {
  AxisBinding,
  ContextPresetDefinition,
  CurveKey,
  MappingRule,
} from "../../model/types";
import {
  COMBINATION_MODES,
  MAPPING_MODES,
  type CombinationMode,
  type MappingMode,
} from "../../model/types";
import { createAxisBinding, createMappingRule, createThreshold } from "../../model/factories";
import { useProjectStore } from "../../store/useProjectStore";
import { refreshParameterCatalog } from "../../persistence/parameterCatalog";

export function PresetsView() {
  const presets = useProjectStore((s) => s.project.presets);
  const addPreset = useProjectStore((s) => s.addPreset);
  const catalog = useProjectStore((s) => s.parameterCatalog);
  const filePath = useProjectStore((s) => s.filePath);

  return (
    <div className="presets-view">
      <div className="presets-toolbar">
        <button onClick={addPreset}>+ Preset</button>
        <span className="hint">
          {catalog.length > 0
            ? `${catalog.length} paramètre(s) de jeu trouvés dans Assets/`
            : "Aucun paramètre trouvé — ouvrez un projet situé dans le dossier Assets/ d'un projet Unity."}
        </span>
        <button onClick={() => void refreshParameterCatalog(filePath).catch(() => {})} disabled={!filePath}>
          ⟳ Rescanner les paramètres
        </button>
      </div>
      <div className="presets-list">
        {presets.length === 0 && <p className="hint">Aucun preset. Un preset mappe les paramètres du jeu vers les axes d'un espace.</p>}
        {presets.map((preset, i) => (
          <PresetEditor key={i} preset={preset} />
        ))}
      </div>
    </div>
  );
}

function PresetEditor({ preset }: { preset: ContextPresetDefinition }) {
  const updatePreset = useProjectStore((s) => s.updatePreset);
  const removePreset = useProjectStore((s) => s.removePreset);
  const spaces = useProjectStore((s) => s.project.spaces);

  const usedBy = useMemo(
    () => spaces.filter((s) => s.presetId === preset.presetId).map((s) => s.name),
    [spaces, preset.presetId],
  );

  // Tous les axisId connus, pour suggérer les noms d'axes des bindings.
  const knownAxes = useMemo(
    () => [...new Set(spaces.flatMap((s) => s.axes.map((a) => a.axisId)))],
    [spaces],
  );

  const setBinding = (index: number, binding: AxisBinding) =>
    updatePreset(preset.presetId, {
      axisBindings: preset.axisBindings.map((b, i) => (i === index ? binding : b)),
    });

  return (
    <details className="preset-editor" open>
      <summary>
        <input
          className="layer-id-input"
          value={preset.name}
          onClick={(e) => e.stopPropagation()}
          onChange={(e) => updatePreset(preset.presetId, { name: e.target.value })}
        />
        <span className="hint">
          {usedBy.length > 0 ? `utilisé par : ${usedBy.join(", ")}` : "non utilisé"}
        </span>
        <button
          className="danger small"
          onClick={(e) => {
            e.preventDefault();
            removePreset(preset.presetId);
          }}
        >
          ✕
        </button>
      </summary>

      {preset.axisBindings.map((binding, bi) => (
        <div key={bi} className="binding">
          <div className="binding-header">
            <label>
              Axe
              <input
                list="known-axes"
                value={binding.axisName}
                placeholder="axisId de l'espace"
                onChange={(e) => setBinding(bi, { ...binding, axisName: e.target.value })}
              />
            </label>
            <label>
              Combinaison
              <select
                value={binding.combinationMode}
                onChange={(e) => setBinding(bi, { ...binding, combinationMode: e.target.value as CombinationMode })}
              >
                {COMBINATION_MODES.map((m) => (
                  <option key={m}>{m}</option>
                ))}
              </select>
            </label>
            <button
              className="danger small"
              onClick={() =>
                updatePreset(preset.presetId, { axisBindings: preset.axisBindings.filter((_, i) => i !== bi) })
              }
            >
              ✕
            </button>
          </div>
          {binding.rules.map((rule, ri) => (
            <RuleEditor
              key={ri}
              rule={rule}
              showWeight={binding.combinationMode === "WeightedSum"}
              onChange={(next) =>
                setBinding(bi, { ...binding, rules: binding.rules.map((r, i) => (i === ri ? next : r)) })
              }
              onRemove={() => setBinding(bi, { ...binding, rules: binding.rules.filter((_, i) => i !== ri) })}
            />
          ))}
          <button className="small" onClick={() => setBinding(bi, { ...binding, rules: [...binding.rules, createMappingRule()] })}>
            + Règle
          </button>
        </div>
      ))}
      <button onClick={() => updatePreset(preset.presetId, { axisBindings: [...preset.axisBindings, createAxisBinding()] })}>
        + Binding d'axe
      </button>
      <datalist id="known-axes">
        {knownAxes.map((a) => (
          <option key={a} value={a} />
        ))}
      </datalist>
    </details>
  );
}

function RuleEditor({
  rule,
  showWeight,
  onChange,
  onRemove,
}: {
  rule: MappingRule;
  showWeight: boolean;
  onChange: (next: MappingRule) => void;
  onRemove: () => void;
}) {
  const catalog = useProjectStore((s) => s.parameterCatalog);
  const known = catalog.find((p) => p.guid === rule.parameterGuid);

  const setKey = (index: number, key: CurveKey) =>
    onChange({ ...rule, curve: { ...rule.curve, keys: rule.curve.keys.map((k, i) => (i === index ? key : k)) } });

  return (
    <div className="rule">
      <div className="rule-header">
        <label>
          Paramètre
          <select
            value={rule.parameterGuid}
            onChange={(e) => {
              const guid = e.target.value;
              const info = catalog.find((p) => p.guid === guid);
              onChange({ ...rule, parameterGuid: guid, parameterName: info?.name ?? rule.parameterName });
            }}
          >
            <option value="">— choisir —</option>
            {catalog.map((p) => (
              <option key={p.guid} value={p.guid}>
                {p.name} ({p.type})
              </option>
            ))}
            {rule.parameterGuid && !known && (
              <option value={rule.parameterGuid}>{rule.parameterName || rule.parameterGuid} (introuvable)</option>
            )}
          </select>
        </label>
        <label>
          Mode
          <select value={rule.mode} onChange={(e) => onChange({ ...rule, mode: e.target.value as MappingMode })}>
            {MAPPING_MODES.map((m) => (
              <option key={m}>{m}</option>
            ))}
          </select>
        </label>
        {showWeight && (
          <label>
            Poids
            <input
              type="number"
              min={0}
              step={0.1}
              value={rule.weight}
              onChange={(e) => onChange({ ...rule, weight: Math.max(0, Number(e.target.value)) })}
            />
          </label>
        )}
        <button className="danger small" onClick={onRemove}>
          ✕
        </button>
      </div>

      {rule.mode === "Threshold" ? (
        <div className="thresholds">
          <span className="hint">RawValue ≥ seuil → résultat [0..1] (le seuil le plus haut satisfait gagne)</span>
          {rule.thresholds.map((t, ti) => (
            <div key={ti} className="threshold-row">
              ≥
              <input
                type="number"
                step={0.1}
                value={t.atLeast}
                onChange={(e) =>
                  onChange({
                    ...rule,
                    thresholds: rule.thresholds.map((x, i) => (i === ti ? { ...x, atLeast: Number(e.target.value) } : x)),
                  })
                }
              />
              →
              <input
                type="number"
                min={0}
                max={1}
                step={0.05}
                value={t.result}
                onChange={(e) =>
                  onChange({
                    ...rule,
                    thresholds: rule.thresholds.map((x, i) => (i === ti ? { ...x, result: Number(e.target.value) } : x)),
                  })
                }
              />
              <button
                className="danger small"
                onClick={() => onChange({ ...rule, thresholds: rule.thresholds.filter((_, i) => i !== ti) })}
              >
                ✕
              </button>
            </div>
          ))}
          <button className="small" onClick={() => onChange({ ...rule, thresholds: [...rule.thresholds, createThreshold()] })}>
            + Seuil
          </button>
        </div>
      ) : (
        <div className="thresholds">
          <span className="hint">Courbe : RawValue (temps) → résultat [0..1] (clés éditées en numérique pour l'instant)</span>
          {rule.curve.keys.map((k, ki) => (
            <div key={ki} className="threshold-row">
              t
              <input type="number" step={0.1} value={k.time} onChange={(e) => setKey(ki, { ...k, time: Number(e.target.value) })} />
              →
              <input
                type="number"
                min={0}
                max={1}
                step={0.05}
                value={k.value}
                onChange={(e) => setKey(ki, { ...k, value: Number(e.target.value) })}
              />
              <button
                className="danger small"
                onClick={() => onChange({ ...rule, curve: { ...rule.curve, keys: rule.curve.keys.filter((_, i) => i !== ki) } })}
              >
                ✕
              </button>
            </div>
          ))}
          <button
            className="small"
            onClick={() => {
              const last = rule.curve.keys[rule.curve.keys.length - 1];
              onChange({
                ...rule,
                curve: {
                  ...rule.curve,
                  keys: [
                    ...rule.curve.keys,
                    { time: (last?.time ?? 0) + 1, value: 1, inSlope: 0, outSlope: 0, inWeight: 0, outWeight: 0, weightedMode: 0 },
                  ],
                },
              });
            }}
          >
            + Clé
          </button>
        </div>
      )}
    </div>
  );
}
