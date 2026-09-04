import { open } from "@tauri-apps/plugin-dialog";
import { readTextFile } from "@tauri-apps/plugin-fs";
import { findAssetsRoot } from "./parameterCatalog";
import { useProjectStore } from "../store/useProjectStore";

export interface ClipChoice {
  clipPath: string;
  clipGuid?: string;
}

/**
 * Ouvre un dialogue natif pour choisir un clip audio. Si le fichier est dans
 * le dossier Assets/ du projet Unity (déduit du fichier projet ouvert), le
 * chemin devient relatif à Assets/ et le GUID est lu depuis son .meta —
 * c'est ce GUID que l'importeur Unity utilise pour résoudre l'AudioClip.
 */
export async function pickClip(): Promise<ClipChoice | null> {
  const { filePath } = useProjectStore.getState();
  const assetsRoot = filePath ? findAssetsRoot(filePath) : null;

  const selected = await open({
    multiple: false,
    defaultPath: assetsRoot ?? undefined,
    filters: [{ name: "Audio", extensions: ["wav", "aif", "aiff", "mp3", "ogg", "flac"] }],
  });
  if (typeof selected !== "string") return null;

  if (assetsRoot && selected.startsWith(assetsRoot + "/")) {
    let clipGuid: string | undefined;
    try {
      clipGuid = (await readTextFile(`${selected}.meta`)).match(/^guid: ([0-9a-f]{32})/m)?.[1];
    } catch {
      // .meta manquant : chemin seul
    }
    return { clipPath: selected.slice(assetsRoot.length + 1), clipGuid };
  }
  return { clipPath: selected };
}
