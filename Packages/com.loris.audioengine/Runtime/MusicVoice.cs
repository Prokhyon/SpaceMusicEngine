using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Loris.AudioEngine
{
    /// <summary>
    /// Représente une couche audio active.
    ///
    /// Cette classe gère :
    /// - la lecture planifiée sample-accurate ;
    /// - le rebouclage ;
    /// - les fades d'entrée et de sortie ;
    /// - le démarrage à un offset musical donné ;
    /// - la préparation du clip pour éviter un début silencieux.
    /// </summary>
    [AddComponentMenu("")]
    public class MusicVoice : MonoBehaviour
    {
        private AudioSource currentSource;
        private AudioSource nextSource;

        public string ActiveKey { get; private set; }
        public AudioClip CurrentClip { get; private set; }

        /// <summary>
        /// Volume musical cible de la couche (hors fade).
        /// </summary>
        public float TargetVolume { get; private set; }

        /// <summary>
        /// Temps DSP de début du segment courant.
        /// </summary>
        public double CurrentStartDspTime { get; private set; }

        /// <summary>
        /// Temps DSP de fin du segment courant.
        /// </summary>
        public double CurrentEndDspTime { get; private set; }

        /// <summary>
        /// Durée logique de boucle de l'espace projet.
        /// </summary>
        public double LoopDurationSeconds { get; private set; }

        public bool IsRunning { get; private set; }
        public bool HasNextScheduled { get; private set; }
        public double NextScheduledStartDspTime { get; private set; }

        // Position musicale à laquelle le tour programmé entre dans le fichier. Vaut 0 dans
        // le cas normal ; non nul seulement quand le raccord a été manqué et rattrapé.
        private double nextScheduledOffsetSeconds;

        // Instant où la source « next » cesse de sonner et redevient réutilisable. Après une
        // promotion avec tail, c'est l'ancienne source courante : elle joue encore sa
        // résonance par-dessus le nouveau tour et ne doit surtout pas être recyclée avant.
        private double nextSourceFreeDspTime;

        public bool IsFadingOut { get; private set; }
        public bool IsMarkedForRemoval { get; private set; }

        // Arrêt planifié (cut sample-accurate) : la voix ne reboucle plus au-delà
        // de ce temps et se marque pour suppression une fois atteint.
        public bool HasHardStop { get; private set; }
        public double HardStopDspTime { get; private set; }

        // Voix one-shot (piste de transition) : ne reboucle pas.
        public bool IsOneShot { get; private set; }

        // Résonance (secondes) jouée après le point de boucle : la source sortante
        // continue de sonner par-dessus la boucle suivante au lieu d'être coupée net.
        private double tailSeconds;

        // --- État des fades ---
        //
        // Les fades sont calés sur l'horloge DSP, pas sur le temps de jeu accumulé.
        // Une couche est créée jusqu'à GetSafeScheduleLeadTime() avant son départ
        // planifié : compter le fade dès la création le consommerait avant même que
        // le premier échantillon sorte (entrée sèche). L'horloge DSP est aussi la
        // seule qui reste juste si le jeu est mis en pause (Time.timeScale = 0).
        private bool isFadingIn;
        private float fadeInDuration;
        private double fadeInStartDspTime;

        private bool isFadingOut;
        private float fadeOutDuration;
        private double fadeOutStartDspTime;
        private float fadeOutStartVolume;

        /// <summary>
        /// Volume instantané de fade entre 0 et 1.
        /// Le volume final appliqué à l'AudioSource est :
        /// fadeFactor * TargetVolume
        /// </summary>
        private float fadeFactor = 1f;

        public void Initialize(string debugName, AudioMixerGroup output = null)
        {
            GameObject currentGO = new GameObject($"{debugName}_Current");
            currentGO.transform.SetParent(transform);
            currentSource = currentGO.AddComponent<AudioSource>();

            GameObject nextGO = new GameObject($"{debugName}_Next");
            nextGO.transform.SetParent(transform);
            nextSource = nextGO.AddComponent<AudioSource>();

            ConfigureSource(currentSource, output);
            ConfigureSource(nextSource, output);

            ResetState();
        }

        /// <summary>
        /// Configure une AudioSource.
        /// Si un AudioMixerGroup est fourni, la couche y est routée pour
        /// apparaître dans l'Audio Mixer Unity.
        /// </summary>
        private void ConfigureSource(AudioSource source, AudioMixerGroup output)
        {
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 1f;
            source.outputAudioMixerGroup = output;
        }

        /// <summary>
        /// Vérifie que le clip est prêt à être lu.
        ///
        /// Si les données audio ne sont pas encore chargées,
        /// on demande explicitement leur chargement.
        ///
        /// Remarque :
        /// la meilleure solution reste d'importer les boucles avec
        /// Preload Audio Data + Decompress On Load côté Unity. [web:58][web:302]
        /// </summary>
        private void EnsureClipIsLoaded(AudioClip clip)
        {
            if (clip == null)
                return;

            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                clip.LoadAudioData();
            }
        }

        /// <summary>
        /// Convertit un offset en secondes vers un index d'échantillon.
        ///
        /// Utiliser timeSamples est généralement plus précis que time
        /// pour les démarrages à offset.
        /// </summary>
        private int SecondsToSampleOffset(AudioClip clip, double seconds)
        {
            if (clip == null || clip.frequency <= 0 || clip.samples <= 0)
                return 0;

            int sample = Mathf.RoundToInt((float)(seconds * clip.frequency));
            return Mathf.Clamp(sample, 0, Mathf.Max(0, clip.samples - 1));
        }

        /// <summary>
        /// Démarre la couche.
        ///
        /// - startDspTime : temps absolu de départ
        /// - loopDurationSeconds : durée de boucle projet
        /// - startOffsetSeconds : position musicale à laquelle on entre
        /// - fadeInSeconds : durée du fade d'entrée
        /// </summary>
        public void StartLoop(
            string key,
            AudioClip clip,
            float targetVolume,
            double startDspTime,
            double loopDurationSeconds,
            double startOffsetSeconds,
            float fadeInSeconds,
            double loopTailSeconds = 0.0)
        {
            if (clip == null)
                return;

            EnsureClipIsLoaded(clip);

            ActiveKey = key;
            CurrentClip = clip;
            TargetVolume = Mathf.Max(0f, targetVolume);
            LoopDurationSeconds = loopDurationSeconds;

            // Le tail chevauche la boucle suivante ; on le borne à la longueur de boucle
            // pour qu'une source ne soit jamais réutilisée alors qu'elle sonne encore.
            tailSeconds = Mathf.Clamp((float)loopTailSeconds, 0f, (float)loopDurationSeconds);

            double normalizedOffset = 0.0;
            if (LoopDurationSeconds > 0.0)
            {
                normalizedOffset = startOffsetSeconds % LoopDurationSeconds;
                if (normalizedOffset < 0.0)
                    normalizedOffset += LoopDurationSeconds;
            }

            int startSample = SecondsToSampleOffset(clip, normalizedOffset);

            CurrentStartDspTime = startDspTime;
            CurrentEndDspTime = CurrentStartDspTime + (LoopDurationSeconds - normalizedOffset);

            currentSource.Stop();
            currentSource.clip = clip;

            // Important :
            // on positionne le curseur dans le clip AVANT de programmer la lecture.
            currentSource.timeSamples = startSample;

            currentSource.PlayScheduled(CurrentStartDspTime);
            // La source joue jusqu'au point de boucle + tail (résonance de fin de fichier).
            currentSource.SetScheduledEndTime(CurrentEndDspTime + tailSeconds);

            if (fadeInSeconds > 0f)
            {
                fadeFactor = 0f;
                isFadingIn = true;
                fadeInDuration = fadeInSeconds;
                // Le fade démarre au premier échantillon audible, pas à la création.
                fadeInStartDspTime = CurrentStartDspTime;
            }
            else
            {
                fadeFactor = 1f;
                isFadingIn = false;
                fadeInDuration = 0f;
                fadeInStartDspTime = 0.0;
            }

            isFadingOut = false;
            IsFadingOut = false;
            IsMarkedForRemoval = false;
            fadeOutDuration = 0f;
            fadeOutStartDspTime = 0.0;
            fadeOutStartVolume = 0f;

            ApplyEffectiveVolume();

            HasNextScheduled = false;
            NextScheduledStartDspTime = 0.0;
            nextSourceFreeDspTime = 0.0;
            IsRunning = true;
        }

        /// <summary>
        /// Démarre une lecture one-shot (piste de transition) :
        /// joue le clip depuis un offset donné, pour une durée donnée, sans rebouclage.
        /// La voix se marque pour suppression à la fin.
        /// </summary>
        public void StartOneShot(
            string key,
            AudioClip clip,
            float targetVolume,
            double startDspTime,
            double clipOffsetSeconds,
            double durationSeconds,
            float fadeInSeconds)
        {
            if (clip == null)
                return;

            EnsureClipIsLoaded(clip);

            ActiveKey = key;
            CurrentClip = clip;
            TargetVolume = Mathf.Max(0f, targetVolume);
            LoopDurationSeconds = 0.0;
            IsOneShot = true;

            int startSample = SecondsToSampleOffset(clip, Math.Max(0.0, clipOffsetSeconds));

            CurrentStartDspTime = startDspTime;
            CurrentEndDspTime = startDspTime + Math.Max(0.0, durationSeconds);

            currentSource.Stop();
            currentSource.clip = clip;
            currentSource.timeSamples = startSample;
            currentSource.PlayScheduled(CurrentStartDspTime);
            currentSource.SetScheduledEndTime(CurrentEndDspTime);

            if (fadeInSeconds > 0f)
            {
                fadeFactor = 0f;
                isFadingIn = true;
                fadeInDuration = fadeInSeconds;
                fadeInStartDspTime = CurrentStartDspTime;
            }
            else
            {
                fadeFactor = 1f;
                isFadingIn = false;
            }

            isFadingOut = false;
            IsFadingOut = false;
            IsMarkedForRemoval = false;

            // La fin de piste sert d'arrêt dur : on ne reboucle jamais.
            HasHardStop = true;
            HardStopDspTime = CurrentEndDspTime;

            HasNextScheduled = false;
            NextScheduledStartDspTime = 0.0;

            ApplyEffectiveVolume();
            IsRunning = true;
        }

        /// <summary>
        /// Planifie un arrêt dur (cut) à un temps DSP donné, sample-accurate.
        /// La voix ne reboucle plus au-delà et se marque pour suppression à ce temps.
        /// </summary>
        public void ScheduleHardStop(double dspTime)
        {
            if (!IsRunning)
            {
                IsMarkedForRemoval = true;
                return;
            }

            HasHardStop = true;
            HardStopDspTime = dspTime;

            if (currentSource != null)
                currentSource.SetScheduledEndTime(dspTime);

            // Annule un rebouclage déjà programmé qui démarrerait après l'arrêt.
            if (HasNextScheduled && nextSource != null)
            {
                if (NextScheduledStartDspTime >= dspTime)
                {
                    nextSource.Stop();
                    nextSource.clip = null;
                    HasNextScheduled = false;
                    NextScheduledStartDspTime = 0.0;
                }
                else
                {
                    nextSource.SetScheduledEndTime(dspTime);
                }
            }
        }

        /// <summary>
        /// Programme le rebouclage complet depuis le début du clip.
        ///
        /// Comme la boucle projet repart de 0, la source suivante repart
        /// au sample 0 du clip.
        /// </summary>
        public void ScheduleNextLoop()
        {
            if (!IsRunning || CurrentClip == null || HasNextScheduled || HasHardStop || IsOneShot)
                return;

            // Programmer le tour suivant recycle la source « next » : tant qu'elle joue la
            // résonance du tour précédent, la réutiliser la couperait net. On repousse donc
            // la programmation de la durée du tail — ce qui laisse encore presque une boucle
            // entière d'avance.
            if (AudioSettings.dspTime < nextSourceFreeDspTime)
                return;

            EnsureClipIsLoaded(CurrentClip);

            double nextStart = CurrentEndDspTime;

            // Rattrapage de phase : si le point de boucle est déjà passé (hoquet de frame
            // plus long que l'avance de programmation), démarrer au sample 0 ferait repartir
            // la couche déphasée du reste. On entre alors dans le fichier à la position
            // musicale réellement atteinte, pour rester calé sur la grille.
            double minimumStart = AudioSettings.dspTime + MusicClock.GetStartupLeadTime();
            nextScheduledOffsetSeconds = 0.0;

            if (nextStart < minimumStart)
            {
                double late = minimumStart - nextStart;

                // Un retard de plus d'une boucle saute simplement les tours entiers manqués.
                if (LoopDurationSeconds > 0.0)
                    late %= LoopDurationSeconds;

                nextScheduledOffsetSeconds = late;
                nextStart = minimumStart;
            }

            double remaining = LoopDurationSeconds - nextScheduledOffsetSeconds;
            int startSample = SecondsToSampleOffset(CurrentClip, nextScheduledOffsetSeconds);

            nextSource.Stop();
            nextSource.clip = CurrentClip;
            nextSource.timeSamples = startSample;
            nextSource.PlayScheduled(nextStart);
            // Chaque itération joue le corps + le tail : la résonance chevauche la boucle suivante.
            nextSource.SetScheduledEndTime(nextStart + remaining + tailSeconds);

            nextSource.volume = GetEffectiveVolume();

            NextScheduledStartDspTime = nextStart;
            HasNextScheduled = true;
        }

        /// <summary>
        /// Promeut la source "next" en source courante
        /// quand son départ planifié a été atteint.
        /// </summary>
        public void PromoteScheduledLoopIfNeeded(double dspTime)
        {
            if (!IsRunning || !HasNextScheduled)
                return;

            if (dspTime < NextScheduledStartDspTime)
                return;

            // Avec un tail, on NE coupe PAS la source sortante : elle continue de sonner
            // (résonance) jusqu'à sa fin planifiée (point de boucle + tail), par-dessus la
            // boucle suivante. Sans tail, on la coupe net comme avant.
            if (tailSeconds <= 0.0)
            {
                currentSource.Stop();
                currentSource.clip = null;
                nextSourceFreeDspTime = 0.0;
            }
            else
            {
                // Elle devient la source « next » : indisponible jusqu'à la fin de sa résonance.
                nextSourceFreeDspTime = NextScheduledStartDspTime + tailSeconds;
            }

            AudioSource temp = currentSource;
            currentSource = nextSource;
            nextSource = temp;

            // Départ « virtuel » du tour : si l'entrée s'est faite en cours de boucle après
            // un rattrapage, la grille reste celle du raccord théorique, pas celle du départ
            // réel — sinon le décalage se reporterait sur tous les tours suivants.
            CurrentStartDspTime = NextScheduledStartDspTime - nextScheduledOffsetSeconds;
            CurrentEndDspTime = CurrentStartDspTime + LoopDurationSeconds;

            HasNextScheduled = false;
            NextScheduledStartDspTime = 0.0;
            nextScheduledOffsetSeconds = 0.0;

            ApplyEffectiveVolume();
        }

        /// <summary>
        /// Met à jour le volume cible de la couche.
        /// </summary>
        public void SetVolume(float volume)
        {
            TargetVolume = Mathf.Max(0f, volume);
            ApplyEffectiveVolume();
        }

        /// <summary>
        /// Lance un fade out de sortie.
        /// </summary>
        public void BeginFadeOut(float durationSeconds)
        {
            if (!IsRunning)
            {
                IsMarkedForRemoval = true;
                return;
            }

            if (durationSeconds <= 0f)
            {
                fadeFactor = 0f;
                ApplyEffectiveVolume();
                StopAndClear();
                IsMarkedForRemoval = true;
                return;
            }

            isFadingIn = false;

            isFadingOut = true;
            IsFadingOut = true;
            fadeOutDuration = durationSeconds;
            fadeOutStartDspTime = AudioSettings.dspTime;
            fadeOutStartVolume = fadeFactor;
        }

        /// <summary>
        /// Tick interne de la voix.
        /// Gère les fades d'entrée et de sortie, sur l'horloge DSP.
        /// </summary>
        public void Tick()
        {
            if (!IsRunning)
                return;

            // Arrêt dur planifié (cut / fin de piste) : on coupe une fois atteint.
            if (HasHardStop && AudioSettings.dspTime >= HardStopDspTime)
            {
                StopAndClear();
                IsMarkedForRemoval = true;
                return;
            }

            double now = AudioSettings.dspTime;

            if (isFadingIn)
            {
                // Tant que le départ planifié n'est pas atteint, la couche reste muette :
                // le fade ne doit pas s'écouler pendant le lead de programmation.
                double elapsed = now - fadeInStartDspTime;
                float t = elapsed <= 0.0
                    ? 0f
                    : (fadeInDuration > 0f ? Mathf.Clamp01((float)(elapsed / fadeInDuration)) : 1f);
                fadeFactor = t;

                if (t >= 1f)
                {
                    fadeFactor = 1f;
                    isFadingIn = false;
                }

                ApplyEffectiveVolume();
            }

            if (isFadingOut)
            {
                double elapsed = now - fadeOutStartDspTime;
                float t = fadeOutDuration > 0f ? Mathf.Clamp01((float)(elapsed / fadeOutDuration)) : 1f;
                fadeFactor = Mathf.Lerp(fadeOutStartVolume, 0f, t);

                ApplyEffectiveVolume();

                if (t >= 1f)
                {
                    fadeFactor = 0f;
                    ApplyEffectiveVolume();

                    isFadingOut = false;
                    IsFadingOut = false;

                    StopAndClear();
                    IsMarkedForRemoval = true;
                }
            }
        }

        /// <summary>
        /// Position de lecture réelle de la source courante, en secondes (-1 si rien ne joue).
        ///
        /// Sert au diagnostic : une couche calée doit toujours lire à la position de boucle
        /// de l'horloge. Tout écart mesure directement la désynchronisation.
        /// </summary>
        public double GetPlaybackPositionSeconds()
        {
            if (currentSource == null || currentSource.clip == null || !currentSource.isPlaying)
                return -1.0;

            int frequency = currentSource.clip.frequency;
            if (frequency <= 0)
                return -1.0;

            return (double)currentSource.timeSamples / frequency;
        }

        /// <summary>
        /// Arrête complètement la voix et vide les clips.
        /// </summary>
        public void StopAndClear()
        {
            if (currentSource != null)
            {
                currentSource.Stop();
                currentSource.clip = null;
            }

            if (nextSource != null)
            {
                nextSource.Stop();
                nextSource.clip = null;
            }

            ResetState();
        }

        /// <summary>
        /// Calcule le volume réellement appliqué à la couche.
        /// </summary>
        private float GetEffectiveVolume()
        {
            return TargetVolume * Mathf.Clamp01(fadeFactor);
        }

        /// <summary>
        /// Applique le volume effectif aux deux AudioSources.
        /// </summary>
        private void ApplyEffectiveVolume()
        {
            float effectiveVolume = GetEffectiveVolume();

            if (currentSource != null)
                currentSource.volume = effectiveVolume;

            if (nextSource != null)
                nextSource.volume = effectiveVolume;
        }

        /// <summary>
        /// Réinitialise l'état interne.
        /// </summary>
        private void ResetState()
        {
            ActiveKey = null;
            CurrentClip = null;
            TargetVolume = 1f;
            CurrentStartDspTime = 0.0;
            CurrentEndDspTime = 0.0;
            LoopDurationSeconds = 0.0;

            IsRunning = false;
            HasNextScheduled = false;
            NextScheduledStartDspTime = 0.0;
            nextScheduledOffsetSeconds = 0.0;
            nextSourceFreeDspTime = 0.0;

            IsFadingOut = false;
            IsMarkedForRemoval = false;

            HasHardStop = false;
            HardStopDspTime = 0.0;
            IsOneShot = false;
            tailSeconds = 0.0;

            isFadingIn = false;
            fadeInDuration = 0f;
            fadeInStartDspTime = 0.0;

            isFadingOut = false;
            fadeOutDuration = 0f;
            fadeOutStartDspTime = 0.0;
            fadeOutStartVolume = 0f;

            fadeFactor = 1f;
        }
    }
}
