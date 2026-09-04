using UnityEngine;

namespace Loris.AudioEngine
{
    /// <summary>
    /// Composant Unity qui héberge le moteur audio.
    /// </summary>
    [AddComponentMenu("SpaceMusicEngine/Audio Engine")]
    public class AudioEngineBehaviour : MonoBehaviour
    {
        [Header("Project")]
        public MusicProject musicProject;

        [Header("Runtime root")]
        public Transform runtimeRoot;

        private AudioEngineFacade engine;

        public AudioEngineFacade Engine => engine;

        /// <summary>
        /// Initialise la façade tôt dans le cycle Unity.
        /// </summary>
        private void Awake()
        {
            runtimeRoot ??= this.transform;

            if (musicProject == null)
            {
                Debug.LogError("AudioEngineBehaviour: musicProject is null.");
                return;
            }

            engine = new AudioEngineFacade(musicProject, runtimeRoot);
            engine.PreloadAllClips();
        }

        /// <summary>
        /// Remplace le projet chargé à chaud : arrêt complet (les voix en cours sont
        /// détruites), puis reconstruction de la façade sur le nouveau projet.
        /// Rien ne joue tant qu'un espace n'a pas été demandé.
        /// </summary>
        public void LoadProject(MusicProject project)
        {
            if (project == null) return;

            engine?.StopPlayback();

            musicProject = project;
            runtimeRoot ??= this.transform;

            engine = new AudioEngineFacade(project, runtimeRoot);
            engine.PreloadAllClips();
        }

        /// <summary>
        /// Tick du moteur à chaque frame.
        /// </summary>
        private void Update()
        {
            engine?.Tick();
        }
    }
}
