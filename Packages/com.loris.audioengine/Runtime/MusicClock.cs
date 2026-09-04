using System;
using UnityEngine;

namespace Loris.AudioEngine
{
    /// <summary>
    /// Transport musical global.
    /// </summary>
    public class MusicClock
    {
        private double loopStartDspTime;
        private double secondsPerBeat;
        private double secondsPerBar;
        private double loopDurationSeconds;

        private int beatsPerBar;
        private int barsPerLoop;

        private bool isRunning;

        public bool IsRunning => isRunning;
        public double DspTime => AudioSettings.dspTime;

        public void StartSpaceClock(MusicSpaceDefinition space)
        {
            if (space == null)
                return;

            beatsPerBar = Mathf.Max(1, space.beatsPerBar);
            barsPerLoop = Mathf.Max(1, space.barsPerLoop);

            secondsPerBeat = 60.0 / Mathf.Max(1f, space.bpm);
            secondsPerBar = secondsPerBeat * beatsPerBar;
            loopDurationSeconds = secondsPerBar * barsPerLoop;

            loopStartDspTime = AudioSettings.dspTime;
            isRunning = true;
        }

        /// <summary>
        /// Démarre (ou bascule) la clock sur un espace en alignant la boucle pour
        /// qu'au temps DSP donné la position de boucle vaille loopOffsetSeconds.
        ///
        /// Utilisé à l'entrée d'un espace cible de transition : on veut atterrir
        /// à une position métrique précise (point d'arrivée) plutôt qu'à 0.
        /// </summary>
        public void StartSpaceClockAligned(MusicSpaceDefinition space, double atDspTime, double loopOffsetSeconds)
        {
            if (space == null)
                return;

            beatsPerBar = Mathf.Max(1, space.beatsPerBar);
            barsPerLoop = Mathf.Max(1, space.barsPerLoop);

            secondsPerBeat = 60.0 / Mathf.Max(1f, space.bpm);
            secondsPerBar = secondsPerBeat * beatsPerBar;
            loopDurationSeconds = secondsPerBar * barsPerLoop;

            loopStartDspTime = atDspTime - loopOffsetSeconds;
            isRunning = true;
        }

        public void Stop()
        {
            loopStartDspTime = 0.0;
            secondsPerBeat = 0.0;
            secondsPerBar = 0.0;
            loopDurationSeconds = 0.0;
            beatsPerBar = 0;
            barsPerLoop = 0;
            isRunning = false;
        }

        public double GetSecondsPerBeat() => secondsPerBeat;
        public double GetSecondsPerBar() => secondsPerBar;
        public double GetLoopDurationSeconds() => loopDurationSeconds;
        public int GetBeatsPerBar() => beatsPerBar;
        public int GetBarsPerLoop() => barsPerLoop;

        /// <summary>
        /// Retourne la position actuelle dans la boucle.
        /// </summary>
        public double GetLoopPositionSeconds()
        {
            return GetLoopPositionSecondsAtDspTime(AudioSettings.dspTime);
        }

        /// <summary>
        /// Retourne la position dans la boucle pour un temps DSP donné.
        /// 
        /// C'est utile lorsqu'on veut programmer une nouvelle couche
        /// qui doit entrer déjà synchronisée à une position musicale précise.
        /// </summary>
        public double GetLoopPositionSecondsAtDspTime(double dspTime)
        {
            if (!isRunning || loopDurationSeconds <= 0.0)
                return 0.0;

            double elapsed = dspTime - loopStartDspTime;
            double position = elapsed % loopDurationSeconds;

            if (position < 0.0)
                position += loopDurationSeconds;

            return position;
        }

        public double GetSafeScheduleLeadTime()
        {
            return 1.0;
        }

        // Short lead for the very first PlayScheduled call — enough for the OS audio
        // scheduler, but avoids the 1-second silence that comes from reusing the loop lead.
        public static double GetStartupLeadTime()
        {
            return 0.1;
        }

        public double GetNextQuantizedDspTime(QuantizationMode mode)
        {
            return GetNextQuantizedDspTime(mode, AudioSettings.dspTime);
        }

        public double GetNextQuantizedDspTime(QuantizationMode mode, double referenceDspTime)
        {
            double safeReference = Math.Max(referenceDspTime, AudioSettings.dspTime + GetSafeScheduleLeadTime());

            if (!isRunning)
                return safeReference;

            double elapsedAtRef = safeReference - loopStartDspTime;

            switch (mode)
            {
                case QuantizationMode.Immediate:
                    return safeReference;

                case QuantizationMode.NextBeat:
                {
                    double beatIndex = Math.Ceiling(elapsedAtRef / secondsPerBeat);
                    return loopStartDspTime + beatIndex * secondsPerBeat;
                }

                case QuantizationMode.NextBar:
                {
                    double barIndex = Math.Ceiling(elapsedAtRef / secondsPerBar);
                    return loopStartDspTime + barIndex * secondsPerBar;
                }

                case QuantizationMode.NextLoop:
                {
                    double loopIndex = Math.Ceiling(elapsedAtRef / loopDurationSeconds);
                    return loopStartDspTime + loopIndex * loopDurationSeconds;
                }

                default:
                    return safeReference;
            }
        }

        /// <summary>
        /// Prochain temps DSP (>= référence sécurisée) auquel la boucle source
        /// atteint un offset donné dans la boucle.
        ///
        /// Généralise GetNextQuantizedDspTime à une position métrique arbitraire :
        /// le resolver convertit une MetricPosition source en offset secondes
        /// (via MetricPosition.ToSeconds) puis passe par ici pour viser ce point.
        /// </summary>
        public double GetNextDspTimeForLoopOffset(double offsetSeconds, double referenceDspTime)
        {
            double safeReference = Math.Max(referenceDspTime, AudioSettings.dspTime + GetSafeScheduleLeadTime());

            if (!isRunning || loopDurationSeconds <= 0.0)
                return safeReference;

            double offset = offsetSeconds % loopDurationSeconds;
            if (offset < 0.0)
                offset += loopDurationSeconds;

            double elapsed = safeReference - loopStartDspTime;
            double loopIndex = Math.Floor(elapsed / loopDurationSeconds);

            double candidate = loopStartDspTime + loopIndex * loopDurationSeconds + offset;
            if (candidate < safeReference)
                candidate += loopDurationSeconds;

            return candidate;
        }

        public double GetNextDspTimeForLoopOffset(double offsetSeconds)
        {
            return GetNextDspTimeForLoopOffset(offsetSeconds, AudioSettings.dspTime);
        }

        /// <summary>
        /// Prochain temps DSP (>= référence sécurisée) aligné sur une grille
        /// régulière d'intervalle donné, relative au début de boucle.
        /// Utilisé par le déclenchement par fréquence (ex. toutes les mesures).
        /// </summary>
        public double GetNextDspTimeForInterval(double intervalSeconds, double referenceDspTime)
        {
            double safeReference = Math.Max(referenceDspTime, AudioSettings.dspTime + GetSafeScheduleLeadTime());

            if (!isRunning || intervalSeconds <= 0.0)
                return safeReference;

            double elapsed = safeReference - loopStartDspTime;
            double index = Math.Ceiling(elapsed / intervalSeconds);

            return loopStartDspTime + index * intervalSeconds;
        }

        public double GetNextDspTimeForInterval(double intervalSeconds)
        {
            return GetNextDspTimeForInterval(intervalSeconds, AudioSettings.dspTime);
        }

        /// <summary>
        /// Convertit un temps DSP en position métrique (mesure.temps) dans la boucle
        /// source courante, arrondie au temps le plus proche.
        ///
        /// Utile pour le mode KeepSourcePosition en déclenchement par fréquence :
        /// on récupère la position de la source au point de départ pour la reporter
        /// (modulo) sur la cible.
        /// </summary>
        public MetricPosition GetMetricPositionAtDspTime(double dspTime)
        {
            if (!isRunning || secondsPerBeat <= 0.0)
                return MetricPosition.One;

            double position = GetLoopPositionSecondsAtDspTime(dspTime);
            long totalBeats = (long)Math.Round(position / secondsPerBeat);

            int bpb = Mathf.Max(1, beatsPerBar);
            long beatsInLoop = (long)bpb * Mathf.Max(1, barsPerLoop);
            if (beatsInLoop > 0)
                totalBeats = ((totalBeats % beatsInLoop) + beatsInLoop) % beatsInLoop;

            int bar = (int)(totalBeats / bpb) + 1;
            int beat = (int)(totalBeats % bpb) + 1;
            return new MetricPosition(bar, beat);
        }
    }
}
