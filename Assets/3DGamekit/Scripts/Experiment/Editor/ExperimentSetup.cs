using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gamekit3D;
using Gamekit3D.Experiment;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Gamekit3D.Experiment.Editor
{
    public static class ExperimentSetup
    {
        const string Level1Path = "Assets/3DGamekit/Scenes/GamePlay/Level1.unity";
        const string Level1StaticPath = "Assets/3DGamekit/Scenes/GamePlay/Level1_Static.unity";
        const string Level2Path = "Assets/3DGamekit/Scenes/GamePlay/Level2.unity";
        const string Level2StaticPath = "Assets/3DGamekit/Scenes/GamePlay/Level2_Static.unity";
        const string StartScenePath = "Assets/3DGamekit/Scenes/Start.unity";
        const string ExperimentScenePath = "Assets/3DGamekit/Scenes/Experiment.unity"; // obsolete, removed by Run()
        const string InterstitialScenePath = "Assets/3DGamekit/Scenes/Interstitial.unity";
        const string DataFolder = "Assets/3DGamekit/Data/Experiment";

        // #171724, fully opaque — no more see-through overlay during questionnaires/Stroop tests.
        static readonly Color PanelBg = new Color(0.0902f, 0.0902f, 0.1412f, 1f);
        static readonly Color ErrorColor = new Color(0.95f, 0.4f, 0.4f);

        static QuestionnaireDefinition s_PreQuestionnaire;
        static QuestionnaireDefinition s_EmotionQuestionnaire;
        static QuestionnaireDefinition s_IeqQuestionnaire;
        static QuestionnaireDefinition s_FinalQuestionnaire;

        [MenuItem("Tools/SpaceMusicEngine Demo/Setup Experiment Scenes And UI")]
        public static void Run()
        {
            DuplicateLevelScenes();

            PatchLevelExit(Level1Path);
            PatchLevelExit(Level1StaticPath);
            PatchLevelExit(Level2Path);
            PatchLevelExit(Level2StaticPath);

            AddBossDoorBlocker(Level1Path);
            AddBossDoorBlocker(Level1StaticPath);

            CreateQuestionnaireAssets();
            CreateInterstitialScene();
            RemoveObsoleteExperimentScene();
            SetupStartScene();
            UpdateBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("ExperimentSetup: terminé avec succès.");
        }

        // ---------------------------------------------------------------
        // Scene duplication
        // ---------------------------------------------------------------

        static void DuplicateLevelScenes()
        {
            CopyIfMissing(Level1Path, Level1StaticPath);
            CopyIfMissing(Level2Path, Level2StaticPath);
        }

        static void CopyIfMissing(string src, string dst)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(dst) != null) return;

            if (!AssetDatabase.CopyAsset(src, dst))
            {
                throw new Exception("Échec de la copie de scène " + src + " -> " + dst);
            }
        }

        // ---------------------------------------------------------------
        // Level-exit trigger patching
        // ---------------------------------------------------------------

        const string InterstitialSceneName = "Interstitial";

        // The level's own exit portal (a native Gamekit3D.TransitionPoint, triggered by the game's
        // built-in SceneController on OnTriggerEnter) is the actual, reliable mechanism that fires
        // when the player reaches the end of a level — not a custom collider of ours. Redirecting
        // its destination directly is far more robust than trying to intercept/replace it.
        static void PatchLevelExit(string scenePath)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var transitionPoints = UnityEngine.Object.FindObjectsByType<TransitionPoint>(FindObjectsInactive.Include)
                .Where(tp => tp.transitionType != TransitionPoint.TransitionType.SameScene)
                .ToList();

            if (transitionPoints.Count != 1)
            {
                throw new Exception("Attendu exactement 1 TransitionPoint de sortie (hors SameScene) dans " +
                    scenePath + ", trouvé " + transitionPoints.Count);
            }

            var exitPoint = transitionPoints[0];

            // Remove any leftover component from a previous, now-abandoned approach (a custom
            // trigger component that used to shadow this TransitionPoint). The class itself has
            // since been deleted from the project, so any remaining instance is a missing script.
            int removedCount = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(exitPoint.gameObject);
            bool hadLegacyExit = removedCount > 0;

            bool alreadyPatched = !hadLegacyExit && exitPoint.newSceneName == InterstitialSceneName && exitPoint.enabled;

            exitPoint.newSceneName = InterstitialSceneName;
            exitPoint.enabled = true;

            bool dirty = !alreadyPatched;

            if (exitPoint.gameObject.GetComponent<LevelCompletionLogger>() == null)
            {
                var completionLogger = exitPoint.gameObject.AddComponent<LevelCompletionLogger>();
                completionLogger.transitioningGameObject = exitPoint.transitioningGameObject;
                dirty = true;
            }

            if (UnityEngine.Object.FindObjectsByType<GameplayLogger>(FindObjectsInactive.Include).Length == 0)
            {
                var loggerGo = new GameObject("GameplayLogger");
                loggerGo.AddComponent<GameplayLogger>();
                dirty = true;
            }

            if (dirty)
            {
                EditorUtility.SetDirty(exitPoint.gameObject);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        // ---------------------------------------------------------------
        // Boss-door barrier (Level1 / Level1_Static)
        // ---------------------------------------------------------------

        const string GrenadierPrefabGuid = "71a1be879433fcb46b1fb72b19e392ef";
        const string BossBarrierLayerName = "PlayerBarrier";
        const string BossBarrierGoName = "BossDoorBarrier";

        // DoorHuge2 is the huge gate that closes behind the player once they reach the Grenadier
        // guarding it; the Grenadier is treated as this level's boss. The barrier sits at the same
        // local position as the door (same parent, so world placement matches exactly regardless of
        // the parent's own transform) and physically blocks only the player until the Grenadier dies.
        static void AddBossDoorBlocker(string scenePath)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            if (GameObject.Find(BossBarrierGoName) != null)
            {
                return; // already patched
            }

            var door = GameObject.Find("DoorHuge2");
            if (door == null)
            {
                throw new Exception("DoorHuge2 introuvable dans " + scenePath);
            }

            EnemyController boss = null;
            float bestDist = float.MaxValue;
            foreach (var enemy in UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsInactive.Include))
            {
                var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(enemy.gameObject);
                if (prefabRoot == null) continue;

                var source = PrefabUtility.GetCorrespondingObjectFromSource(prefabRoot);
                if (source == null) continue;

                var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
                if (guid != GrenadierPrefabGuid) continue;

                float dist = Vector3.Distance(enemy.transform.position, door.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    boss = enemy;
                }
            }

            if (boss == null)
            {
                throw new Exception("Grenadier (boss) introuvable près de DoorHuge2 dans " + scenePath);
            }

            var bossDamageable = boss.GetComponentInParent<Damageable>();
            if (bossDamageable == null)
            {
                bossDamageable = boss.GetComponentInChildren<Damageable>();
            }
            if (bossDamageable == null)
            {
                throw new Exception("Le Grenadier identifié (" + boss.gameObject.name + ") n'a pas de Damageable dans " + scenePath);
            }

            int barrierLayer = LayerMask.NameToLayer(BossBarrierLayerName);
            if (barrierLayer < 0)
            {
                throw new Exception("Layer '" + BossBarrierLayerName + "' introuvable — vérifiez ProjectSettings/TagManager.asset.");
            }

            var barrierGo = new GameObject(BossBarrierGoName);
            barrierGo.layer = barrierLayer;
            barrierGo.transform.SetParent(door.transform.parent, false);
            barrierGo.transform.localPosition = door.transform.localPosition;
            barrierGo.transform.localRotation = door.transform.localRotation;
            barrierGo.transform.localScale = door.transform.localScale;

            var box = barrierGo.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.size = new Vector3(16f, 20f, 2f); // generous coverage of the gate opening; resize in the Inspector if needed

            var barrier = barrierGo.AddComponent<PlayerOnlyBarrier>();
            barrier.bossDamageable = bossDamageable;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // ---------------------------------------------------------------
        // Questionnaire assets
        // ---------------------------------------------------------------

        static void CreateQuestionnaireAssets()
        {
            EnsureFolder("Assets/3DGamekit/Data");
            EnsureFolder(DataFolder);

            s_PreQuestionnaire = CreateOrReplaceAsset<QuestionnaireDefinition>(DataFolder + "/PreQuestionnaire.asset");
            s_PreQuestionnaire.title = "Questionnaire pré-session";
            s_PreQuestionnaire.items = BuildPreQuestionnaireItems();

            s_EmotionQuestionnaire = CreateOrReplaceAsset<QuestionnaireDefinition>(DataFolder + "/EmotionQuestionnaire.asset");
            s_EmotionQuestionnaire.title = "Évaluation émotionnelle";
            s_EmotionQuestionnaire.items = BuildEmotionItems();

            s_IeqQuestionnaire = CreateOrReplaceAsset<QuestionnaireDefinition>(DataFolder + "/IEQQuestionnaire.asset");
            s_IeqQuestionnaire.title = "Immersive Experience Questionnaire (IEQ)";
            s_IeqQuestionnaire.items = BuildIeqItems();

            s_FinalQuestionnaire = CreateOrReplaceAsset<QuestionnaireDefinition>(DataFolder + "/FinalQuestionnaire.asset");
            s_FinalQuestionnaire.title = "Questions finales";
            s_FinalQuestionnaire.items = BuildFinalQuestionnaireItems();

            EditorUtility.SetDirty(s_PreQuestionnaire);
            EditorUtility.SetDirty(s_EmotionQuestionnaire);
            EditorUtility.SetDirty(s_IeqQuestionnaire);
            EditorUtility.SetDirty(s_FinalQuestionnaire);
        }

        static List<QuestionItem> BuildPreQuestionnaireItems()
        {
            return new List<QuestionItem>
            {
                new QuestionItem { id = "age", text = "Quel est votre âge ?", type = QuestionType.Numeric, required = true },
                new QuestionItem { id = "gender", text = "Quel est votre genre ?", type = QuestionType.SingleChoice, required = true,
                    options = new[] { "Homme", "Femme", "Non-binaire", "Préfère ne pas répondre" } },
                new QuestionItem { id = "hours_per_week", text = "Combien d'heures par semaine jouez-vous à des jeux vidéo en moyenne ?", type = QuestionType.SingleChoice, required = true,
                    options = new[] { "Moins d'1h", "1–3h", "3–7h", "7–15h", "Plus de 15h" } },
                new QuestionItem { id = "game_types", text = "Quel type de jeux pratiquez-vous principalement ? (réponse multiple)", type = QuestionType.MultiChoice, required = true,
                    options = new[] { "Action", "RPG", "FPS", "Puzzle", "Plateforme", "Aucun", "Autre" } },
                new QuestionItem { id = "tpp_experience", text = "Avez-vous déjà joué à un jeu en vue à la troisième personne avec combats et exploration ?", type = QuestionType.SingleChoice, required = true,
                    options = new[] { "Oui", "Non" } },
                new QuestionItem { id = "music_training", text = "Avez-vous une formation musicale ou pratiquez-vous un instrument ?", type = QuestionType.SingleChoice, required = true,
                    options = new[] { "Oui, en amateur", "Oui, de façon avancée", "Non" } },
                new QuestionItem { id = "music_sensitivity", text = "Comment décririez-vous votre sensibilité habituelle à la musique dans les jeux vidéo ?", type = QuestionType.SingleChoice, required = true,
                    options = new[]
                    {
                        "Je ne prête généralement pas attention à la musique",
                        "Je la remarque sans qu'elle m'affecte particulièrement",
                        "Elle influence nettement mon ressenti pendant le jeu",
                    } },
            };
        }

        // Affective Slider (Betella & Verschure, 2016) + GEMS sélectif (Zentner, Grandjean & Scherer,
        // 2008), administré immédiatement après l'estimation du temps de jeu et avant l'IEQ. Tension
        // et Sérénité apparaissent volontairement dans les deux phases (exploration/combat) : c'est
        // ce qui permet de calculer l'indice de contraste émotionnel à l'analyse (voir ids
        // explo_tension/combat_tension et explo_serenity/combat_serenity).
        static List<QuestionItem> BuildEmotionItems()
        {
            string[] gemsScale = { "Pas du tout", "Légèrement", "Modérément", "Beaucoup", "Extrêmement" };

            QuestionItem Gems(string id, string dimension, string descriptors) => new QuestionItem
            {
                id = id,
                text = dimension + " (" + descriptors + ")",
                type = QuestionType.SingleChoice,
                options = gemsScale,
                required = true,
            };

            return new List<QuestionItem>
            {
                new QuestionItem { id = "affective_slider_intro", type = QuestionType.SectionHeader,
                    text = "En pensant à l'ensemble du niveau que vous venez de jouer…" },
                new QuestionItem { id = "valence", text = "Valence (1 = Très déplaisant, 7 = Très plaisant)",
                    type = QuestionType.Likert7, required = true },
                new QuestionItem { id = "arousal", text = "Arousal (1 = Très calme / endormi, 7 = Très activé / excité)",
                    type = QuestionType.Likert7, required = true },

                new QuestionItem { id = "gems_explo_intro", type = QuestionType.SectionHeader,
                    text = "Pensez aux moments où vous exploriez l'environnement, sans ennemi immédiatement présent ou menaçant. " +
                        "\nPour chacune des sensations ci-dessous, indiquez à quel point vous l'avez ressentie pendant ces moments." },
                Gems("explo_serenity", "Sérénité", "sérénité · calme · apaisement"),
                Gems("explo_wonder", "Émerveillement", "émerveillement · fascination · étonnement"),
                Gems("explo_transcendence", "Transcendance", "profondément ému · touché · frissons"),
                Gems("explo_tension", "Tension", "tension · nervosité · anxiété"),

                new QuestionItem { id = "gems_combat_intro", type = QuestionType.SectionHeader,
                    text = "Pensez maintenant aux moments de combat, lorsque vous affrontiez des ennemis. " +
                        "\nPour chacune des sensations ci-dessous, indiquez à quel point vous l'avez ressentie pendant ces moments." },
                Gems("combat_tension", "Tension", "tension · nervosité · anxiété"),
                Gems("combat_power", "Puissance", "puissance · énergie · force"),
                Gems("combat_joy", "Activation joyeuse", "joie · enthousiasme · bonheur"),
                Gems("combat_serenity", "Sérénité", "sérénité · calme · apaisement"),
            };
        }

        static List<QuestionItem> BuildFinalQuestionnaireItems()
        {
            string[] levelOptions = { "Niveau 1", "Niveau 2", "Équivalent" };

            return new List<QuestionItem>
            {
                new QuestionItem { id = "immersion_compare", text = "Dans quel niveau aviez-vous l'impression d'être le plus absorbé ou immergé ?", type = QuestionType.SingleChoice, required = true, options = levelOptions },
                new QuestionItem { id = "time_compare", text = "Dans quel niveau le temps vous a-t-il semblé passer le plus vite ?", type = QuestionType.SingleChoice, required = true, options = levelOptions },
                new QuestionItem { id = "focus_compare", text = "Dans quel niveau votre concentration vous a-t-elle semblé la plus soutenue ?", type = QuestionType.SingleChoice, required = true, options = levelOptions },
                new QuestionItem { id = "music_diff_noticed", text = "Avez-vous remarqué des différences entre les ambiances sonores ou musicales des deux niveaux ?", type = QuestionType.SingleChoice, required = true,
                    options = new[] { "Oui", "Non", "Je ne suis pas sûr(e)" } },
                new QuestionItem { id = "music_diff_desc", text = "Si oui, pouvez-vous décrire ce que vous avez perçu ?", type = QuestionType.OpenText, required = false },
                new QuestionItem { id = "music_influence", text = "La musique ou le son a-t-il influencé votre façon de jouer ou votre ressenti ?", type = QuestionType.SingleChoice, required = true,
                    options = new[] { "Oui", "Non", "Peut-être" } },
                new QuestionItem { id = "music_influence_desc", text = "Si oui, de quelle façon ?", type = QuestionType.OpenText, required = false },
                new QuestionItem { id = "felt_difference", text = "Pensez-vous avoir ressenti la différence pendant le jeu ? Dans quel sens ?", type = QuestionType.OpenText, required = false },
                new QuestionItem { id = "marking_moment", text = "Quel moment du jeu vous a le plus marqué, et pourquoi ?", type = QuestionType.OpenText, required = false },
                new QuestionItem { id = "distraction", text = "Y a-t-il quelque chose qui vous a distrait ou sorti du jeu à un moment ?", type = QuestionType.OpenText, required = false },
                new QuestionItem { id = "general_remarks", text = "Avez-vous des remarques générales sur l'expérience ?", type = QuestionType.OpenText, required = false },
            };
        }

        // Traduction française du Immersive Experience Questionnaire (31 items, échelle 1-7) plus la
        // question de contrôle unique sur l'immersion (échelle 1-10), d'après :
        // Jennett, C., Cox, A. L., Cairns, P., Dhoparee, S., Epps, A., Tijs, T. & Walton, A. (2008).
        // Measuring and defining the experience of immersion in games. IJHCS, 66, 641-661.
        // Les items 6, 8, 9, 10, 18 et 20 sont marqués reverseScored=true : à inverser (1<->7, etc.)
        // au moment du calcul du score, pas à l'affichage.
        static List<QuestionItem> BuildIeqItems()
        {
            QuestionItem Item(int number, string text, string lowLabel, string highLabel, bool reverse = false)
            {
                return new QuestionItem
                {
                    id = "ieq_" + number,
                    text = text + " (1 = " + lowLabel + ", 7 = " + highLabel + ")",
                    type = QuestionType.Likert7,
                    required = true,
                    reverseScored = reverse,
                };
            }

            var items = new List<QuestionItem>
            {
                Item(1, "Dans quelle mesure le jeu a-t-il retenu votre attention ?", "Pas du tout", "Beaucoup"),
                Item(2, "Dans quelle mesure vous êtes-vous senti(e) concentré(e) sur le jeu ?", "Pas du tout", "Beaucoup"),
                Item(3, "Quel effort avez-vous fourni pour jouer à ce jeu ?", "Très peu", "Beaucoup"),
                Item(4, "Avez-vous eu l'impression de donner le meilleur de vous-même ?", "Pas du tout", "Tout à fait"),
                Item(5, "Dans quelle mesure avez-vous perdu la notion du temps, c'est-à-dire le jeu a-t-il absorbé votre attention au point de ne pas vous ennuyer ?", "Pas du tout", "Beaucoup"),
                Item(6, "Dans quelle mesure étiez-vous consciemment conscient(e) d'être dans le monde réel pendant que vous jouiez ?", "Pas du tout", "Tout à fait", reverse: true),
                Item(7, "Dans quelle mesure avez-vous oublié vos préoccupations quotidiennes ?", "Pas du tout", "Beaucoup"),
                Item(8, "Dans quelle mesure étiez-vous conscient(e) de vous-même dans votre environnement ?", "Pas du tout", "Très conscient(e)", reverse: true),
                Item(9, "Dans quelle mesure avez-vous remarqué les événements se produisant autour de vous ?", "Pas du tout", "Beaucoup", reverse: true),
                Item(10, "Avez-vous, à un moment donné, ressenti l'envie d'arrêter de jouer pour voir ce qui se passait autour de vous ?", "Pas du tout", "Tout à fait", reverse: true),
                Item(11, "Dans quelle mesure avez-vous eu l'impression d'interagir avec l'environnement du jeu ?", "Pas du tout", "Tout à fait"),
                Item(12, "Dans quelle mesure avez-vous eu l'impression d'être séparé(e) de votre environnement réel ?", "Pas du tout", "Tout à fait"),
                Item(13, "Dans quelle mesure avez-vous eu l'impression que le jeu était une expérience amusante plutôt qu'une tâche que vous accomplissiez ?", "Pas du tout", "Tout à fait"),
                Item(14, "Dans quelle mesure votre sentiment d'être dans l'environnement du jeu était-il plus fort que votre sentiment d'être dans le monde réel ?", "Pas du tout", "Tout à fait"),
                Item(15, "Vous êtes-vous, à un moment donné, senti(e) tellement impliqué(e) que vous ne remarquiez même plus utiliser les commandes, comme si cela devenait sans effort ?", "Pas du tout", "Tout à fait"),
                Item(16, "Dans quelle mesure avez-vous eu l'impression de vous déplacer dans le jeu selon votre propre volonté ?", "Pas du tout", "Tout à fait"),
                Item(17, "Dans quelle mesure avez-vous trouvé le jeu difficile ?", "Pas du tout", "Très difficile"),
                Item(18, "Y a-t-il eu des moments pendant le jeu où vous avez simplement eu envie d'abandonner ?", "Pas du tout", "Beaucoup", reverse: true),
                Item(19, "Dans quelle mesure vous êtes-vous senti(e) motivé(e) en jouant ?", "Pas du tout", "Beaucoup"),
                Item(20, "Dans quelle mesure avez-vous trouvé le jeu facile ?", "Pas du tout", "Tout à fait", reverse: true),
                Item(21, "Dans quelle mesure aviez-vous l'impression de progresser vers la fin du jeu ?", "Pas du tout", "Beaucoup"),
                Item(22, "Selon vous, à quel point avez-vous bien performé dans le jeu ?", "Très mal", "Très bien"),
                Item(23, "Dans quelle mesure vous êtes-vous senti(e) émotionnellement attaché(e) au jeu ?", "Pas du tout", "Tout à fait"),
                Item(24, "Dans quelle mesure étiez-vous intéressé(e) de voir comment les événements du jeu allaient évoluer ?", "Pas du tout", "Beaucoup"),
                Item(25, "À quel point vouliez-vous « gagner » la partie ?", "Pas du tout", "Tout à fait"),
                Item(26, "Étiez-vous en suspense quant à savoir si vous alliez bien vous en sortir dans le jeu ?", "Pas du tout", "Tout à fait"),
                Item(27, "Vous êtes-vous, à un moment donné, senti(e) tellement impliqué(e) que vous avez eu envie de parler directement au jeu ?", "Pas du tout", "Tout à fait"),
                Item(28, "Dans quelle mesure avez-vous apprécié les graphismes et l'esthétique visuelle ?", "Pas du tout", "Beaucoup"),
                Item(29, "Dans quelle mesure diriez-vous avoir aimé jouer à ce jeu ?", "Pas du tout", "Beaucoup"),
                Item(30, "Lorsque la partie s'est terminée, avez-vous été déçu(e) que ce soit fini ?", "Pas du tout", "Tout à fait"),
                Item(31, "Souhaiteriez-vous rejouer à ce jeu ?", "Certainement pas", "Certainement oui"),
            };

            items.Add(new QuestionItem
            {
                id = "ieq_immersion_single",
                text = "Dans quelle mesure vous êtes-vous senti(e) immergé(e) ? (1 = Pas du tout immergé(e), 10 = Très immergé(e))",
                type = QuestionType.Likert10,
                required = true,
            });

            return items;
        }

        static T CreateOrReplaceAsset<T>(string path) where T : ScriptableObject
        {
            if (AssetDatabase.LoadAssetAtPath<T>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string name = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, name);
        }

        // ---------------------------------------------------------------
        // Experiment scene / UI construction
        // ---------------------------------------------------------------

        static void RemoveObsoleteExperimentScene()
        {
            // The whole UI used to live in its own bare bootstrap scene ("Experiment.unity"). We now
            // build the same persistent root directly into the existing 3D Game Kit "Start" scene
            // instead, so that scene (with its own title-screen visuals) is what participants see
            // first. The old scene asset is no longer referenced anywhere and would otherwise linger.
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ExperimentScenePath) != null)
            {
                AssetDatabase.DeleteAsset(ExperimentScenePath);
            }
        }

        static void SetupStartScene()
        {
            var scene = EditorSceneManager.OpenScene(StartScenePath, OpenSceneMode.Single);

            // Neutralize the vanilla "walk through the portal" demo flow: our own controlled flow
            // decides which level scene loads next (based on group assignment), so the participant
            // must not be able to bypass the protocol by wandering into the original exit trigger.
            foreach (var tp in UnityEngine.Object.FindObjectsByType<TransitionPoint>(FindObjectsInactive.Include))
            {
                if (tp.transitionType != TransitionPoint.TransitionType.SameScene)
                {
                    tp.enabled = false;
                }
            }

            // Avoid two competing EventSystems (the scene's own + ours). Ours, added below, persists
            // for the whole app lifetime and is sufficient for both the existing Start-scene UI
            // (Options/Audio/Controls) and our own panels.
            foreach (var es in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include))
            {
                UnityEngine.Object.DestroyImmediate(es.gameObject);
            }

            var existingRoot = GameObject.Find("ExperimentRoot");
            if (existingRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(existingRoot);
            }

            var rootGo = new GameObject("ExperimentRoot");
            var manager = rootGo.AddComponent<ExperimentManager>();

            var esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            esGo.transform.SetParent(rootGo.transform, false);

            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(rootGo.transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            manager.startOverlayPanel = BuildStartOverlayPanel(canvasGo.transform);
            manager.groupSelectPanel = BuildGroupSelectPanel(canvasGo.transform);
            manager.questionnairePanel = BuildQuestionnairePanel(canvasGo.transform);
            manager.stroopPanel = BuildStroopPanel(canvasGo.transform);
            manager.timeEstimatePanel = BuildTimeEstimatePanel(canvasGo.transform);
            manager.pausePanel = BuildPausePanel(canvasGo.transform);
            manager.endScreenPanel = BuildEndScreenPanel(canvasGo.transform);
            manager.infoPanel = BuildInfoPanel(canvasGo.transform);

            manager.preQuestionnaire = s_PreQuestionnaire;
            manager.emotionQuestionnaire = s_EmotionQuestionnaire;
            manager.ieqQuestionnaire = s_IeqQuestionnaire;
            manager.finalQuestionnaire = s_FinalQuestionnaire;

            // The vanilla title screen's "START" button begins the study flow directly (same action
            // as the overlay's own button) instead of transitioning straight into Level1. This has to
            // be redone every run since startOverlayPanel above is a fresh instance each time.
            var startButtonGo = FindIncludingInactive("StartButton");
            if (startButtonGo != null)
            {
                var startButton = startButtonGo.GetComponent<Button>();
                startButton.onClick = new Button.ButtonClickedEvent();
                UnityEventTools.AddPersistentListener(startButton.onClick, manager.startOverlayPanel.OnStartClicked);
                PrefabUtility.RecordPrefabInstancePropertyModifications(startButton);
                EditorUtility.SetDirty(startButtonGo);
            }

            // The overlay's own button is redundant now that StartButton does its job.
            manager.startOverlayPanel.startButton.gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // GameObject.Find skips inactive objects; StartButton lives inside the vanilla UICanvas
        // prefab instance and isn't reachable by name any other easy way from here.
        static GameObject FindIncludingInactive(string name)
        {
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name == name) return t.gameObject;
            }
            return null;
        }

        static StartOverlayPanel BuildStartOverlayPanel(Transform parent)
        {
            var root = UIFactory.CreateRect(parent, "StartOverlayPanel");
            UIFactory.Stretch(root);
            var panel = root.gameObject.AddComponent<StartOverlayPanel>();

            var button = UIFactory.CreateButton(root, "Commencer l'étude", out var label);
            UIFactory.SetPreferredSize(button.gameObject, 440, 90);
            label.fontSize = 28;

            var buttonRt = (RectTransform)button.transform;
            buttonRt.anchorMin = new Vector2(0.5f, 0f);
            buttonRt.anchorMax = new Vector2(0.5f, 0f);
            buttonRt.pivot = new Vector2(0.5f, 0f);
            buttonRt.anchoredPosition = new Vector2(0, 120);

            panel.startButton = button;
            UnityEventTools.AddPersistentListener(button.onClick, panel.OnStartClicked);

            return panel;
        }

        static void CreateInterstitialScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(InterstitialScenePath) != null)
            {
                AssetDatabase.DeleteAsset(InterstitialScenePath);
            }

            // Deliberately empty: no EventSystem, no Canvas, no ExperimentManager. It exists purely
            // so the level scene (with its player, enemies, adaptive music) is fully unloaded between
            // gameplay sessions, without spawning anything that could collide with the persistent
            // DontDestroyOnLoad EventSystem/Canvas coming from the original Experiment scene load.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Camera", typeof(Camera));
            var camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.08f, 1f);
            camera.cullingMask = 0;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, InterstitialScenePath);
        }

        static void BuildPanelLayout(GameObject panelRoot, string titleText, out TextMeshProUGUI title,
            out RectTransform middle, out RectTransform bottomBar)
        {
            var layoutRt = UIFactory.CreateRect(panelRoot.transform, "Layout");
            UIFactory.Stretch(layoutRt);
            layoutRt.offsetMin = new Vector2(60, 40);
            layoutRt.offsetMax = new Vector2(-60, -40);

            var vlayout = layoutRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlayout.spacing = 24;
            vlayout.childForceExpandWidth = true;
            vlayout.childForceExpandHeight = false;
            vlayout.childControlWidth = true;
            vlayout.childControlHeight = true;

            title = UIFactory.CreateText(layoutRt, titleText, 34, TextAlignmentOptions.Center);
            UIFactory.AddLayoutElement(title.gameObject, 60, 60);

            middle = UIFactory.CreateRect(layoutRt, "Middle");
            UIFactory.AddLayoutElement(middle.gameObject).flexibleHeight = 1;

            bottomBar = UIFactory.CreateRect(layoutRt, "BottomBar");
            UIFactory.AddHorizontalLayout(bottomBar.gameObject, 20);
            UIFactory.AddLayoutElement(bottomBar.gameObject, 80, 80);
        }

        static void SetSize(Component c, float w, float h)
        {
            UIFactory.SetPreferredSize(c.gameObject, w, h);
        }

        static GroupSelectPanel BuildGroupSelectPanel(Transform parent)
        {
            var panelRoot = UIFactory.CreatePanelRoot(parent, "GroupSelectPanel", PanelBg);
            BuildPanelLayout(panelRoot, "Étude — Musique adaptative", out _, out var middle, out var bottomBar);

            UIFactory.AddVerticalLayout(middle.gameObject, 16);

            UIFactory.CreateText(middle, "Identifiant participant", 20, TextAlignmentOptions.Left);
            var idInput = UIFactory.CreateInputField(middle, TMP_InputField.ContentType.Standard);
            UIFactory.AddLayoutElement(idInput.gameObject, 55, 55);

            UIFactory.CreateText(middle, "Groupe", 20, TextAlignmentOptions.Left);
            var groupRow = UIFactory.CreateRect(middle, "GroupRow");
            UIFactory.AddHorizontalLayout(groupRow.gameObject, 20);
            UIFactory.AddLayoutElement(groupRow.gameObject, 60, 60);

            var groupAButton = UIFactory.CreateButton(groupRow, "Groupe A", out _);
            SetSize(groupAButton, 240, 60);
            var groupBButton = UIFactory.CreateButton(groupRow, "Groupe B", out _);
            SetSize(groupBButton, 240, 60);

            var feedback = UIFactory.CreateText(middle, string.Empty, 18, TextAlignmentOptions.Center, ErrorColor);
            UIFactory.AddLayoutElement(feedback.gameObject, 30, 30);

            var startButton = UIFactory.CreateButton(bottomBar, "Commencer", out _);
            SetSize(startButton, 300, 65);

            var panel = panelRoot.AddComponent<GroupSelectPanel>();
            panel.participantIdInput = idInput;
            panel.groupAButton = groupAButton;
            panel.groupBButton = groupBButton;
            panel.startButton = startButton;
            panel.feedbackText = feedback;

            UnityEventTools.AddPersistentListener(groupAButton.onClick, panel.SelectGroupA);
            UnityEventTools.AddPersistentListener(groupBButton.onClick, panel.SelectGroupB);
            UnityEventTools.AddPersistentListener(startButton.onClick, panel.TryStart);

            panelRoot.SetActive(false);
            return panel;
        }

        static InfoPanel BuildInfoPanel(Transform parent)
        {
            var panelRoot = UIFactory.CreatePanelRoot(parent, "InfoPanel", PanelBg);
            BuildPanelLayout(panelRoot, "Information", out var title, out var middle, out var bottomBar);

            var vlayout = UIFactory.AddVerticalLayout(middle.gameObject, 20);
            vlayout.childAlignment = TextAnchor.MiddleCenter;

            var body = UIFactory.CreateText(middle, string.Empty, 24, TextAlignmentOptions.Center);
            UIFactory.AddLayoutElement(body.gameObject).flexibleHeight = 1;

            var continueButton = UIFactory.CreateButton(bottomBar, "Continuer", out _);
            SetSize(continueButton, 300, 65);

            var panel = panelRoot.AddComponent<InfoPanel>();
            panel.titleText = title;
            panel.bodyText = body;
            panel.continueButton = continueButton;

            UnityEventTools.AddPersistentListener(continueButton.onClick, panel.Continue);

            panelRoot.SetActive(false);
            return panel;
        }

        static QuestionnairePanel BuildQuestionnairePanel(Transform parent)
        {
            var panelRoot = UIFactory.CreatePanelRoot(parent, "QuestionnairePanel", PanelBg);
            BuildPanelLayout(panelRoot, "Questionnaire", out var title, out var middle, out var bottomBar);

            var scrollRect = UIFactory.CreateScrollView(middle, out var content);

            var continueButton = UIFactory.CreateButton(bottomBar, "Continuer", out _);
            SetSize(continueButton, 260, 60);

            var validation = UIFactory.CreateText(bottomBar, string.Empty, 16, TextAlignmentOptions.MidlineLeft, ErrorColor);
            SetSize(validation, 600, 60);

            var panel = panelRoot.AddComponent<QuestionnairePanel>();
            panel.titleText = title;
            panel.contentContainer = content;
            panel.scrollRect = scrollRect;
            panel.continueButton = continueButton;
            panel.validationText = validation;

            UnityEventTools.AddPersistentListener(continueButton.onClick, panel.TrySubmit);

            panelRoot.SetActive(false);
            return panel;
        }

        static StroopPanel BuildStroopPanel(Transform parent)
        {
            var panelRoot = UIFactory.CreatePanelRoot(parent, "StroopPanel", PanelBg);
            BuildPanelLayout(panelRoot, "Test de Stroop", out _, out var middle, out var bottomBar);

            UIFactory.AddVerticalLayout(middle.gameObject, 20);

            var instruction = UIFactory.CreateText(middle, "Entraînement", 22, TextAlignmentOptions.Center);
            UIFactory.AddLayoutElement(instruction.gameObject, 40, 40);

            var stimulus = UIFactory.CreateText(middle, string.Empty, 72, TextAlignmentOptions.Center);
            UIFactory.AddLayoutElement(stimulus.gameObject).flexibleHeight = 1;

            var feedback = UIFactory.CreateText(middle, string.Empty, 24, TextAlignmentOptions.Center);
            UIFactory.AddLayoutElement(feedback.gameObject, 40, 40);

            var buttonGrid = UIFactory.CreateRect(middle, "ColorButtons");
            UIFactory.AddGridLayout(buttonGrid.gameObject, new Vector2(220, 90), 20, columns: 2);
            UIFactory.AddLayoutElement(buttonGrid.gameObject, 220, 220);

            var colorDefs = new (string name, Color color)[]
            {
                ("Rouge", new Color(0.82f, 0.16f, 0.16f)),
                ("Vert", new Color(0.16f, 0.65f, 0.27f)),
                ("Bleu", new Color(0.2f, 0.4f, 0.85f)),
                ("Jaune", new Color(0.92f, 0.82f, 0.15f)),
            };

            var colorOptions = new List<StroopPanel.ColorOption>();
            foreach (var def in colorDefs)
            {
                // The response buttons stay neutral — only the stimulus word's ink colour should
                // convey colour information, otherwise the buttons themselves would cue the answer.
                var btn = UIFactory.CreateButton(buttonGrid, def.name, out _);
                colorOptions.Add(new StroopPanel.ColorOption { button = btn, colorName = def.name, color = def.color });
            }

            var panel = panelRoot.AddComponent<StroopPanel>();
            panel.instructionText = instruction;
            panel.stimulusText = stimulus;
            panel.feedbackText = feedback;
            panel.colorOptions = colorOptions;

            var colorClickHandlers = new UnityEngine.Events.UnityAction[]
            {
                panel.OnClickColor0, panel.OnClickColor1, panel.OnClickColor2, panel.OnClickColor3,
            };
            for (int i = 0; i < colorOptions.Count && i < colorClickHandlers.Length; i++)
            {
                UnityEventTools.AddPersistentListener(colorOptions[i].button.onClick, colorClickHandlers[i]);
            }

            panelRoot.SetActive(false);
            return panel;
        }

        static TimeEstimatePanel BuildTimeEstimatePanel(Transform parent)
        {
            var panelRoot = UIFactory.CreatePanelRoot(parent, "TimeEstimatePanel", PanelBg);
            BuildPanelLayout(panelRoot, "Estimation du temps de jeu", out _, out var middle, out var bottomBar);

            var vlayout = UIFactory.AddVerticalLayout(middle.gameObject, 24);
            vlayout.childAlignment = TextAnchor.MiddleCenter;

            UIFactory.CreateText(middle,
                "Sans chercher à vérifier, combien de temps pensez-vous avoir joué à ce niveau ?\nDonnez votre meilleure estimation en minutes.",
                22, TextAlignmentOptions.Center);

            var input = UIFactory.CreateInputField(middle, TMP_InputField.ContentType.DecimalNumber);
            UIFactory.AddLayoutElement(input.gameObject, 60, 60);

            var validation = UIFactory.CreateText(middle, string.Empty, 16, TextAlignmentOptions.Center, ErrorColor);

            var submitButton = UIFactory.CreateButton(bottomBar, "Valider", out _);
            SetSize(submitButton, 260, 60);

            var panel = panelRoot.AddComponent<TimeEstimatePanel>();
            panel.minutesInput = input;
            panel.submitButton = submitButton;
            panel.validationText = validation;

            UnityEventTools.AddPersistentListener(submitButton.onClick, panel.TrySubmit);

            panelRoot.SetActive(false);
            return panel;
        }

        static PausePanel BuildPausePanel(Transform parent)
        {
            var panelRoot = UIFactory.CreatePanelRoot(parent, "PausePanel", PanelBg);
            BuildPanelLayout(panelRoot, "Pause", out _, out var middle, out var bottomBar);

            var vlayout = UIFactory.AddVerticalLayout(middle.gameObject, 20);
            vlayout.childAlignment = TextAnchor.MiddleCenter;

            UIFactory.CreateText(middle, "Vous pouvez faire une pause avant de commencer le second niveau.", 22,
                TextAlignmentOptions.Center);
            var timerText = UIFactory.CreateText(middle, string.Empty, 30, TextAlignmentOptions.Center);

            var continueButton = UIFactory.CreateButton(bottomBar, "Continuer", out _);
            SetSize(continueButton, 300, 65);

            var panel = panelRoot.AddComponent<PausePanel>();
            panel.timerText = timerText;
            panel.continueButton = continueButton;

            UnityEventTools.AddPersistentListener(continueButton.onClick, panel.OnContinueClicked);

            panelRoot.SetActive(false);
            return panel;
        }

        static EndScreenPanel BuildEndScreenPanel(Transform parent)
        {
            var panelRoot = UIFactory.CreatePanelRoot(parent, "EndScreenPanel", PanelBg);
            BuildPanelLayout(panelRoot, "Fin de session", out _, out var middle, out var bottomBar);

            var vlayout = UIFactory.AddVerticalLayout(middle.gameObject, 20);
            vlayout.childAlignment = TextAnchor.MiddleCenter;

            var message = UIFactory.CreateText(middle, string.Empty, 26, TextAlignmentOptions.Center);
            var pathText = UIFactory.CreateText(middle, string.Empty, 16, TextAlignmentOptions.Center);

            var newSessionButton = UIFactory.CreateButton(bottomBar, "Nouvelle session", out _);
            SetSize(newSessionButton, 320, 60);
            var quitButton = UIFactory.CreateButton(bottomBar, "Quitter", out _);
            SetSize(quitButton, 220, 60);

            var panel = panelRoot.AddComponent<EndScreenPanel>();
            panel.messageText = message;
            panel.pathText = pathText;
            panel.newSessionButton = newSessionButton;
            panel.quitButton = quitButton;

            UnityEventTools.AddPersistentListener(newSessionButton.onClick, panel.OnNewSessionClicked);
            UnityEventTools.AddPersistentListener(quitButton.onClick, panel.Quit);

            panelRoot.SetActive(false);
            return panel;
        }

        // ---------------------------------------------------------------
        // Build settings
        // ---------------------------------------------------------------

        static void UpdateBuildSettings()
        {
            // Start.unity is the actual entry point now (the existing 3D Game Kit title scene, with
            // our overlay on top), so it must be index 0. Interstitial and the four level scenes
            // follow; anything else already present (e.g. _TemplateScene, Loading) is kept afterwards.
            var desiredOrder = new List<string>
            {
                StartScenePath,
                InterstitialScenePath,
                Level1Path,
                Level1StaticPath,
                Level2Path,
                Level2StaticPath,
            };

            var newList = new List<EditorBuildSettingsScene>();
            var seenPaths = new HashSet<string>();

            foreach (var path in desiredOrder)
            {
                newList.Add(new EditorBuildSettingsScene(path, true));
                seenPaths.Add(path);
            }

            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s.path == ExperimentScenePath) continue; // obsolete bootstrap scene, dropped
                if (seenPaths.Add(s.path))
                {
                    newList.Add(s);
                }
            }

            EditorBuildSettings.scenes = newList.ToArray();
        }
    }
}
