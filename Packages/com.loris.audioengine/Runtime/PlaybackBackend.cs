using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Loris.AudioEngine
{
    /// <summary>
    /// Backend de lecture audio.
    ///
    /// Il gère :
    /// - l'application d'un état cible,
    /// - les couches nouvelles / existantes / à retirer,
    /// - le scheduling des boucles,
    /// - les fades d'entrée et de sortie.
    /// </summary>
    public class PlaybackBackend
    {
        private readonly Transform root;
        private readonly MusicClock clock;
        private readonly MusicProject project;

        private readonly Dictionary<string, MusicVoice> activeVoices = new();
        private readonly List<string> keysToRemove = new();

        // Voix one-shot des pistes de transition (hors résolution des couches d'espace).
        private readonly List<MusicVoice> transitionVoices = new();

        public PlaybackBackend(Transform rootTransform, MusicClock musicClock, MusicProject musicProject)
        {
            root = rootTransform;
            clock = musicClock;
            project = musicProject;
        }

        /// <summary>
        /// Applique un nouvel état cible.
        ///
        /// Règles :
        /// - une couche absente du nouvel état => fade out ;
        /// - une couche déjà présente => update volume ;
        /// - une nouvelle couche => création + fade in.
        /// </summary>
        public void ApplyState(List<TargetLayer> targetLayers, double dspStartTime)
        {
            Dictionary<string, TargetLayer> targetByKey = new();
            foreach (var layer in targetLayers)
                targetByKey[layer.key] = layer;

            List<string> existingKeys = new(activeVoices.Keys);

            // 1. Couches à retirer : fade out au lieu de destruction immédiate.
            foreach (string key in existingKeys)
            {
                if (!targetByKey.ContainsKey(key))
                {
                    MusicVoice voice = activeVoices[key];

                    // On évite de redéclencher le fade si déjà en cours.
                    if (!voice.IsFadingOut)
                    {
                        voice.BeginFadeOut(project.defaultFadeOutSeconds);
                    }
                }
            }

            double projectLoopDuration = clock.GetLoopDurationSeconds();
            double startOffsetSeconds = clock.GetLoopPositionSecondsAtDspTime(dspStartTime);

            // 2. Couches à créer ou mettre à jour.
            foreach (TargetLayer target in targetLayers)
            {
                if (target.clip == null)
                    continue;

                if (activeVoices.TryGetValue(target.key, out MusicVoice existingVoice))
                {
                    existingVoice.SetVolume(target.volume);
                    continue;
                }

                GameObject go = new GameObject($"Layer_{target.key.Replace('/', '_')}");
                go.transform.SetParent(root);

                MusicVoice voice = go.AddComponent<MusicVoice>();
                voice.Initialize(target.key, project.ResolveOutput(target.stateId));

                // Envelope layers drive their own volume each frame, so they start
                // without a project fade-in (the envelope handles the ramp).
                float fadeIn = target.continuousVolume ? 0f : project.defaultFadeInSeconds;

                voice.StartLoop(
                    target.key,
                    target.clip,
                    target.volume,
                    dspStartTime,
                    projectLoopDuration,
                    startOffsetSeconds,
                    fadeIn,
                    target.loopTailSeconds
                );

                activeVoices.Add(target.key, voice);
            }
        }

        /// <summary>
        /// Fade out de toutes les couches d'un espace (sortie de source, mode FadeOut).
        /// </summary>
        public void FadeOutSpace(string spaceId, float fadeSeconds)
        {
            if (string.IsNullOrEmpty(spaceId)) return;
            string prefix = spaceId + "/";

            foreach (var kv in activeVoices)
            {
                if (!kv.Key.StartsWith(prefix)) continue;
                if (!kv.Value.IsFadingOut)
                    kv.Value.BeginFadeOut(fadeSeconds);
            }
        }

        /// <summary>
        /// Arrêt dur planifié (cut) de toutes les couches d'un espace à un temps DSP donné.
        /// </summary>
        public void HardStopSpace(string spaceId, double atDspTime)
        {
            if (string.IsNullOrEmpty(spaceId)) return;
            string prefix = spaceId + "/";

            foreach (var kv in activeVoices)
            {
                if (!kv.Key.StartsWith(prefix)) continue;
                kv.Value.ScheduleHardStop(atDspTime);
            }
        }

        /// <summary>
        /// Lance la piste de transition (one-shot) : clip joué une fois depuis un offset,
        /// pour une durée donnée, géré hors des couches d'espace.
        /// </summary>
        public void StartTransitionTrack(
            AudioClip clip,
            double startDspTime,
            double clipOffsetSeconds,
            double durationSeconds,
            float fadeInSeconds,
            AudioMixerGroup output)
        {
            if (clip == null) return;

            GameObject go = new GameObject("TransitionTrack");
            go.transform.SetParent(root);

            MusicVoice voice = go.AddComponent<MusicVoice>();
            voice.Initialize("Transition", output);
            voice.StartOneShot("__transition__", clip, 1f, startDspTime, clipOffsetSeconds, durationSeconds, fadeInSeconds);

            transitionVoices.Add(voice);
        }

        /// <summary>
        /// Tick runtime :
        /// - met à jour les fades,
        /// - programme les tours suivants,
        /// - promeut les segments planifiés,
        /// - nettoie les voix terminées.
        /// </summary>
        public void Tick()
        {
            double now = AudioSettings.dspTime;
            double lead = clock.GetSafeScheduleLeadTime();

            keysToRemove.Clear();

            foreach (var kv in activeVoices)
            {
                string key = kv.Key;
                MusicVoice voice = kv.Value;

                // 1. Tick fade
                voice.Tick();

                // 2. Scheduling audio
                if (voice.IsRunning)
                {
                    // Le tour suivant est programmé le plus tôt possible — dès que le
                    // précédent est promu, soit une boucle entière d'avance — et non
                    // juste avant le point de boucle. Sinon la chaîne dépend du fait
                    // que Tick() tourne dans la dernière seconde avant le raccord :
                    // un hoquet de frame plus long (GC, décodage, onglet en arrière-plan
                    // dans un navigateur) fait manquer le raccord, et le tour repart
                    // déphasé. Programmer d'avance rend le rebouclage insensible à
                    // tout hoquet plus court qu'une boucle.
                    double scheduleAhead = System.Math.Max(lead, voice.LoopDurationSeconds);
                    if (!voice.HasNextScheduled && now + scheduleAhead >= voice.CurrentEndDspTime)
                    {
                        voice.ScheduleNextLoop();
                    }

                    voice.PromoteScheduledLoopIfNeeded(now);
                }

                // 3. Marquage pour suppression
                if (voice.IsMarkedForRemoval)
                {
                    keysToRemove.Add(key);
                }
            }

            // 4. Nettoyage hors boucle
            foreach (string key in keysToRemove)
            {
                if (activeVoices.TryGetValue(key, out MusicVoice voice))
                {
                    Object.Destroy(voice.gameObject);
                    activeVoices.Remove(key);
                }
            }

            // 5. Voix de transition one-shot : tick + nettoyage.
            for (int i = transitionVoices.Count - 1; i >= 0; i--)
            {
                MusicVoice voice = transitionVoices[i];
                voice.Tick();

                if (voice.IsMarkedForRemoval)
                {
                    Object.Destroy(voice.gameObject);
                    transitionVoices.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Stop complet de toutes les voix.
        /// </summary>
        public void StopAll()
        {
            foreach (var kv in activeVoices)
            {
                kv.Value.StopAndClear();
                Object.Destroy(kv.Value.gameObject);
            }

            activeVoices.Clear();

            foreach (var voice in transitionVoices)
            {
                voice.StopAndClear();
                Object.Destroy(voice.gameObject);
            }

            transitionVoices.Clear();
        }

        /// <summary>
        /// Met à jour en direct le volume cible d'une couche active (voix conservée).
        /// Utilisé par les couches en mode VolumeEnvelope, pilotées chaque frame.
        /// </summary>
        public void SetLayerVolume(string key, float volume)
        {
            if (activeVoices.TryGetValue(key, out MusicVoice voice))
                voice.SetVolume(volume);
        }

        /// <summary>
        /// Position de lecture réelle d'une couche active, en secondes (-1 si absente).
        /// Diagnostic : elle doit suivre la position de boucle de l'horloge.
        /// </summary>
        public double GetLayerPlaybackPositionSeconds(string key)
            => activeVoices.TryGetValue(key, out MusicVoice voice)
                ? voice.GetPlaybackPositionSeconds()
                : -1.0;

        public int GetActiveVoiceCount() => activeVoices.Count;

        public IEnumerable<string> GetActiveLayerKeys()
        {
            foreach (var kv in activeVoices)
                yield return kv.Key;
        }
    }
}
