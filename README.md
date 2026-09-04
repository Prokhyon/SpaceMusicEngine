# SpaceMusicEngine

Moteur de musique adaptative pour Unity, fondé sur des espaces émotionnels.

La musique d'un lieu ou d'une situation est décrite comme un espace à plusieurs axes
(par exemple Valence, Arousal, Tension). Le jeu déplace la musique dans cet espace en
temps réel ; le moteur choisit les couches audibles à chaque position et enchaîne les
espaces avec des transitions calées sur la mesure.

Ce projet est un travail de bachelor réalisé à l'Université de Genève, dans le cadre du
Bachelor en systèmes d'information et science des services. Le rapport complet est
disponible au même lien que la démo.

Le dépôt contient le package Unity `com.loris.audioengine`, les données et scripts de la
démo réalisée avec le 3D Game Kit d'Unity, et un outil d'édition externe en cours de
développement.

## Téléchargement

Le lien ci-dessous contient :

- le rapport du travail de bachelor ;
- le jeu de démo (build) ;
- le Space Player (build) ;
- `SpaceMusicEngine-DemoAssets.zip` : les ressources Unity de la démo qui ne sont pas
  dans le dépôt (stems audio et scènes).

https://drive.proton.me/urls/WBS1HJSNTG#AIbwdLUuHu0b

## Principe

**Espace.** Un contexte musical (tempo, mesure, longueur de boucle) avec un ou plusieurs
axes. Chaque axe a un nombre de paliers (`levelCount`), de 0 à `levelCount` inclus.

**Couche.** Un clip joué en boucle, synchronisé sur l'horloge audio. Son volume dépend de
la position dans l'espace : soit par filtres min/max sur chaque axe (mode *Stepped*), soit
par une enveloppe continue suivant un axe (mode *VolumeEnvelope*).

**Transition.** Un passage orienté d'un espace à un autre, ou depuis et vers le silence,
avec une piste de transition facultative et un départ quantifié (immédiat, temps, mesure).

**Paramètres et presets.** Le gameplay écrit des valeurs dans des assets `IntParameter` ou
`FloatParameter`. Un `ContextPreset` associé à chaque espace convertit ces valeurs en
positions sur les axes, par table de seuils ou par courbe.

**Composants de scène.**

- `AudioEngineBehaviour` héberge le moteur et charge un `MusicProject`.
- `DefaultMusicBrain` évalue à chaque image le preset de l'espace actif et déplace les axes.
  Pour une logique propre au jeu, on dérive `MusicBrain`.
- `MusicZone` est un trigger qui active un espace quand le joueur y entre.

## Contenu du dépôt

```
Packages/com.loris.audioengine/      Le moteur (package UPM)
  Runtime/                             Moteur
  Editor/                              Import des fichiers .laproj
  Debug/                               Interfaces de test, dont le Space Player
Packages/manifest.json               Dépendances du projet de démo
Assets/Loris/AudioEngine/Data/Demo/  Projets musicaux, presets, paramètres, scènes de test
Assets/Audio/                        Liaison jeu-moteur de la démo
  Writers/                             Écriture des paramètres depuis le gameplay
  Combat/                              Séquences de combat et musique associée
Assets/3DGamekit/Scripts/            Module d'expérimentation et scripts du kit modifiés
Assets/3DGamekit/Data/Experiment/    Questionnaires de l'expérimentation
Assets/3DGamekit/Audio/Mixers/       Mixer de sortie
AuthoringTool/                       Outil d'édition (Tauri, React), en développement
demo-audio-files.txt                 Liste des stems de la démo
scripts/make-demo-zip.sh             Construction du zip des ressources
```

L'arborescence est celle du projet Unity d'origine, ce qui garde valides les références
entre fichiers (GUID des `.meta`). Le `.gitignore` n'autorise que les fichiers listés ;
le dépôt peut donc être placé à la racine du projet complet sans y ajouter le kit.

## Utiliser le moteur dans un autre projet

Ajouter le package depuis le Package Manager (*Add package from git URL*) :

```
https://github.com/Prokhyon/SpaceMusicEngine.git?path=/Packages/com.loris.audioengine
```

ou copier `Packages/com.loris.audioengine` dans le dossier `Packages/` du projet.

Mise en place :

1. Créer un `MusicProject` (*Create > SpaceMusicEngine > Music Project*) et y décrire les
   espaces, couches et transitions.
2. Ajouter un `AudioEngineBehaviour` dans la scène et lui assigner le `MusicProject`.
3. Ajouter un `DefaultMusicBrain` relié à ce composant.
4. Placer des `MusicZone` (collider en trigger) avec l'identifiant de l'espace à activer.
5. Faire écrire au gameplay les valeurs des `IntParameter` et `FloatParameter` utilisés
   par les presets. Les scripts de `Assets/Audio/Writers` en donnent des exemples.

## Menus

Le moteur est regroupé sous **SpaceMusicEngine**, les outils propres à la démo sous
**SpaceMusicEngine Demo**.

| Menu | Entrées |
|---|---|
| *Create > SpaceMusicEngine* | Music Project, Context Preset, Parameters (Int, Float) |
| *Add Component > SpaceMusicEngine* | Audio Engine, Default Music Brain, Music Zone, Debug (Music Debug UI, Space Player) |
| *Create > SpaceMusicEngine Demo* | Questionnaire Definition |
| *Add Component > SpaceMusicEngine Demo* | Writers, Combat, Debug |
| *Tools > SpaceMusicEngine Demo* | Setup Experiment Scenes And UI, Rebake Level Lighting |

## Projets musicaux de la démo

Les projets utilisés par la démo sont les assets `MusicProject` :

| Asset | Contenu |
|---|---|
| `MusicProject.asset` | Niveau 1 : Exploration, Combat, Jungle, Ruines, Boss, Portail |
| `Level2 MusicProject.asset` | Niveau 2 : Seuil, Antichambres, Boss2, Courtyard |
| `Level1StaticMusicProject.asset` | Niveau 1, version non adaptative |
| `Level2StaticProject.asset` | Niveau 2, version non adaptative |

Les fichiers `.laproj` sont des exports de ces projets pour l'outil d'édition. Cet outil
n'est pas terminé ; la démo n'utilise pas encore les `.laproj`.

## Space Player

Le Space Player permet d'écouter les espaces de la démo sans le jeu : choix du projet et
de l'espace, puis réglage des paramètres de jeu à la main.

C'est la scène `Assets/Loris/AudioEngine/Data/Demo/AudioSpaceTestV2.unity`. Elle n'a
besoin que du package, du dossier `Assets/Loris` et des stems audio.

1. Installer les stems : décompresser `SpaceMusicEngine-DemoAssets.zip` à la racine du
   projet Unity (le dossier qui contient `Assets/`).
2. Ouvrir la scène `AudioSpaceTestV2` et lancer Play.
3. Choisir le projet (Niveau 1 ou Niveau 2), puis un espace. Les contrôles affichés
   dépendent du preset de l'espace. F1 masque ou affiche le panneau.

Pour en faire une application autonome, construire un build ne contenant que cette scène.

`AudioSpaceTest.unity` est la version précédente, avec une interface de débogage générique.

## Reconstruire le jeu de démo

Le jeu de démo est le 3D Game Kit d'Unity, auquel sont ajoutés le moteur, les projets
musicaux, les scripts de liaison, les scènes modifiées et un module d'expérimentation
(sélection du groupe, questionnaires, export des données en JSON).

1. Créer un projet Unity 6 vide (pipeline de rendu intégré, *Built-in*).
2. Importer le 3D Game Kit depuis l'Asset Store (Unity Technologies, gratuit) avec le
   Package Manager, rubrique *My Assets*. Accepter le remplacement des réglages du projet.
3. Fermer Unity.
4. Copier le contenu de ce dépôt à la racine du projet, en remplaçant les fichiers
   existants. `Packages/manifest.json` remplace celui du kit, dont les versions de
   packages sont anciennes.
5. Décompresser `SpaceMusicEngine-DemoAssets.zip` à la racine du projet, en remplaçant
   les fichiers existants. Cela ajoute les stems et remplace `Assets/3DGamekit/Scenes`.
6. Rouvrir le projet. Dans *Project Settings > Player*, régler *Active Input Handling*
   sur *Both* : le kit utilise l'ancien système d'entrée, certains scripts le nouveau.
7. Dans *File > Build Profiles*, ajouter les scènes dans cet ordre : `Start`, `Level1`,
   `Level1_Static`, `Level2`, `Level2_Static` (dossier `Assets/3DGamekit/Scenes`).
8. Ouvrir `Start` et lancer Play.

Si l'éclairage des niveaux est incorrect dans le build, *Tools > SpaceMusicEngine Demo > Rebake
Level Lighting* recalcule les lightmaps des quatre niveaux.

## Outil d'édition

`AuthoringTool/` est une application de bureau (Tauri 2, React, TypeScript) pour éditer les
projets musicaux sous forme de fichiers `.laproj`, importés dans Unity par
`LaprojImporter`. Elle est en cours de développement. Voir
[AuthoringTool/README.md](AuthoringTool/README.md).

## Préparer le zip des ressources

Depuis le projet complet :

```bash
scripts/make-demo-zip.sh SpaceMusicEngine-DemoAssets.zip
```

Le zip contient les stems listés dans `demo-audio-files.txt` et le dossier
`Assets/3DGamekit/Scenes`, avec leurs `.meta`. Ces `.meta` portent les identifiants
référencés par les projets et les scènes ; ils doivent rester avec les fichiers.

## Suite prévue

- Terminer l'outil d'édition et faire passer la démo aux `.laproj`.
- Rendre le package autonome, avec la démo dans `Samples~`.
- Choisir une licence.

## Auteur

Loris Thomas
