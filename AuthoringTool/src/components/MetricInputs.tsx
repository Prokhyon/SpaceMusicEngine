import type { MetricInterval, MetricPosition } from "../model/types";

/** Position métrique 1-based « mesure.temps ». */
export function PositionInput({
  value,
  onChange,
}: {
  value: MetricPosition;
  onChange: (next: MetricPosition) => void;
}) {
  return (
    <span className="metric-input">
      <input
        type="number"
        min={1}
        value={value.bar}
        onChange={(e) => onChange({ ...value, bar: Math.max(1, Number(e.target.value)) })}
        title="Mesure"
      />
      .
      <input
        type="number"
        min={1}
        value={value.beat}
        onChange={(e) => onChange({ ...value, beat: Math.max(1, Number(e.target.value)) })}
        title="Temps"
      />
    </span>
  );
}

/** Intervalle métrique « mesures + temps ». */
export function IntervalInput({
  value,
  onChange,
}: {
  value: MetricInterval;
  onChange: (next: MetricInterval) => void;
}) {
  return (
    <span className="metric-input">
      <input
        type="number"
        min={0}
        value={value.bars}
        onChange={(e) => onChange({ ...value, bars: Math.max(0, Number(e.target.value)) })}
        title="Mesures"
      />
      m
      <input
        type="number"
        min={0}
        value={value.beats}
        onChange={(e) => onChange({ ...value, beats: Math.max(0, Number(e.target.value)) })}
        title="Temps"
      />
      t
    </span>
  );
}
