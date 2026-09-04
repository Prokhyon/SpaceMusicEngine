#!/usr/bin/env bash
# Construit le zip des ressources Unity de la démo (stems et scènes).
#
# Usage (depuis le projet complet) : scripts/make-demo-zip.sh [sortie.zip]
#
# Contenu, avec les chemins relatifs à la racine du projet :
#   - les stems listés dans demo-audio-files.txt et leurs .meta ;
#   - le dossier Assets/3DGamekit/Scenes (scènes modifiées et éclairage précalculé).
# Les .meta sont indispensables : ils portent les GUID référencés par les projets
# musicaux et les scènes.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:-SpaceMusicEngine-DemoAssets.zip}"
case "$out" in /*) ;; *) out="$PWD/$out" ;; esac

cd "$root"

list="$(mktemp)"
trap 'rm -f "$list"' EXIT

missing=0
while IFS= read -r f; do
  [[ -z "$f" || "$f" == \#* ]] && continue
  for p in "$f" "$f.meta"; do
    if [[ -f "$p" ]]; then
      echo "$p" >> "$list"
    else
      echo "Manquant : $p" >&2
      missing=1
    fi
  done
  # .meta des dossiers parents (sous Assets/), s'ils existent
  d="$(dirname "$f")"
  while [[ "$d" == Assets/* ]]; do
    [[ -f "$d.meta" ]] && echo "$d.meta" >> "$list"
    d="$(dirname "$d")"
  done
done < demo-audio-files.txt

scenes="Assets/3DGamekit/Scenes"
if [[ -d "$scenes" ]]; then
  find "$scenes" -type f ! -name .DS_Store >> "$list"
else
  echo "Manquant : $scenes" >&2
  missing=1
fi

if [[ $missing -ne 0 ]]; then
  echo "Des fichiers manquent : zip non créé." >&2
  exit 1
fi

rm -f "$out"
sort -u "$list" | zip -q -X "$out" -@
echo "Créé : $out ($(du -h "$out" | cut -f1), $(sort -u "$list" | wc -l | tr -d ' ') fichiers)"
