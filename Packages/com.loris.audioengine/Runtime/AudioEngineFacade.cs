using System;
using System.Collections.Generic;
using UnityEngine;

namespace Loris.AudioEngine
{
    /// <summary>
    /// API principale du moteur audio.
    /// </summary>
    public class AudioEngineFacade
    {
        private readonly MusicProject project;
        private readonly MusicSpaceController spaceController;
        private readonly PlaybackBackend playbackBackend;
        private readonly MusicClock clock;

        private MusicRequestState pendingState;
        private double pendingApplyDspTime = -1.0;

        // Descripteur de la transition en cours, pour l'affichage debug (timeline propre
        // à la transition : départ, entrée cible, grille métrique de la piste).
        private double transitionDepartureDsp;
        private double transitionTargetEntryDsp;
        private float transitionBpm = 120f;
        private int transitionBeatsPerBar = 4;
        private bool transitionHasTrack;

        // File d'actions planifiées pour les transitions (départ, sortie source, entrée cible).
        // Chaque action est déclenchée quand le temps DSP courant atteint fireDspTime.
        private struct ScheduledAction
        {
            public double fireDspTime;
            public Action run;
        }

        private readonly List<ScheduledAction> scheduledActions = new List<ScheduledAction>();

        private bool HasScheduledTransition => scheduledActions.Count > 0;

        public AudioEngineFacade(MusicProject project, Transform runtimeRoot)
        {
            this.project = project;
            clock = new MusicClock();
            spaceController = new MusicSpaceController(project);
            playbackBackend = new PlaybackBackend(runtimeRoot, clock, project);
        }

        public void SetSpace(string spaceId, QuantizationMode quantization)
        {
            bool firstStart = string.IsNullOrEmpty(spaceController.AudibleState.SpaceId);

            if (!firstStart &&
                pendingState == null &&
                !HasScheduledTransition &&
                spaceController.RequestedState.SpaceId == spaceId)
            {
                return;
            }

            // Espace courant (avant de réécrire l'état demandé) : source de la transition.
            string fromSpaceId = spaceController.AudibleState.SpaceId;

            spaceController.RequestSpace(spaceId);
            MusicSpaceDefinition requestedSpace = spaceController.GetRequestedSpace();
            if (requestedSpace == null)
                return;

            if (firstStart || !clock.IsRunning)
            {
                // Transition d'intro (depuis silence) si définie.
                TransitionDefinition intro = project.GetTransition("", spaceId);
                if (intro != null)
                {
                    ScheduleTransition(intro, null, requestedSpace);
                    return;
                }

                clock.StartSpaceClock(requestedSpace);

                spaceController.CommitRequestedToAudible();
                List<TargetLayer> layers = spaceController.ResolveAudibleTargetLayers();

                double startTime = AudioSettings.dspTime + MusicClock.GetStartupLeadTime();
                playbackBackend.ApplyState(layers, startTime);
                return;
            }

            // Transition espace → espace si définie.
            TransitionDefinition transition = project.GetTransition(fromSpaceId, spaceId);
            if (transition != null)
            {
                MusicSpaceDefinition fromSpace = project.GetSpace(fromSpaceId);
                ScheduleTransition(transition, fromSpace, requestedSpace);
                return;
            }

            // Fallback : crossfade implicite quantisé (comportement historique inchangé).
            pendingState = new MusicRequestState
            {
                SpaceId = spaceController.RequestedState.SpaceId,
                Coordinate = spaceController.RequestedState.Coordinate != null
                    ? (int[])spaceController.RequestedState.Coordinate.Clone()
                    : null
            };

            pendingApplyDspTime = clock.GetNextQuantizedDspTime(quantization);
        }

        /// <summary>
        /// Planifie une transition résolue : démarrage de la piste, sortie de la source
        /// (fade ou cut), puis entrée de l'espace cible aligné sur son point d'arrivée.
        /// </summary>
        private void ScheduleTransition(
            TransitionDefinition transition,
            MusicSpaceDefinition fromSpace,
            MusicSpaceDefinition toSpace)
        {
            // Une transition supersede toute demande en attente (axe ou transition antérieure).
            pendingState = null;
            pendingApplyDspTime = -1.0;
            scheduledActions.Clear();

            bool sourceRunning = clock.IsRunning && fromSpace != null;
            double lead = clock.GetSafeScheduleLeadTime();
            double reference = sourceRunning
                ? AudioSettings.dspTime + lead
                : AudioSettings.dspTime + MusicClock.GetStartupLeadTime();

            TransitionPlan plan = TransitionResolver.Resolve(transition, fromSpace, toSpace, clock, reference);
            if (!plan.valid)
                return;

            // Mémorise la timeline de la transition pour l'UI de debug.
            transitionDepartureDsp = plan.departureDspTime;
            transitionTargetEntryDsp = plan.targetEntryDspTime;
            transitionBpm = transition.bpm;
            transitionBeatsPerBar = transition.beatsPerBar;
            transitionHasTrack = plan.hasTrack;

            string fromSpaceId = fromSpace != null ? fromSpace.spaceId : "";
            float fadeOut = project.defaultFadeOutSeconds;
            float fadeIn = project.defaultFadeInSeconds;

            // 1. Départ : lancer la piste de transition.
            if (plan.hasTrack)
            {
                // Durée réelle de lecture de la piste = jusqu'à trackEndDspTime (tail inclus).
                // L'entrée de l'espace cible reste planifiée à targetEntryDspTime.
                double trackDuration = plan.trackEndDspTime - plan.departureDspTime;
                double departure = plan.departureDspTime;
                double trackOffset = plan.trackStartOffsetSeconds;
                AudioClip clip = plan.clip;
                // Output de la piste : override propre à la transition s'il est défini,
                // sinon l'output de l'espace cible.
                var transitionOutput = transition.output != null
                    ? transition.output
                    : project.ResolveOutput(toSpace);

                ScheduleAction(plan.departureDspTime - lead, () =>
                    playbackBackend.StartTransitionTrack(clip, departure, trackOffset, trackDuration, fadeIn, transitionOutput));
            }

            // 2. Sortie de la source.
            if (sourceRunning)
            {
                double exitAt = plan.keepSourcePlaying ? plan.sourceStopDspTime : plan.departureDspTime;
                ScheduleSourceExit(fromSpaceId, plan.sourceExit, exitAt, fadeOut, lead);
            }

            // 3. Entrée de la cible (ou silence) à la fin de piste.
            double targetEntry = plan.targetEntryDspTime;
            bool toSilence = plan.toSilence;
            double arrivalOffset = plan.targetArrivalOffsetSeconds;

            ScheduleAction(plan.targetEntryDspTime - lead, () =>
            {
                if (toSilence)
                {
                    playbackBackend.StopAll();
                    clock.Stop();
                    spaceController.RequestSpace("");
                    spaceController.CommitRequestedToAudible();
                    return;
                }

                clock.StartSpaceClockAligned(toSpace, targetEntry, arrivalOffset);
                spaceController.CommitRequestedToAudible();
                List<TargetLayer> layers = spaceController.ResolveAudibleTargetLayers();
                playbackBackend.ApplyState(layers, targetEntry);
            });
        }

        private void ScheduleSourceExit(string spaceId, SourceExit exit, double atDspTime, float fadeOut, double lead)
        {
            if (exit == SourceExit.Cut)
            {
                // Planifie le cut un peu en avance pour que SetScheduledEndTime soit pris en compte.
                ScheduleAction(atDspTime - lead, () => playbackBackend.HardStopSpace(spaceId, atDspTime));
            }
            else
            {
                // Démarre le fade pour qu'il se termine au point voulu.
                ScheduleAction(atDspTime - fadeOut, () => playbackBackend.FadeOutSpace(spaceId, fadeOut));
            }
        }

        private void ScheduleAction(double fireDspTime, Action run)
        {
            scheduledActions.Add(new ScheduledAction { fireDspTime = fireDspTime, run = run });
            scheduledActions.Sort((a, b) => a.fireDspTime.CompareTo(b.fireDspTime));
        }

        /// <summary>
        /// Demande un changement sur un axe donné.
        ///
        /// Plusieurs appels dans la même mesure s'accumulent dans le même
        /// événement pending : GetNextQuantizedDspTime est stable dans une mesure.
        /// </summary>
        public void SetAxis(int axisIndex, int value, QuantizationMode quantization)
        {
            MusicSpaceDefinition space = spaceController.GetRequestedSpace();
            if (space == null || axisIndex < 0 || axisIndex >= space.axes.Count) return;

            int clamped = space.ClampAxisValue(axisIndex, value);

            if (pendingState == null)
            {
                int[] coord = spaceController.RequestedState.Coordinate;
                if (coord != null && axisIndex < coord.Length && coord[axisIndex] == clamped)
                    return;
            }

            spaceController.SetAxisValue(axisIndex, value);

            if (string.IsNullOrEmpty(spaceController.AudibleState.SpaceId))
                return;

            pendingState = new MusicRequestState
            {
                SpaceId = spaceController.RequestedState.SpaceId,
                Coordinate = spaceController.RequestedState.Coordinate != null
                    ? (int[])spaceController.RequestedState.Coordinate.Clone()
                    : null
            };

            pendingApplyDspTime = clock.GetNextQuantizedDspTime(quantization);
        }

        public void Tick()
        {
            double nowDsp = AudioSettings.dspTime;

            // Actions de transition planifiées, déclenchées dans l'ordre temporel.
            while (scheduledActions.Count > 0 && nowDsp >= scheduledActions[0].fireDspTime)
            {
                Action run = scheduledActions[0].run;
                scheduledActions.RemoveAt(0);
                run?.Invoke();
            }

            // Pendant une transition planifiée, l'entrée du nouvel espace est pilotée
            // exclusivement par ses actions (départ / sortie source / entrée cible).
            // On gèle donc l'application des états pending : sinon un changement d'axe
            // (le DefaultMusicBrain en émet chaque frame) créerait un pendingState ciblant
            // l'espace requested — le nouveau — et le ferait démarrer avant la fin de la
            // piste, décalant tout (espace prématuré, puis piste, boucle désalignée).
            if (!HasScheduledTransition && pendingState != null && pendingApplyDspTime > 0.0)
            {
                double now = AudioSettings.dspTime;
                double lead = clock.GetSafeScheduleLeadTime();

                if (now + lead >= pendingApplyDspTime)
                {
                    // Un changement d'espace via le fallback (aucune transition définie) doit
                    // redémarrer l'horloge sur le nouvel espace : sinon les nouvelles couches
                    // héritent de la durée de boucle et de l'offset de l'ancien espace
                    // (boucle erronée, départ décalé au milieu du fichier, coupures).
                    // Un simple changement d'axe dans le même espace ne touche pas l'horloge.
                    bool spaceChanging = pendingState.SpaceId != spaceController.AudibleState.SpaceId;
                    if (spaceChanging)
                    {
                        MusicSpaceDefinition newSpace = project.GetSpace(pendingState.SpaceId);
                        if (newSpace != null)
                            clock.StartSpaceClockAligned(newSpace, pendingApplyDspTime, 0.0);
                    }

                    spaceController.ApplyRequestedState(pendingState);
                    spaceController.CommitRequestedToAudible();

                    List<TargetLayer> layers = spaceController.ResolveAudibleTargetLayers();
                    playbackBackend.ApplyState(layers, pendingApplyDspTime);

                    pendingState = null;
                    pendingApplyDspTime = -1.0;
                }
            }

            // Volume continu des couches à enveloppe (proximité, etc.), recalculé chaque frame.
            UpdateEnvelopeVolumes();

            playbackBackend.Tick();
        }

        /// <summary>
        /// Recompute et applique le volume des couches audibles en mode VolumeEnvelope,
        /// à partir de la valeur d'axe continue courante.
        /// </summary>
        private void UpdateEnvelopeVolumes()
        {
            MusicSpaceDefinition space = spaceController.GetAudibleSpace();
            if (space == null) return;

            foreach (var layer in space.layers)
            {
                if (layer == null || layer.clip == null) continue;
                if (layer.volumeMode != LayerVolumeMode.VolumeEnvelope) continue;

                float axisValue = spaceController.GetLiveAxis(layer.volumeEnvelope.axisId);
                float volume = layer.baseVolume * layer.volumeEnvelope.Evaluate(axisValue);
                playbackBackend.SetLayerVolume($"{space.spaceId}/{layer.layerId}", volume);
            }
        }

        public void StopPlayback()
        {
            pendingState = null;
            pendingApplyDspTime = -1.0;
            scheduledActions.Clear();

            playbackBackend.StopAll();
            clock.Stop();
        }

        public void RestartCurrentFromBeginning()
        {
            if (string.IsNullOrEmpty(spaceController.AudibleState.SpaceId))
                return;

            MusicSpaceDefinition space = project.GetSpace(spaceController.AudibleState.SpaceId);
            if (space == null)
                return;

            playbackBackend.StopAll();
            clock.StartSpaceClock(space);

            List<TargetLayer> layers = spaceController.ResolveAudibleTargetLayers();
            double startTime = AudioSettings.dspTime + MusicClock.GetStartupLeadTime();
            playbackBackend.ApplyState(layers, startTime);
        }

        /// <summary>
        /// Sets an axis by name instead of index, converting a normalised float [0..1]
        /// to an integer level based on the axis's levelCount.
        /// Looks up the axis in the currently requested space, so it is safe to call
        /// immediately after SetSpace before the transition fires.
        /// </summary>
        public void SetAxisByName(string axisName, float normalizedValue, QuantizationMode quantization)
        {
            MusicSpaceDefinition space = spaceController.GetRequestedSpace();
            if (space == null) return;

            // Record the continuous (unquantized) value for VolumeEnvelope layers,
            // which need the smooth value rather than the discretised level below.
            spaceController.SetLiveAxisValue(axisName, normalizedValue);

            for (int i = 0; i < space.axes.Count; i++)
            {
                if (space.axes[i].axisId != axisName) continue;
                int level = Mathf.RoundToInt(Mathf.Clamp01(normalizedValue) * space.axes[i].levelCount);
                SetAxis(i, level, quantization);
                return;
            }
        }

        public ContextPreset GetCurrentContextPreset()
        {
            MusicSpaceDefinition space = spaceController.GetAudibleSpace();
            return space?.contextPreset;
        }

        // Call once at startup so clips are in memory before any SetSpace fires.
        public void PreloadAllClips()
        {
            foreach (var space in project.spaces)
                foreach (var layer in space.layers)
                    if (layer?.clip != null && layer.clip.loadState != AudioDataLoadState.Loaded)
                        layer.clip.LoadAudioData();
        }

        public string GetCurrentSpace() => spaceController.AudibleState.SpaceId;
        public string GetRequestedSpace() => spaceController.RequestedState.SpaceId;

        public int[] GetCurrentCoordinate() => spaceController.AudibleState.Coordinate;
        public int[] GetRequestedCoordinate() => spaceController.RequestedState.Coordinate;

        public int GetAxisCount()
        {
            MusicSpaceDefinition space = spaceController.GetAudibleSpace();
            return space != null ? space.axes.Count : 0;
        }

        public string GetAxisId(int axisIndex)
        {
            MusicSpaceDefinition space = spaceController.GetAudibleSpace();
            if (space == null || axisIndex < 0 || axisIndex >= space.axes.Count) return "";
            return space.axes[axisIndex].axisId;
        }

        public int GetAxisLevelCount(int axisIndex)
        {
            MusicSpaceDefinition space = spaceController.GetAudibleSpace();
            if (space == null || axisIndex < 0 || axisIndex >= space.axes.Count) return 0;
            return space.axes[axisIndex].levelCount;
        }

        public bool IsTransitioning => scheduledActions.Count > 0;

        /// <summary>True tant qu'une horloge d'espace tourne (un espace est audible).</summary>
        public bool IsClockRunning => clock.IsRunning;

        /// <summary>Position métrique (mesure.temps) dans la boucle de l'espace courant.</summary>
        public MetricPosition GetLoopMetricPosition()
            => clock.GetMetricPositionAtDspTime(AudioSettings.dspTime);

        // --- Timeline de la transition en cours (debug) ---

        /// <summary>Secondes écoulées depuis le départ de la transition (négatif avant le départ).</summary>
        public double GetTransitionElapsedSeconds()
            => AudioSettings.dspTime - transitionDepartureDsp;

        /// <summary>Durée totale départ → entrée cible.</summary>
        public double GetTransitionTotalSeconds()
            => transitionTargetEntryDsp - transitionDepartureDsp;

        /// <summary>Secondes restantes avant l'entrée de l'espace cible (négatif une fois entré).</summary>
        public double GetTransitionEntryCountdown()
            => transitionTargetEntryDsp - AudioSettings.dspTime;

        public bool TransitionHasTrack => transitionHasTrack;

        /// <summary>Position métrique (mesure.temps) sur la grille de la piste de transition.</summary>
        public MetricPosition GetTransitionMetricPosition()
        {
            double secondsPerBeat = 60.0 / Mathf.Max(1f, transitionBpm);
            int bpb = Mathf.Max(1, transitionBeatsPerBar);
            double elapsed = Math.Max(0.0, AudioSettings.dspTime - transitionDepartureDsp);
            long totalBeats = (long)Math.Floor(elapsed / secondsPerBeat);
            int bar = (int)(totalBeats / bpb) + 1;
            int beat = (int)(totalBeats % bpb) + 1;
            return new MetricPosition(bar, beat);
        }

        public double GetLoopDurationSeconds() => clock.GetLoopDurationSeconds();
        public double GetLoopPositionSeconds() => clock.GetLoopPositionSeconds();
        public int GetActiveVoiceCount() => playbackBackend.GetActiveVoiceCount();

        /// <summary>
        /// Position de lecture réelle d'une couche ("espace/couche"), en secondes (-1 si inactive).
        /// À comparer à GetLoopPositionSeconds() : l'écart mesure la désynchronisation.
        /// </summary>
        public double GetLayerPlaybackPositionSeconds(string layerKey)
            => playbackBackend.GetLayerPlaybackPositionSeconds(layerKey);
        public IEnumerable<string> GetActiveLayerKeys() => playbackBackend.GetActiveLayerKeys();
    }
}
