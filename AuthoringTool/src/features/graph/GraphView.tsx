import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useProjectStore } from "../../store/useProjectStore";
import { TransitionPanel } from "./TransitionPanel";
import {
  NODE_RADIUS,
  SHELF_RADIUS,
  SILENCE_ID,
  SILENCE_RADIUS,
  computeWorldLayout,
} from "./orbitalLayout";

/** Nœud résolu à l'écran (monde transformé, silence ou étagère fixes). */
interface ScreenNode {
  id: string;
  sx: number;
  sy: number;
  sr: number;
  kind: "world" | "silence" | "shelf";
  parentId: string | null;
  isRoot: boolean;
}

interface DragState {
  mode: "move" | "connect" | "pan";
  id: string | null;
  startX: number;
  startY: number;
  moved: boolean;
  panStart?: { tx: number; ty: number };
}

const EDGE_PAIR_GAP = 40; // écart (world px) entre les deux flèches d'une paire bidirectionnelle
const MIN_ZOOM = 0.3;
const MAX_ZOOM = 2.5;

export function GraphView() {
  const spaces = useProjectStore((s) => s.project.spaces);
  const transitions = useProjectStore((s) => s.project.transitions);
  const orbitAngles = useProjectStore((s) => s.project.layout.orbitAngles);
  const rootPositions = useProjectStore((s) => s.project.layout.nodePositions);
  const addSpace = useProjectStore((s) => s.addSpace);
  const setOrbitAngle = useProjectStore((s) => s.setOrbitAngle);
  const setRootPosition = useProjectStore((s) => s.setRootPosition);
  const addTransition = useProjectStore((s) => s.addTransition);
  const openSpaceDetail = useProjectStore((s) => s.openSpaceDetail);

  const containerRef = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ w: 1200, h: 700 });
  const [view, setView] = useState({ tx: 0, ty: 0, z: 1 });
  const viewInitialized = useRef(false);
  const [hoverId, setHoverId] = useState<string | null>(null);
  const [selectedTransitionId, setSelectedTransitionId] = useState<string | null>(null);
  const [connectLine, setConnectLine] = useState<{ x1: number; y1: number; x2: number; y2: number } | null>(null);
  const dragRef = useRef<DragState | null>(null);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const measure = () => {
      if (el.clientWidth && el.clientHeight) setSize({ w: el.clientWidth, h: el.clientHeight });
    };
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    measure();
    return () => ro.disconnect();
  }, []);

  const layout = useMemo(
    () => computeWorldLayout(spaces, transitions, orbitAngles ?? {}, rootPositions),
    [spaces, transitions, orbitAngles, rootPositions],
  );

  // Cadre le monde dans la vue (au premier rendu et via « Recentrer »).
  const fitView = useCallback(() => {
    const nodes = [...layout.world.values()];
    if (nodes.length === 0) {
      setView({ tx: size.w / 2, ty: size.h / 2, z: 1 });
      return;
    }
    const xs = nodes.map((n) => n.x);
    const ys = nodes.map((n) => n.y);
    const minX = Math.min(...xs) - 120;
    const maxX = Math.max(...xs) + 120;
    const minY = Math.min(...ys) - 120;
    const maxY = Math.max(...ys) + 150;
    const availW = size.w - 160;
    const availH = size.h - 300; // marge pour le silence et l'étagère en bas
    const z = Math.min(1, availW / Math.max(1, maxX - minX), availH / Math.max(1, maxY - minY));
    setView({
      tx: size.w / 2 - ((minX + maxX) / 2) * z,
      ty: 80 + availH / 2 - ((minY + maxY) / 2) * z,
      z: Math.max(MIN_ZOOM, z),
    });
  }, [layout.world, size.w, size.h]);

  useEffect(() => {
    if (!viewInitialized.current && size.w > 0) {
      viewInitialized.current = true;
      fitView();
    }
  }, [size.w, fitView]);

  const silencePos = { x: size.w / 2, y: size.h - 100 };

  const screenNodes = useMemo(() => {
    const map = new Map<string, ScreenNode>();
    for (const n of layout.world.values()) {
      map.set(n.id, {
        id: n.id,
        sx: n.x * view.z + view.tx,
        sy: n.y * view.z + view.ty,
        sr: NODE_RADIUS * view.z,
        kind: "world",
        parentId: n.parentId,
        isRoot: n.isRoot,
      });
    }
    layout.shelf.forEach((id, i) => {
      map.set(id, {
        id,
        sx: 90 + i * 96,
        sy: size.h - 78,
        sr: SHELF_RADIUS,
        kind: "shelf",
        parentId: null,
        isRoot: false,
      });
    });
    map.set(SILENCE_ID, {
      id: SILENCE_ID,
      sx: silencePos.x,
      sy: silencePos.y,
      sr: SILENCE_RADIUS,
      kind: "silence",
      parentId: null,
      isRoot: false,
    });
    return map;
  }, [layout, view, size.h, silencePos.x, silencePos.y]);

  const isVisible = useCallback(
    (n: ScreenNode) => n.sx > -n.sr && n.sx < size.w + n.sr && n.sy > -n.sr && n.sy < size.h + n.sr,
    [size.w, size.h],
  );

  // Edges : bord-à-bord au plus proche ; deux sens → deux flèches parallèles
  // opposées, symétriques autour de la ligne des centres.
  const edges = useMemo(() => {
    const directed = new Set(transitions.map((t) => `${t.fromSpaceId}→${t.toSpaceId}`));
    return transitions.flatMap((t) => {
      const fromId = t.fromSpaceId === "" ? SILENCE_ID : t.fromSpaceId;
      const toId = t.toSpaceId === "" ? SILENCE_ID : t.toSpaceId;
      const a = screenNodes.get(fromId);
      const b = screenNodes.get(toId);
      if (!a || !b || a === b) return [];
      if (!isVisible(a) || !isVisible(b)) return [];

      const dx = b.sx - a.sx;
      const dy = b.sy - a.sy;
      const dist = Math.hypot(dx, dy) || 1;
      const ux = dx / dist;
      const uy = dy / dist;
      // Décalage « à gauche du sens de parcours » (repère écran, y vers le bas) :
      // pour a→b la ligne se décale à gauche, pour b→a à gauche aussi de SON sens,
      // donc de l'autre côté → motif --->/<---, centré sur la ligne des centres.
      const leftPx = uy;
      const leftPy = -ux;
      const both = directed.has(`${t.toSpaceId}→${t.fromSpaceId}`);
      const offset = both ? (EDGE_PAIR_GAP / 2) * view.z : 0;

      const sx = a.sx + ux * a.sr + leftPx * offset;
      const sy = a.sy + uy * a.sr + leftPy * offset;
      const ex = b.sx - ux * b.sr + leftPx * offset;
      const ey = b.sy - uy * b.sr + leftPy * offset;
      // Label au milieu de SA flèche, poussé un peu plus à gauche pour ne pas se croiser.
      const lx = (sx + ex) / 2 + leftPx * 12;
      const ly = (sy + ey) / 2 + leftPy * 12;

      return [
        {
          transitionId: t.transitionId,
          d: `M${sx},${sy} L${ex},${ey}`,
          lx,
          ly,
          label: `${t.hasTrack ? "piste" : "directe"} · ${
            t.triggerMode === "Frequency" ? "fréquence" : `${t.variants.length} var.`
          }`,
          selected: t.transitionId === selectedTransitionId,
        },
      ];
    });
  }, [transitions, screenNodes, selectedTransitionId, isVisible, view.z]);

  const toLocal = useCallback((e: PointerEvent | React.PointerEvent | WheelEvent) => {
    const rect = containerRef.current!.getBoundingClientRect();
    return { x: e.clientX - rect.left, y: e.clientY - rect.top };
  }, []);

  const screenToWorld = useCallback(
    (p: { x: number; y: number }) => ({ x: (p.x - view.tx) / view.z, y: (p.y - view.ty) / view.z }),
    [view],
  );

  const onNodePointerDown = useCallback(
    (e: React.PointerEvent, id: string) => {
      e.preventDefault();
      e.stopPropagation();
      const node = screenNodes.get(id);
      if (!node) return;
      const p = toLocal(e);
      const dist = Math.hypot(p.x - node.sx, p.y - node.sy);
      // Bordure extérieure → création de transition ; intérieur → déplacement.
      const onBorder = dist >= node.sr - Math.min(14, node.sr * 0.4);
      const movable = node.kind === "world";
      if (!onBorder && !movable && node.kind === "silence") return; // silence fixe
      dragRef.current = {
        mode: onBorder ? "connect" : movable ? "move" : "pan",
        id,
        startX: p.x,
        startY: p.y,
        moved: false,
      };
      if (!onBorder && !movable) dragRef.current.mode = "connect"; // étagère : intérieur = clic seulement
      if (dragRef.current.mode === "connect") setConnectLine({ x1: node.sx, y1: node.sy, x2: p.x, y2: p.y });
    },
    [screenNodes, toLocal],
  );

  const onBackgroundPointerDown = useCallback(
    (e: React.PointerEvent) => {
      const p = toLocal(e);
      dragRef.current = { mode: "pan", id: null, startX: p.x, startY: p.y, moved: false, panStart: { tx: view.tx, ty: view.ty } };
    },
    [toLocal, view.tx, view.ty],
  );

  useEffect(() => {
    const onMove = (e: PointerEvent) => {
      const drag = dragRef.current;
      if (!drag || !containerRef.current) return;
      const p = toLocal(e);
      if (Math.hypot(p.x - drag.startX, p.y - drag.startY) > 4) drag.moved = true;
      if (!drag.moved) return;

      if (drag.mode === "pan" && drag.panStart) {
        setView((v) => ({ ...v, tx: drag.panStart!.tx + (p.x - drag.startX), ty: drag.panStart!.ty + (p.y - drag.startY) }));
      } else if (drag.mode === "connect") {
        setConnectLine((line) => (line ? { ...line, x2: p.x, y2: p.y } : line));
      } else if (drag.mode === "move" && drag.id) {
        const node = screenNodes.get(drag.id);
        if (!node) return;
        const w = screenToWorld(p);
        if (node.isRoot) {
          setRootPosition(drag.id, { x: w.x, y: w.y });
        } else if (node.parentId) {
          const parent = layout.world.get(node.parentId);
          if (parent) setOrbitAngle(drag.id, Math.atan2(w.y - parent.y, w.x - parent.x));
        }
      }
    };
    const onUp = (e: PointerEvent) => {
      const drag = dragRef.current;
      dragRef.current = null;
      setConnectLine(null);
      if (!drag || !containerRef.current) return;
      const p = toLocal(e);
      if (!drag.moved) {
        if (drag.id && drag.id !== SILENCE_ID) openSpaceDetail(drag.id);
        else if (!drag.id) setSelectedTransitionId(null); // clic dans le vide
        return;
      }
      if (drag.mode === "connect" && drag.id) {
        for (const n of screenNodes.values()) {
          if (n.id === drag.id) continue;
          if (Math.hypot(p.x - n.sx, p.y - n.sy) <= n.sr + 6) {
            const fromShelf = screenNodes.get(drag.id)?.kind === "shelf";
            // Un espace de l'étagère relié uniquement au silence devient racine :
            // il entre dans le monde à l'endroit visé par le geste.
            if (fromShelf && n.id === SILENCE_ID) {
              const w = screenToWorld({ x: drag.startX, y: drag.startY - 160 });
              setRootPosition(drag.id, { x: w.x, y: w.y });
            }
            if (n.kind === "shelf" && drag.id === SILENCE_ID) {
              const w = screenToWorld({ x: n.sx, y: n.sy - 160 });
              setRootPosition(n.id, { x: w.x, y: w.y });
            }
            addTransition(drag.id === SILENCE_ID ? "" : drag.id, n.id === SILENCE_ID ? "" : n.id);
            break;
          }
        }
      }
    };
    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", onUp);
    return () => {
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", onUp);
    };
  }, [screenNodes, layout.world, toLocal, screenToWorld, setOrbitAngle, setRootPosition, openSpaceDetail, addTransition]);

  const onWheel = useCallback(
    (e: React.WheelEvent) => {
      const p = toLocal(e.nativeEvent);
      setView((v) => {
        const z = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, v.z * Math.exp(-e.deltaY * 0.0012)));
        // Zoom centré sur le curseur.
        const k = z / v.z;
        return { z, tx: p.x - (p.x - v.tx) * k, ty: p.y - (p.y - v.ty) * k };
      });
    },
    [toLocal],
  );

  const selectedTransition = transitions.find((t) => t.transitionId === selectedTransitionId) ?? null;
  const glowR = 260;

  return (
    <div className="graph-view" ref={containerRef} onPointerDown={onBackgroundPointerDown} onWheel={onWheel}>
      {/* Gradient d'ancrage du silence, derrière tout */}
      <div
        className="silence-gradient"
        style={{ left: silencePos.x - glowR, top: silencePos.y - glowR, width: glowR * 2, height: glowR * 2 }}
      />

      <svg className="orbital-edges" width={size.w} height={size.h}>
        <defs>
          <marker id="orbital-arrow" markerWidth="9" markerHeight="9" refX="7.5" refY="3.5" orient="auto">
            <path d="M0,0 L9,3.5 L0,7 Z" fill="#5c4a34" />
          </marker>
          <marker id="orbital-arrow-selected" markerWidth="9" markerHeight="9" refX="7.5" refY="3.5" orient="auto">
            <path d="M0,0 L9,3.5 L0,7 Z" fill="#b5793f" />
          </marker>
        </defs>
        {edges.map((e) => (
          <g key={e.transitionId}>
            <path
              d={e.d}
              fill="none"
              stroke={e.selected ? "#b5793f" : "#3a2f24"}
              strokeWidth={1.4}
              markerEnd={e.selected ? "url(#orbital-arrow-selected)" : "url(#orbital-arrow)"}
            />
            <path
              d={e.d}
              fill="none"
              stroke="transparent"
              strokeWidth={12}
              style={{ pointerEvents: "stroke", cursor: "pointer" }}
              onPointerDown={(ev) => ev.stopPropagation()}
              onClick={() => setSelectedTransitionId(e.transitionId)}
            />
          </g>
        ))}
        {connectLine && (
          <line
            x1={connectLine.x1}
            y1={connectLine.y1}
            x2={connectLine.x2}
            y2={connectLine.y2}
            stroke="#b5793f"
            strokeWidth={1.4}
            strokeDasharray="5 4"
            markerEnd="url(#orbital-arrow-selected)"
          />
        )}
      </svg>

      {edges.map((e) => (
        <div
          key={e.transitionId}
          className={`transition-edge-label${e.selected ? " selected" : ""}`}
          style={{ left: e.lx, top: e.ly, transform: "translate(-50%,-50%)" }}
          onPointerDown={(ev) => ev.stopPropagation()}
          onClick={() => setSelectedTransitionId(e.transitionId)}
        >
          {e.label}
        </div>
      ))}

      {[...screenNodes.values()].map((n) => {
        const space = spaces.find((s) => s.spaceId === n.id);
        const isSilence = n.kind === "silence";
        return (
          <div
            key={n.id}
            className={`orbital-node${isSilence ? " silence" : ""}${n.kind === "shelf" ? " shelf" : ""}${
              hoverId === n.id ? " hover" : ""
            }`}
            style={{ left: n.sx - n.sr, top: n.sy - n.sr, width: n.sr * 2, height: n.sr * 2 }}
            onPointerDown={(e) => onNodePointerDown(e, n.id)}
            onPointerEnter={() => setHoverId(n.id)}
            onPointerLeave={() => setHoverId((h) => (h === n.id ? null : h))}
          >
            <div className="circle">{isSilence ? "∅" : null}</div>
            <div className="node-label" style={{ top: n.sr * 2 + 8 }}>
              <div className="name">{isSilence ? "Silence" : (space?.name ?? n.id)}</div>
              {!isSilence && space && n.kind === "world" && (
                <div className="meta">
                  {space.axes.length} axe{space.axes.length > 1 ? "s" : ""} · {space.layers.length} couche
                  {space.layers.length > 1 ? "s" : ""}
                </div>
              )}
            </div>
          </div>
        );
      })}

      {layout.shelf.length > 0 && <div className="shelf-caption">Espaces non reliés</div>}

      <div className="graph-overlay-title">
        <div className="title">Space Map</div>
      </div>
      <div className="graph-overlay-hint">
        Molette pour zoomer · glisser le fond pour se déplacer · clic sur un espace pour l'ouvrir · glisser un espace
        pour le déplacer (racine libre, les autres sur leur orbite) · glisser depuis le bord d'un cercle pour créer
        une transition · clic sur une ligne pour éditer la transition
      </div>
      <div className="graph-actions">
        <button onClick={() => addSpace()}>+ Ajouter un espace</button>
        <button onClick={fitView}>Recentrer</button>
      </div>

      {selectedTransition && (
        <TransitionPanel transition={selectedTransition} onClose={() => setSelectedTransitionId(null)} />
      )}
    </div>
  );
}
