using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Gamekit3D.EditorTools
{
    // The 4 level scenes are authored with Realtime GI (Enlighten) on and Baked GI off. Realtime
    // Enlighten GI is deprecated and effectively non-functional in Player builds on modern Unity —
    // the Editor can still show a live approximation while the scene is open, which is why the
    // resulting broken shadows / unlit black zones only show up once built. Switching to Baked GI
    // and baking fixes it.
    public static class LightingRebake
    {
        static readonly string[] ScenePaths =
        {
            "Assets/3DGamekit/Scenes/GamePlay/Level1.unity",
            "Assets/3DGamekit/Scenes/GamePlay/Level1_Static.unity",
            "Assets/3DGamekit/Scenes/GamePlay/Level2.unity",
            "Assets/3DGamekit/Scenes/GamePlay/Level2_Static.unity",
        };

        [MenuItem("Tools/SpaceMusicEngine Demo/Rebake Level Lighting")]
        public static void RebakeAll()
        {
            foreach (var path in ScenePaths)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

                var settings = Lightmapping.lightingSettings;
                settings.bakedGI = true;
                settings.realtimeGI = false;

                Debug.Log("LightingRebake: baking " + path + " ...");
                bool ok = Lightmapping.Bake();
                if (!ok)
                {
                    Debug.LogError("LightingRebake: Lightmapping.Bake() failed for " + path);
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("LightingRebake: done with " + path);
            }

            Debug.Log("LightingRebake: terminé avec succès.");
        }
    }
}
