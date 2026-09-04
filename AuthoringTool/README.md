# Loris Audio Authoring

Outil d'authoring externe pour le package Unity `com.loris.audioengine` (musique adaptative).
Application desktop Tauri 2 + React + TypeScript. Cible actuelle : macOS (l'architecture reste
portable — tout le métier/UI est dans `src/`, rien de spécifique macOS hors config Tauri).

## Lancer

```sh
npm install
npm run tauri dev      # app native en dev
npm run tauri build    # bundle macOS (.app / .dmg)
```

Prérequis : Node ≥ 20, Rust (rustup), Xcode Command Line Tools.

## Structure

```
src/
  model/          Types du projet (miroir simplifié du modèle C# du package) + helpers
  store/          État applicatif Zustand (projet, vue active, mutations)
  persistence/    Sauvegarde/chargement JSON via les plugins Tauri dialog + fs
  features/
    graph/          Vue Graphe : un nœud par Space, une edge par transition
    space-detail/   Vue Détail : cube/grille 3D (react-three-fiber) + vue tableau + panneau couches
src-tauri/        Backend Tauri (config, capabilities, plugins dialog/fs)
```

## Concepts

- **Space** : contexte musical (transport bpm/mesure/boucle) avec des axes N-dimensionnels.
- **Axe** : `levelCount` paliers — comme côté Unity, les valeurs vont de 0 à `levelCount` inclus.
- **Couche** : clip + volume + quantization d'entrée + mode de volume :
  - *Stepped* : un filtre (min/max) par axe ; la couche est active si tous les filtres activés passent.
  - *VolumeEnvelope* : enveloppe trapézoïdale continue pilotée par la valeur normalisée d'un axe.
  Plus loop tail optionnel (résonance de fin de boucle).
- **Transition** : arête orientée entre deux spaces (ou depuis/vers le **silence**, nœud ∅ du
  graphe), avec piste optionnelle (clip, transport, durée effective), sortie source
  (FadeOut/Cut, keep source), déclenchement (variantes avec départs/trackStart/arrival, ou
  fréquence), mode d'arrivée (PerVariant / KeepSourcePosition / Fixed) et tail.

## Design

L'UI suit le design « Music Engine - Orbital » (projet Claude Design « Adaptive music engine
editor ») : thème sombre chaud, tokens CSS dans `src/App.css`. La vue Graphe est la « Space
Map » (nœuds circulaires, halo autour du silence, edges droites à labels pilule) ; la vue
Détail place cube 3D + axes à gauche et les couches à droite (filtre « Point sélectionné »,
éditeur flottant, chips de combinaison dans l'en-tête).

## Vues

- **Space Map (graphe orbital, pannable/zoomable)** : molette pour zoomer (centré curseur),
  glisser le fond pour se déplacer, « Recentrer » pour recadrer. Le silence est **fixé à
  l'écran** en bas au centre (gradient d'ancrage) — il n'a pas d'orbite, c'est une constante.
  Chaque composante de transitions est enracinée sur son premier espace (position monde libre,
  `layout.nodePositions`) ; les autres membres orbitent leur parent BFS à distance fixe (glisser
  ne change que l'ANGLE, `layout.orbitAngles`). Les espaces **sans aucune transition** attendent
  sur une **étagère fixe** en bas à gauche (« Espaces non reliés ») jusqu'à leur première
  transition. Glisser depuis la **bordure** d'un cercle crée une transition ; clic simple → vue
  Détail ; clic sur une ligne → panneau transition (suppression incluse). Les flèches relient
  les bords les plus proches des deux cercles ; deux sens = deux flèches parallèles, symétriques
  autour de la ligne des centres.
- **Détail** : cube 3D (≤ 3 axes affichés, OrbitControls) avec un point cliquable par combinaison ;
  au-delà de 3 axes, sélecteur X/Y/Z + sliders de tranche pour les axes figés. Vue Tableau
  toujours disponible (lignes = combinaisons, colonnes = couches, % pour les volumes d'enveloppe ;
  cliquer une ligne pour éditer les couches de cette combinaison dans le panneau de droite).

## Source de vérité & lien Unity

Le fichier `*.laproj` (JSON, schéma dans `src/model/types.ts`, `formatVersion: 3` — v1/v2 migrés
à l'ouverture) est LA source de vérité. Il vit dans le dossier `Assets/` du projet Unity :

- L'outil l'ouvre et le sauvegarde directement (bouton ou ⌘S, ⇧⌘S = « sauvegarder sous »).
- Côté Unity, `LaprojImporter` (ScriptedImporter du package, `Editor/LaprojImporter.cs`)
  régénère à chaque import le `MusicProject` + ses `ContextPreset` en sous-assets.
  L'inspecteur Unity est en lecture seule pour ces assets.
- Les clips/mixers/paramètres restent des assets Unity, référencés par GUID
  (+ fileID 64 bits en chaîne pour les groupes de mixer, + chemin d'affichage pour les clips).

## Presets (mapping paramètres → axes)

La vue « Presets » édite les ContextPresets embarqués dans le projet : bindings d'axe
(HighestWins / WeightedSum), règles par paramètre (table de seuils ou courbe — clés éditées en
numérique pour l'instant), poids. Un espace choisit son preset dans l'en-tête de la vue Détail.
Les paramètres de jeu (IntParameter / FloatParameter) sont scannés dans `Assets/` quand le
projet ouvert s'y trouve (« ⟳ Rescanner les paramètres » pour rafraîchir).

## Import initial depuis Unity

`node scripts/import-unity.mjs <MonMusicProject.asset> [sortie.laproj]` convertit un
ScriptableObject historique en `.laproj` (presets référencés inlinés, GUID/fileID préservés).
Migration sans perte ; à faire une fois par projet, puis re-pointer les scènes vers l'asset
importé et supprimer les anciens `.asset`.

## Hors scope (phases suivantes)

- Synchronisation avec Unity (écriture dans `Assets/`, `ScriptedImporter` côté package).
- Build Windows.
- Édition du `ContextPreset`.
