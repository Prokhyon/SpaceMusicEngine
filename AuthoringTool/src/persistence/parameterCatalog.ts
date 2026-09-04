import { readDir, readTextFile } from "@tauri-apps/plugin-fs";
import type { ParameterInfo } from "../model/types";
import { useProjectStore } from "../store/useProjectStore";

/**
 * Catalogue des paramètres de jeu (IntParameter / FloatParameter) :
 * scan des .asset du dossier Assets/ du projet Unity englobant le fichier
 * projet ouvert. Les paramètres restent des assets Unity (le jeu écrit
 * dedans) — l'outil n'en a besoin qu'en lecture, pour les MappingRules.
 */

/** Remonte depuis le fichier projet jusqu'au dossier Assets/ Unity. */
export function findAssetsRoot(projectFilePath: string): string | null {
  const parts = projectFilePath.split("/");
  for (let i = parts.length - 1; i > 0; i--) {
    if (parts[i] === "Assets") return parts.slice(0, i + 1).join("/");
  }
  return null;
}

const PARAM_RE = /m_EditorClassIdentifier:[^\n]*[:.](Int|Float)Parameter\s*$/m;
const NAME_RE = /^ {2}m_Name: (.*)$/m;
const GUID_RE = /^guid: ([0-9a-f]{32})/m;

async function scanDir(dir: string, results: ParameterInfo[], assetsRoot: string): Promise<void> {
  let entries;
  try {
    entries = await readDir(dir);
  } catch {
    return; // dossier illisible : on ignore
  }
  for (const entry of entries) {
    const path = `${dir}/${entry.name}`;
    if (entry.isDirectory) {
      await scanDir(path, results, assetsRoot);
    } else if (entry.name.endsWith(".asset")) {
      try {
        const content = await readTextFile(path);
        const match = content.match(PARAM_RE);
        if (!match) continue;
        const meta = await readTextFile(`${path}.meta`);
        const guid = meta.match(GUID_RE)?.[1];
        if (!guid) continue;
        results.push({
          guid,
          name: content.match(NAME_RE)?.[1] ?? entry.name.replace(/\.asset$/, ""),
          type: match[1] as "Int" | "Float",
          assetPath: path.slice(assetsRoot.length + 1),
        });
      } catch {
        // .asset illisible ou .meta manquant : on ignore
      }
    }
  }
}

/** Scanne Assets/ et met le catalogue à jour dans le store. Silencieux hors Tauri. */
export async function refreshParameterCatalog(projectFilePath: string | null): Promise<void> {
  const assetsRoot = projectFilePath ? findAssetsRoot(projectFilePath) : null;
  if (!assetsRoot) {
    useProjectStore.getState().setParameterCatalog([]);
    return;
  }
  const results: ParameterInfo[] = [];
  await scanDir(assetsRoot, results, assetsRoot);
  results.sort((a, b) => a.name.localeCompare(b.name));
  useProjectStore.getState().setParameterCatalog(results);
}
