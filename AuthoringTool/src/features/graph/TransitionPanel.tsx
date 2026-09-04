import type { TransitionDefinition, TransitionVariant } from "../../model/types";
import {
  ARRIVAL_MODES,
  SOURCE_EXITS,
  TAIL_TEMPOS,
  TRIGGER_MODES,
  type ArrivalMode,
  type SourceExit,
  type TailTempo,
  type TriggerMode,
} from "../../model/types";
import { createVariant, positionOne } from "../../model/factories";
import { useProjectStore } from "../../store/useProjectStore";
import { IntervalInput, PositionInput } from "../../components/MetricInputs";

export function TransitionPanel({
  transition,
  onClose,
}: {
  transition: TransitionDefinition;
  onClose: () => void;
}) {
  const spaces = useProjectStore((s) => s.project.spaces);
  const updateTransition = useProjectStore((s) => s.updateTransition);
  const removeTransition = useProjectStore((s) => s.removeTransition);

  const patch = (p: Partial<Omit<TransitionDefinition, "transitionId">>) =>
    updateTransition(transition.transitionId, p);

  const spaceName = (id: string) => (id === "" ? "∅ Silence" : (spaces.find((s) => s.spaceId === id)?.name ?? id));

  const setVariant = (index: number, v: TransitionVariant) => {
    const variants = transition.variants.map((old, i) => (i === index ? v : old));
    patch({ variants });
  };

  return (
    <aside className="transition-panel">
      <div className="panel-header">
        <h3>Transition</h3>
        <button onClick={onClose}>Fermer</button>
      </div>

      <div className="transition-routing">
        {spaceName(transition.fromSpaceId)} → {spaceName(transition.toSpaceId)}
      </div>

      <label className="field">
        Identifiant
        <input value={transition.transitionId} readOnly />
      </label>

      <section>
        <h4>Piste</h4>
        <label className="check">
          <input type="checkbox" checked={transition.hasTrack} onChange={(e) => patch({ hasTrack: e.target.checked })} />
          Jouer une piste de transition (sinon directe)
        </label>
        {transition.hasTrack && (
          <>
            <label className="field">
              Clip (chemin)
              <input value={transition.clipPath} onChange={(e) => patch({ clipPath: e.target.value })} />
            </label>
            <div className="field-row">
              <label>
                BPM
                <input
                  type="number"
                  min={1}
                  value={transition.bpm}
                  onChange={(e) => patch({ bpm: Number(e.target.value) })}
                />
              </label>
              <label>
                Temps/mesure
                <input
                  type="number"
                  min={1}
                  value={transition.beatsPerBar}
                  onChange={(e) => patch({ beatsPerBar: Number(e.target.value) })}
                />
              </label>
              <label>
                Longueur (mesures)
                <input
                  type="number"
                  min={1}
                  value={transition.lengthBars}
                  onChange={(e) => patch({ lengthBars: Math.max(1, Number(e.target.value)) })}
                />
              </label>
            </div>
            <label className="check">
              <input
                type="checkbox"
                checked={transition.useCustomTransitionLength}
                onChange={(e) => patch({ useCustomTransitionLength: e.target.checked })}
              />
              Durée effective personnalisée
            </label>
            {transition.useCustomTransitionLength && (
              <label className="field-inline">
                Durée
                <IntervalInput
                  value={transition.transitionLength}
                  onChange={(transitionLength) => patch({ transitionLength })}
                />
              </label>
            )}
          </>
        )}
      </section>

      <section>
        <h4>Sortie source</h4>
        <label className="field">
          Sortie
          <select value={transition.sourceExit} onChange={(e) => patch({ sourceExit: e.target.value as SourceExit })}>
            {SOURCE_EXITS.map((m) => (
              <option key={m}>{m}</option>
            ))}
          </select>
        </label>
        <label className="check">
          <input
            type="checkbox"
            checked={transition.keepSourcePlaying}
            onChange={(e) => patch({ keepSourcePlaying: e.target.checked })}
          />
          Garder la source pendant la transition
        </label>
        {transition.keepSourcePlaying && (
          <>
            <label className="check">
              <input
                type="checkbox"
                checked={transition.keepSourceUntilEndOfTrack}
                onChange={(e) => patch({ keepSourceUntilEndOfTrack: e.target.checked })}
              />
              Jusqu'à la fin de la piste
            </label>
            {!transition.keepSourceUntilEndOfTrack && (
              <label className="field-inline">
                Jusqu'à (grille transition)
                <PositionInput
                  value={transition.keepSourceUntil}
                  onChange={(keepSourceUntil) => patch({ keepSourceUntil })}
                />
              </label>
            )}
          </>
        )}
      </section>

      <section>
        <h4>Déclenchement</h4>
        <label className="field">
          Mode
          <select value={transition.triggerMode} onChange={(e) => patch({ triggerMode: e.target.value as TriggerMode })}>
            {TRIGGER_MODES.map((m) => (
              <option key={m}>{m}</option>
            ))}
          </select>
        </label>
        {transition.triggerMode === "Frequency" ? (
          <>
            <label className="field-inline">
              Fréquence
              <IntervalInput value={transition.frequency} onChange={(frequency) => patch({ frequency })} />
            </label>
            {transition.hasTrack && (
              <label className="field-inline">
                Départ piste
                <PositionInput
                  value={transition.defaultTrackStart}
                  onChange={(defaultTrackStart) => patch({ defaultTrackStart })}
                />
              </label>
            )}
          </>
        ) : (
          <div className="variants">
            {transition.variants.map((variant, vi) => (
              <div key={vi} className="variant">
                <div className="variant-header">
                  <span>Variante {vi + 1}</span>
                  <button
                    className="danger small"
                    onClick={() => patch({ variants: transition.variants.filter((_, i) => i !== vi) })}
                  >
                    ✕
                  </button>
                </div>
                <div className="field">
                  Départs (grille source)
                  {variant.departures.map((dep, di) => (
                    <span key={di} className="departure-row">
                      <PositionInput
                        value={dep}
                        onChange={(next) =>
                          setVariant(vi, {
                            ...variant,
                            departures: variant.departures.map((d, i) => (i === di ? next : d)),
                          })
                        }
                      />
                      <button
                        className="danger small"
                        onClick={() =>
                          setVariant(vi, { ...variant, departures: variant.departures.filter((_, i) => i !== di) })
                        }
                      >
                        ✕
                      </button>
                    </span>
                  ))}
                  <button
                    className="small"
                    onClick={() => setVariant(vi, { ...variant, departures: [...variant.departures, positionOne()] })}
                  >
                    + Départ
                  </button>
                </div>
                {transition.hasTrack && (
                  <label className="field-inline">
                    Départ piste
                    <PositionInput value={variant.trackStart} onChange={(trackStart) => setVariant(vi, { ...variant, trackStart })} />
                  </label>
                )}
                {transition.arrivalMode === "PerVariant" && (
                  <label className="field-inline">
                    Arrivée (grille cible)
                    <PositionInput value={variant.arrival} onChange={(arrival) => setVariant(vi, { ...variant, arrival })} />
                  </label>
                )}
              </div>
            ))}
            <button onClick={() => patch({ variants: [...transition.variants, createVariant()] })}>+ Variante</button>
          </div>
        )}
      </section>

      <section>
        <h4>Arrivée</h4>
        <label className="field">
          Mode
          <select value={transition.arrivalMode} onChange={(e) => patch({ arrivalMode: e.target.value as ArrivalMode })}>
            {ARRIVAL_MODES.map((m) => (
              <option key={m}>{m}</option>
            ))}
          </select>
        </label>
        {transition.arrivalMode === "KeepSourcePosition" && (
          <label className="field">
            Décalage de reprise (mesures)
            <input
              type="number"
              value={transition.resumeOffsetBars}
              onChange={(e) => patch({ resumeOffsetBars: Number(e.target.value) })}
            />
          </label>
        )}
        {transition.arrivalMode === "Fixed" && (
          <label className="field-inline">
            Arrivée fixe
            <PositionInput value={transition.fixedArrival} onChange={(fixedArrival) => patch({ fixedArrival })} />
          </label>
        )}
      </section>

      {transition.hasTrack && (
        <section>
          <h4>Tail</h4>
          <label className="check">
            <input type="checkbox" checked={transition.playTail} onChange={(e) => patch({ playTail: e.target.checked })} />
            Laisser sonner la piste après l'entrée cible
          </label>
          {transition.playTail && (
            <>
              <label className="field-inline">
                Durée
                <IntervalInput value={transition.tailLength} onChange={(tailLength) => patch({ tailLength })} />
              </label>
              <label className="field">
                Tempo de référence
                <select value={transition.tailTempo} onChange={(e) => patch({ tailTempo: e.target.value as TailTempo })}>
                  {TAIL_TEMPOS.map((m) => (
                    <option key={m}>{m}</option>
                  ))}
                </select>
              </label>
            </>
          )}
        </section>
      )}

      <button
        className="danger"
        onClick={() => {
          removeTransition(transition.transitionId);
          onClose();
        }}
      >
        Supprimer la transition
      </button>
    </aside>
  );
}
