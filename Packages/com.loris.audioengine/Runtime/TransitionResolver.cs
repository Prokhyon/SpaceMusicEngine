using System;
using UnityEngine;

namespace Loris.AudioEngine
{
    /// <summary>
    /// Plan de transition résolu : tout ce dont le runtime a besoin pour
    /// programmer la transition de façon sample-accurate.
    /// Tous les temps sont des temps DSP absolus, tous les offsets en secondes.
    /// </summary>
    public struct TransitionPlan
    {
        public bool valid;

        /// <summary>Temps DSP où la source part / la transition commence.</summary>
        public double departureDspTime;

        // --- Piste ---
        public bool hasTrack;
        public AudioClip clip;
        /// <summary>Offset de lecture dans le clip (secondes).</summary>
        public double trackStartOffsetSeconds;

        /// <summary>Temps DSP d'entrée de la cible (= fin de piste, ou = départ si directe).</summary>
        public double targetEntryDspTime;

        /// <summary>
        /// Temps DSP où la piste s'arrête réellement. Égal à targetEntryDspTime sauf si
        /// un tail est actif : la piste continue alors de sonner (résonances) au-delà de
        /// l'entrée de l'espace cible, jusqu'à ce temps.
        /// </summary>
        public double trackEndDspTime;

        // --- Cible ---
        public bool toSilence;
        /// <summary>Offset (secondes) dans la boucle cible où l'espace cible démarre.</summary>
        public double targetArrivalOffsetSeconds;

        // --- Source ---
        public SourceExit sourceExit;
        public bool keepSourcePlaying;
        /// <summary>Temps DSP où la source maintenue s'arrête (== départ si non maintenue).</summary>
        public double sourceStopDspTime;

        public static TransitionPlan Invalid => new TransitionPlan { valid = false };
    }

    /// <summary>
    /// Résout une TransitionDefinition en un TransitionPlan concret, à partir de
    /// la clock source courante et d'un temps de référence.
    ///
    /// Logique pure (pas d'effet de bord audio) — facile à tester.
    /// </summary>
    public static class TransitionResolver
    {
        public static TransitionPlan Resolve(
            TransitionDefinition transition,
            MusicSpaceDefinition fromSpace,
            MusicSpaceDefinition toSpace,
            MusicClock clock,
            double referenceDspTime)
        {
            if (transition == null)
                return TransitionPlan.Invalid;

            bool sourceRunning = clock != null && clock.IsRunning && fromSpace != null;

            // --- 1. Point de départ ---
            double departureDsp;
            TransitionVariant chosenVariant = null;
            MetricPosition chosenDeparture = MetricPosition.One;

            if (!sourceRunning)
            {
                // Depuis silence (intro) : on part immédiatement.
                departureDsp = referenceDspTime;
                if (transition.triggerMode == TriggerMode.Variants &&
                    transition.variants != null && transition.variants.Count > 0)
                {
                    chosenVariant = transition.variants[0];
                    if (chosenVariant.departures != null && chosenVariant.departures.Count > 0)
                        chosenDeparture = chosenVariant.departures[0];
                }
            }
            else if (transition.triggerMode == TriggerMode.Frequency)
            {
                double interval = transition.frequency.ToSeconds(fromSpace.bpm, fromSpace.beatsPerBar);
                departureDsp = clock.GetNextDspTimeForInterval(interval, referenceDspTime);
                chosenDeparture = clock.GetMetricPositionAtDspTime(departureDsp);
            }
            else // Variants : on prend le prochain départ à venir parmi toutes les variantes.
            {
                double best = double.PositiveInfinity;

                if (transition.variants != null)
                {
                    foreach (var variant in transition.variants)
                    {
                        if (variant?.departures == null) continue;
                        foreach (var dep in variant.departures)
                        {
                            double off = dep.ToSeconds(fromSpace.bpm, fromSpace.beatsPerBar);
                            double dsp = clock.GetNextDspTimeForLoopOffset(off, referenceDspTime);
                            if (dsp < best)
                            {
                                best = dsp;
                                chosenVariant = variant;
                                chosenDeparture = dep;
                            }
                        }
                    }
                }

                departureDsp = double.IsPositiveInfinity(best) ? referenceDspTime : best;
            }

            // --- 2. Piste / timing d'entrée cible ---
            bool hasTrack = transition.hasTrack && transition.clip != null;
            double trackStartOffset = 0.0;
            double targetEntryDsp = departureDsp;

            if (hasTrack)
            {
                MetricPosition ts = (transition.triggerMode == TriggerMode.Variants && chosenVariant != null)
                    ? chosenVariant.trackStart
                    : transition.defaultTrackStart;

                trackStartOffset = ts.ToSeconds(transition.bpm, transition.beatsPerBar);

                double secondsPerBarTransition = SecondsPerBar(transition.bpm, transition.beatsPerBar);
                double endOffset = Mathf.Max(1, transition.lengthBars) * secondsPerBarTransition;

                double trackDuration;
                if (transition.useCustomTransitionLength)
                {
                    // Durée fixe mesurée depuis le point de lecture : la fin se décale selon
                    // trackStart, bornée à la fin du fichier (reste au sein de la plage).
                    double customLength = transition.transitionLength.ToSeconds(transition.bpm, transition.beatsPerBar);
                    trackDuration = Math.Max(0.0, Math.Min(customLength, endOffset - trackStartOffset));
                }
                else
                {
                    // Historique : jusqu'à la fin du fichier (lengthBars).
                    trackDuration = Math.Max(0.0, endOffset - trackStartOffset);
                }

                targetEntryDsp = departureDsp + trackDuration;
            }

            // --- 3. Arrivée dans la cible ---
            bool toSilence = transition.ToSilence || toSpace == null;
            double arrivalOffset = 0.0;

            if (!toSilence)
            {
                MetricPosition arr = ResolveArrival(
                    transition, chosenVariant, chosenDeparture, toSpace, sourceRunning);
                arrivalOffset = WrapToTargetLoop(
                    arr.ToSeconds(toSpace.bpm, toSpace.beatsPerBar), toSpace);
            }

            // --- 4. Maintien de la source ---
            double sourceStopDsp = departureDsp;
            if (transition.keepSourcePlaying && hasTrack)
            {
                if (transition.keepSourceUntilEndOfTrack)
                {
                    sourceStopDsp = targetEntryDsp;
                }
                else
                {
                    double keepOffset = transition.keepSourceUntil.ToSeconds(transition.bpm, transition.beatsPerBar);
                    double dur = Math.Max(0.0, keepOffset - trackStartOffset);
                    sourceStopDsp = departureDsp + dur;
                }
            }

            // --- 5. Tail : prolonge la lecture de la piste au-delà de l'entrée cible
            //     (résonances / reverb du fichier), sans décaler l'entrée de la cible. ---
            double trackEndDsp = targetEntryDsp;
            if (hasTrack && transition.playTail && !toSilence)
            {
                float tailBpm;
                int tailBpb;
                if (transition.tailTempo == TailTempo.TargetSpace && toSpace != null)
                {
                    tailBpm = toSpace.bpm;
                    tailBpb = toSpace.beatsPerBar;
                }
                else
                {
                    tailBpm = transition.bpm;
                    tailBpb = transition.beatsPerBar;
                }
                trackEndDsp = targetEntryDsp + transition.tailLength.ToSeconds(tailBpm, tailBpb);
            }

            return new TransitionPlan
            {
                valid = true,
                departureDspTime = departureDsp,
                hasTrack = hasTrack,
                clip = hasTrack ? transition.clip : null,
                trackStartOffsetSeconds = trackStartOffset,
                targetEntryDspTime = targetEntryDsp,
                trackEndDspTime = trackEndDsp,
                toSilence = toSilence,
                targetArrivalOffsetSeconds = arrivalOffset,
                sourceExit = transition.sourceExit,
                keepSourcePlaying = transition.keepSourcePlaying && hasTrack,
                sourceStopDspTime = sourceStopDsp
            };
        }

        private static MetricPosition ResolveArrival(
            TransitionDefinition transition,
            TransitionVariant chosenVariant,
            MetricPosition chosenDeparture,
            MusicSpaceDefinition toSpace,
            bool sourceRunning)
        {
            switch (transition.arrivalMode)
            {
                case ArrivalMode.Fixed:
                    return transition.fixedArrival;

                case ArrivalMode.KeepSourcePosition:
                    if (!sourceRunning)
                        return transition.fixedArrival; // pas de source à reprendre
                    return MapKeepPosition(chosenDeparture, toSpace, transition.resumeOffsetBars);

                case ArrivalMode.PerVariant:
                default:
                    return chosenVariant != null ? chosenVariant.arrival : transition.fixedArrival;
            }
        }

        /// <summary>
        /// Reporte la position source sur la grille cible :
        /// - mesure : (barSource - 1 + resumeOffsetBars) modulo barsPerLoop cible ;
        /// - temps : conservé, ramené à 1 s'il dépasse le beatsPerBar de la cible.
        /// </summary>
        private static MetricPosition MapKeepPosition(
            MetricPosition source, MusicSpaceDefinition toSpace, int resumeOffsetBars)
        {
            int toBars = Mathf.Max(1, toSpace.barsPerLoop);
            int toBpb = Mathf.Max(1, toSpace.beatsPerBar);

            int rawBarIndex = (source.bar - 1) + resumeOffsetBars;
            int barIndex = ((rawBarIndex % toBars) + toBars) % toBars;

            int beat = (source.beat <= toBpb) ? source.beat : 1;

            return new MetricPosition(barIndex + 1, beat);
        }

        private static double SecondsPerBar(float bpm, int beatsPerBar)
        {
            double secondsPerBeat = 60.0 / Mathf.Max(1f, bpm);
            return secondsPerBeat * Mathf.Max(1, beatsPerBar);
        }

        private static double WrapToTargetLoop(double offsetSeconds, MusicSpaceDefinition toSpace)
        {
            double loopDuration = SecondsPerBar(toSpace.bpm, toSpace.beatsPerBar) * Mathf.Max(1, toSpace.barsPerLoop);
            if (loopDuration <= 0.0)
                return 0.0;

            double wrapped = offsetSeconds % loopDuration;
            if (wrapped < 0.0)
                wrapped += loopDuration;
            return wrapped;
        }
    }
}
