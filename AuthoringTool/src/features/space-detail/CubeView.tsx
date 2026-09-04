import { useMemo, useState } from "react";
import { Canvas } from "@react-three/fiber";
import { Line, OrbitControls } from "@react-three/drei";
import * as THREE from "three";
import type { Coordinate, SpaceDefinition } from "../../model/types";
import { layerActiveAt } from "../../model/types";

const SPACING = 1.4;
const AXIS_COLORS = ["#b5793f", "#5f7a5c", "#5c7fa0"];

interface CubeViewProps {
  space: SpaceDefinition;
  /** Indices (dans space.axes) des axes affichés en X/Y/Z — 1 à 3 entrées. */
  displayedAxes: number[];
  /** Valeur figée pour chaque axe non affiché, par index d'axe. */
  fixedValues: Record<number, number>;
  selectedCoordinate: Coordinate | null;
  onSelect: (coordinate: Coordinate) => void;
}

/** Reconstruit la coordonnée complète à partir des valeurs affichées + figées. */
function buildCoordinate(
  space: SpaceDefinition,
  displayedAxes: number[],
  fixedValues: Record<number, number>,
  displayedValues: number[],
): Coordinate {
  return space.axes.map((_, axisIndex) => {
    const d = displayedAxes.indexOf(axisIndex);
    if (d >= 0) return displayedValues[d];
    return fixedValues[axisIndex] ?? 0;
  });
}

interface PointInfo {
  key: string;
  position: [number, number, number];
  coordinate: Coordinate;
  activeLayerCount: number;
}

export function CubeView({ space, displayedAxes, fixedValues, selectedCoordinate, onSelect }: CubeViewProps) {
  const [hovered, setHovered] = useState<string | null>(null);

  // Nombre de paliers par dimension affichée (levelCount inclus → +1 valeurs).
  const dims = displayedAxes.map((i) => space.axes[i].levelCount);

  const points = useMemo<PointInfo[]>(() => {
    const result: PointInfo[] = [];
    const counts = dims.map((c) => c + 1);
    const total = counts.reduce((a, b) => a * b, 1);
    for (let flat = 0; flat < total; flat++) {
      let rest = flat;
      const displayedValues: number[] = [];
      for (const c of counts) {
        displayedValues.push(rest % c);
        rest = Math.floor(rest / c);
      }
      const coordinate = buildCoordinate(space, displayedAxes, fixedValues, displayedValues);
      const position: [number, number, number] = [0, 1, 2].map((d) =>
        d < dims.length ? (displayedValues[d] - dims[d] / 2) * SPACING : 0,
      ) as [number, number, number];
      result.push({
        key: coordinate.join(","),
        position,
        coordinate,
        activeLayerCount: space.layers.filter((l) => layerActiveAt(l, space, coordinate)).length,
      });
    }
    return result;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [space, displayedAxes.join(","), JSON.stringify(fixedValues)]);

  const wireframe = useMemo(() => {
    const positions: number[] = [];
    const counts = dims.map((c) => c + 1);
    const coord = (v: number[], d: number) => (v[d] !== undefined ? (v[d] - dims[d] / 2) * SPACING : 0);
    const push = (a: number[], b: number[]) => {
      positions.push(coord(a, 0), coord(a, 1), coord(a, 2), coord(b, 0), coord(b, 1), coord(b, 2));
    };
    // Segments entre points adjacents le long de chaque dimension affichée.
    const walk = (dims2: number[], current: number[], i: number, cb: (v: number[]) => void) => {
      if (i === dims2.length) return cb([...current]);
      for (let v = 0; v < dims2[i]; v++) {
        current[i] = v;
        walk(dims2, current, i + 1, cb);
      }
    };
    for (let d = 0; d < counts.length; d++) {
      walk(counts, new Array(counts.length).fill(0), 0, (v) => {
        if (v[d] + 1 < counts[d]) {
          const b = [...v];
          b[d] = v[d] + 1;
          push(v, b);
        }
      });
    }
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute("position", new THREE.Float32BufferAttribute(positions, 3));
    return geometry;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dims.join(",")]);

  const selectedKey = selectedCoordinate?.join(",") ?? null;
  const maxDim = Math.max(...dims, 1);
  const cameraDistance = maxDim * SPACING * 1.9 + 3;

  return (
    <Canvas
      className="cube-canvas"
      camera={{ position: [cameraDistance, cameraDistance * 0.75, cameraDistance], fov: 40 }}
    >
      <color attach="background" args={["#050505"]} />
      <ambientLight intensity={1.0} />
      <directionalLight position={[5, 8, 5]} intensity={0.7} />

      <lineSegments geometry={wireframe}>
        <lineBasicMaterial color="#24201a" />
      </lineSegments>

      {/* Repères de direction des axes affichés, depuis le coin origine (0,0,0) du treillis */}
      {displayedAxes.map((axisIndex, d) => {
        const corner = [0, 1, 2].map((k) => (k < dims.length ? (-dims[k] / 2) * SPACING : 0));
        const end = [...corner];
        end[d] += dims[d] * SPACING + 0.8;
        return (
          <Line
            key={axisIndex}
            points={[corner as [number, number, number], end as [number, number, number]]}
            color={AXIS_COLORS[d]}
            lineWidth={2}
          />
        );
      })}

      {points.map((p) => {
        const isSelected = p.key === selectedKey;
        const isHovered = p.key === hovered;
        const hasLayers = p.activeLayerCount > 0;
        return (
          <mesh
            key={p.key}
            position={p.position}
            onClick={(e) => {
              e.stopPropagation();
              onSelect(p.coordinate);
            }}
            onPointerOver={(e) => {
              e.stopPropagation();
              setHovered(p.key);
            }}
            onPointerOut={() => setHovered((h) => (h === p.key ? null : h))}
          >
            <sphereGeometry args={[isSelected ? 0.24 : 0.16, 20, 20]} />
            <meshStandardMaterial
              color={isSelected ? "#e0a15c" : isHovered ? "#d89a55" : hasLayers ? "#b5793f" : "#2c2822"}
              emissive={isSelected ? "#4a2c10" : "#000000"}
            />
          </mesh>
        );
      })}

      <OrbitControls makeDefault enableDamping dampingFactor={0.12} autoRotate autoRotateSpeed={0.6} />
    </Canvas>
  );
}
