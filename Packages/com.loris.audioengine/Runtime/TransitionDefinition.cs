using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Loris.AudioEngine
{
    /// <summary>
    /// Position métrique 1-based (mesure, temps) dans une grille donnée.
    /// La grille de référence (BPM, beatsPerBar) dépend du contexte :
    /// - source pour les départs,
    /// - transition pour trackStart / keepSourceUntil,
    /// - cible pour l'arrivée.
    /// </summary>
    [Serializable]
    public struct MetricPosition
    {
        [Min(1)] public int bar;
        [Min(1)] public int beat;

        public MetricPosition(int bar, int beat)
        {
            this.bar = bar;
            this.beat = beat;
        }

        public static MetricPosition One => new MetricPosition(1, 1);

        /// <summary>
        /// Convertit la position en offset secondes depuis le début de la grille,
        /// pour un transport donné. bar/beat sont 1-based.
        /// </summary>
        public double ToSeconds(float bpm, int beatsPerBar)
        {
            double secondsPerBeat = 60.0 / Mathf.Max(1f, bpm);
            int barIndex = Mathf.Max(1, bar) - 1;
            int beatIndex = Mathf.Max(1, beat) - 1;
            int bpbSafe = Mathf.Max(1, beatsPerBar);
            return (barIndex * bpbSafe + beatIndex) * secondsPerBeat;
        }
    }

    /// <summary>
    /// Intervalle métrique utilisé par le déclenchement par fréquence
    /// (ex. toutes les 1 mesure, ou tous les 2 temps).
    /// </summary>
    [Serializable]
    public struct MetricInterval
    {
        [Min(0)] public int bars;
        [Min(0)] public int beats;

        public MetricInterval(int bars, int beats)
        {
            this.bars = bars;
            this.beats = beats;
        }

        public double ToSeconds(float bpm, int beatsPerBar)
        {
            double secondsPerBeat = 60.0 / Mathf.Max(1f, bpm);
            int bpbSafe = Mathf.Max(1, beatsPerBar);
            double totalBeats = bars * (double)bpbSafe + beats;
            return totalBeats * secondsPerBeat;
        }
    }

    /// <summary>Sortie de l'espace source au point de départ.</summary>
    public enum SourceExit
    {
        FadeOut,
        Cut
    }

    /// <summary>Où sont définis les points de départ de la transition.</summary>
    public enum TriggerMode
    {
        /// <summary>Liste explicite de variantes, chacune avec ses départs.</summary>
        Variants,

        /// <summary>Déclenchement sur une grille régulière (frequency).</summary>
        Frequency
    }

    /// <summary>Grille de tempo utilisée pour la durée du tail de la piste de transition.</summary>
    public enum TailTempo
    {
        /// <summary>La durée du tail est mesurée sur le tempo de la transition.</summary>
        Transition,

        /// <summary>La durée du tail est mesurée sur le tempo du nouvel espace (cible).</summary>
        TargetSpace
    }

    /// <summary>Comment est résolue la position d'arrivée dans la cible.</summary>
    public enum ArrivalMode
    {
        /// <summary>Chaque variante définit son arrivée.</summary>
        PerVariant,

        /// <summary>La cible reprend la position de la source (modulo sa boucle) + resumeOffsetBars.</summary>
        KeepSourcePosition,

        /// <summary>La cible démarre toujours à fixedArrival (défaut 1.1).</summary>
        Fixed
    }

    /// <summary>
    /// Une variante de transition : un ou plusieurs points de départ (grille source)
    /// partageant le même offset de lecture de piste et la même arrivée.
    /// </summary>
    [Serializable]
    public class TransitionVariant
    {
        [Tooltip("Points de départ dans la boucle SOURCE (mesure.temps). Plusieurs départs partagent le même trackStart / arrival.")]
        public List<MetricPosition> departures = new List<MetricPosition> { MetricPosition.One };

        [Tooltip("Offset de lecture dans la piste de transition (grille TRANSITION). Ignoré si la transition est directe.")]
        public MetricPosition trackStart = MetricPosition.One;

        [Tooltip("Point d'arrivée dans la boucle CIBLE (mesure.temps). Utilisé uniquement si ArrivalMode == PerVariant.")]
        public MetricPosition arrival = MetricPosition.One;
    }

    /// <summary>
    /// Décrit comment passer d'un espace source vers un espace cible
    /// (ou depuis/vers le silence). Peut être directe ou jouer une piste de transition.
    /// </summary>
    [Serializable]
    public class TransitionDefinition
    {
        public string transitionId;

        [Header("Routing")]
        [Tooltip("spaceId source. Vide = depuis le silence.")]
        public string fromSpaceId;

        [Tooltip("spaceId cible. Vide = vers le silence.")]
        public string toSpaceId;

        [Header("Track")]
        [Tooltip("Si false, la transition est directe (pas de clip).")]
        public bool hasTrack = false;

        public AudioClip clip;

        [Tooltip("Optionnel. Groupe de mixer pour la piste de CETTE transition. " +
                 "Vide = output de l'espace cible.")]
        public AudioMixerGroup output;

        [Tooltip("Transport propre à la piste de transition.")]
        public float bpm = 120f;
        public int beatsPerBar = 4;

        [Tooltip("Longueur / fin de la piste en mesures. Définit la plage totale du fichier.")]
        [Min(1)] public int lengthBars = 1;

        [Header("Transition length (durée effective, optionnelle)")]
        [Tooltip("Si activé, la transition dure 'Transition Length' à partir du point de lecture " +
                 "(trackStart), au lieu d'aller jusqu'à la fin du fichier (lengthBars). La fin se " +
                 "décale donc selon le trackStart : plage de 4 mesures, longueur de 1 → fin en " +
                 "mesure 2 si débutée en 1, mesure 3 si débutée en 2, etc.")]
        public bool useCustomTransitionLength = false;

        [Tooltip("Durée effective de la transition (grille de la transition). Bornée à la fin du fichier.")]
        public MetricInterval transitionLength = new MetricInterval(1, 0);

        [Header("Source exit")]
        public SourceExit sourceExit = SourceExit.FadeOut;

        [Tooltip("Garder l'audio source pendant la transition (couche additionnelle sous la piste).")]
        public bool keepSourcePlaying = false;

        [Tooltip("Si true, la source continue jusqu'à la fin de la piste (lengthBars). Sinon, jusqu'à keepSourceUntil.")]
        public bool keepSourceUntilEndOfTrack = true;

        [Tooltip("Jusqu'où la source continue, dans la grille TRANSITION. Ignoré si keepSourceUntilEndOfTrack == true.")]
        public MetricPosition keepSourceUntil = MetricPosition.One;

        [Header("Trigger (départ)")]
        public TriggerMode triggerMode = TriggerMode.Variants;

        public List<TransitionVariant> variants = new List<TransitionVariant>();

        [Tooltip("Fréquence de déclenchement si TriggerMode == Frequency.")]
        public MetricInterval frequency = new MetricInterval(1, 0);

        [Tooltip("Offset de lecture de piste si TriggerMode == Frequency (grille TRANSITION).")]
        public MetricPosition defaultTrackStart = MetricPosition.One;

        [Header("Arrival (cible)")]
        public ArrivalMode arrivalMode = ArrivalMode.PerVariant;

        [Tooltip("Décalage de reprise en mesures si ArrivalMode == KeepSourcePosition.")]
        public int resumeOffsetBars = 0;

        [Tooltip("Position d'arrivée fixe si ArrivalMode == Fixed (défaut 1.1).")]
        public MetricPosition fixedArrival = MetricPosition.One;

        [Header("Tail (résonance après l'entrée cible)")]
        [Tooltip("Continue à lire la piste après l'entrée du nouvel espace, pour laisser " +
                 "sonner les résonances / la reverb du fichier. N'affecte pas le moment " +
                 "d'entrée de l'espace cible, seulement la fin réelle de la piste.")]
        public bool playTail = false;

        [Tooltip("Durée supplémentaire de lecture de la piste après l'entrée cible.")]
        public MetricInterval tailLength = new MetricInterval(0, 0);

        [Tooltip("Tempo servant à mesurer la durée du tail : celui de la transition ou du nouvel espace.")]
        public TailTempo tailTempo = TailTempo.Transition;

        public bool FromSilence => string.IsNullOrEmpty(fromSpaceId);
        public bool ToSilence => string.IsNullOrEmpty(toSpaceId);
    }
}
