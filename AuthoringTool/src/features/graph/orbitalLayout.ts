import type { SpaceDefinition, TransitionDefinition } from "../../model/types";

/**
 * Layout de la Space Map.
 *
 * Trois couches :
 * - Le MONDE (pannable/zoomable) : les espaces ayant au moins une transition.
 *   Chaque composante connexe (transitions espace↔espace) est enracinée sur
 *   son premier espace (ordre projet) ; la racine a une position monde libre
 *   (layout.nodePositions), les autres se placent sur l'ORBITE de leur parent
 *   BFS à distance fixe ORBIT_RADIUS — on ne déplace que leur angle
 *   (layout.orbitAngles).
 * - Le SILENCE : fixé à l'écran (bas centre), hors du monde.
 * - L'ÉTAGÈRE : espaces sans aucune transition, sur une ligne fixe en bas à
 *   gauche de l'écran, jusqu'à leur première transition.
 */

export const SILENCE_ID = "__silence__";
export const NODE_RADIUS = 50;
export const SILENCE_RADIUS = 40;
export const SHELF_RADIUS = 30;
export const ORBIT_RADIUS = 250;

export interface WorldNode {
  id: string;
  /** Coordonnées monde. */
  x: number;
  y: number;
  r: number;
  /** Parent d'orbite (null pour une racine de composante). */
  parentId: string | null;
  isRoot: boolean;
}

export interface WorldLayout {
  world: Map<string, WorldNode>;
  /** Espaces sans transition, dans l'ordre du projet. */
  shelf: string[];
}

/** Normalise un angle dans [-π, π]. */
export function normalizeAngle(a: number): number {
  while (a > Math.PI) a -= 2 * Math.PI;
  while (a < -Math.PI) a += 2 * Math.PI;
  return a;
}

export function computeWorldLayout(
  spaces: SpaceDefinition[],
  transitions: TransitionDefinition[],
  storedAngles: Record<string, number>,
  rootPositions: Record<string, { x: number; y: number }>,
): WorldLayout {
  const spaceIds = spaces.map((s) => s.spaceId);
  const idSet = new Set(spaceIds);
  const order = (id: string) => spaceIds.indexOf(id);

  // Un espace est « dans le monde » dès qu'une transition le touche (silence inclus).
  const connected = new Set<string>();
  const adj = new Map<string, Set<string>>();
  const link = (a: string, b: string) => {
    if (!adj.has(a)) adj.set(a, new Set());
    adj.get(a)!.add(b);
  };
  for (const t of transitions) {
    if (t.fromSpaceId && idSet.has(t.fromSpaceId)) connected.add(t.fromSpaceId);
    if (t.toSpaceId && idSet.has(t.toSpaceId)) connected.add(t.toSpaceId);
    if (t.fromSpaceId && t.toSpaceId && idSet.has(t.fromSpaceId) && idSet.has(t.toSpaceId) && t.fromSpaceId !== t.toSpaceId) {
      link(t.fromSpaceId, t.toSpaceId);
      link(t.toSpaceId, t.fromSpaceId);
    }
  }

  const shelf = spaceIds.filter((id) => !connected.has(id));

  const world = new Map<string, WorldNode>();
  const visited = new Set<string>();
  let rootIndex = 0;

  for (const rootId of spaceIds) {
    if (!connected.has(rootId) || visited.has(rootId)) continue;

    // Racine de composante : position monde libre.
    const rootPos = rootPositions[rootId] ?? { x: rootIndex * ORBIT_RADIUS * 2.8, y: -rootIndex * 40 };
    rootIndex++;
    visited.add(rootId);
    world.set(rootId, { id: rootId, x: rootPos.x, y: rootPos.y, r: NODE_RADIUS, parentId: null, isRoot: true });

    // BFS : les membres de la composante orbitent leur parent.
    const queue = [rootId];
    while (queue.length) {
      const cur = queue.shift()!;
      const parent = world.get(cur)!;
      const neighbors = [...(adj.get(cur) ?? [])].filter((n) => !visited.has(n));
      neighbors.sort((a, b) => order(a) - order(b));
      for (const id of neighbors) {
        visited.add(id);
        const slot = order(id);
        // Angle par défaut stable (index global) : indépendant du reste du graphe.
        let fallback: number;
        if (parent.isRoot) {
          fallback = normalizeAngle(-Math.PI / 2 + ((slot % 5) - 2) * 0.5);
        } else {
          const grandparent = world.get(parent.parentId!)!;
          const base = Math.atan2(parent.y - grandparent.y, parent.x - grandparent.x);
          fallback = normalizeAngle(base + ((slot % 3) - 1) * 0.55);
        }
        const angle = storedAngles[id] ?? fallback;
        world.set(id, {
          id,
          x: parent.x + Math.cos(angle) * ORBIT_RADIUS,
          y: parent.y + Math.sin(angle) * ORBIT_RADIUS,
          r: NODE_RADIUS,
          parentId: cur,
          isRoot: false,
        });
        queue.push(id);
      }
    }
  }

  return { world, shelf };
}
