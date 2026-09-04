using System.Collections.Generic;
using UnityEngine;

namespace Loris.AudioEngine
{
    public class MusicSpaceController
    {
        private readonly MusicProject project;

        public MusicRequestState RequestedState { get; } = new MusicRequestState();
        public MusicRequestState AudibleState { get; } = new MusicRequestState();

        /// <summary>
        /// Latest continuous (unquantized) normalised axis values, keyed by axisId.
        /// Feeds VolumeEnvelope layers, which need the smooth value rather than the
        /// discretised integer coordinate used by Stepped layers.
        /// </summary>
        private readonly Dictionary<string, float> liveAxisValues = new();

        public MusicSpaceController(MusicProject musicProject)
        {
            project = musicProject;
        }

        /// <summary>
        /// Records a continuous normalised axis value (unquantized), keyed by axisId.
        /// </summary>
        public void SetLiveAxisValue(string axisId, float normalizedValue)
        {
            if (string.IsNullOrEmpty(axisId)) return;
            liveAxisValues[axisId] = Mathf.Clamp01(normalizedValue);
        }

        public float GetLiveAxis(string axisId)
            => !string.IsNullOrEmpty(axisId) && liveAxisValues.TryGetValue(axisId, out float v) ? v : 0f;

        /// <summary>
        /// Sets the requested space and resets the coordinate to all zeros.
        /// </summary>
        public void RequestSpace(string spaceId)
        {
            RequestedState.SpaceId = spaceId;

            MusicSpaceDefinition space = GetRequestedSpace();
            int axisCount = space != null ? space.axes.Count : 0;
            RequestedState.Coordinate = new int[axisCount];

            // Live values belong to the previous space's axes; drop them so a new
            // space's envelope layers don't briefly read stale values.
            liveAxisValues.Clear();
        }

        /// <summary>
        /// Updates a single axis value on the requested coordinate, clamped to the axis range.
        /// </summary>
        public void SetAxisValue(int axisIndex, int value)
        {
            MusicSpaceDefinition space = GetRequestedSpace();
            if (space == null) return;

            if (RequestedState.Coordinate == null || RequestedState.Coordinate.Length != space.axes.Count)
                RequestedState.Coordinate = new int[space.axes.Count];

            if (axisIndex >= 0 && axisIndex < space.axes.Count)
                RequestedState.Coordinate[axisIndex] = space.ClampAxisValue(axisIndex, value);
        }

        /// <summary>
        /// Directly applies a full state snapshot to RequestedState (used when promoting a pending state).
        /// </summary>
        public void ApplyRequestedState(MusicRequestState state)
        {
            RequestedState.SpaceId = state.SpaceId;
            RequestedState.Coordinate = state.Coordinate != null
                ? (int[])state.Coordinate.Clone()
                : null;
        }

        public void CommitRequestedToAudible()
        {
            AudibleState.SpaceId = RequestedState.SpaceId;
            AudibleState.Coordinate = RequestedState.Coordinate != null
                ? (int[])RequestedState.Coordinate.Clone()
                : null;
        }

        public MusicSpaceDefinition GetRequestedSpace()
        {
            if (string.IsNullOrEmpty(RequestedState.SpaceId)) return null;
            return project.GetSpace(RequestedState.SpaceId);
        }

        public MusicSpaceDefinition GetAudibleSpace()
        {
            if (string.IsNullOrEmpty(AudibleState.SpaceId)) return null;
            return project.GetSpace(AudibleState.SpaceId);
        }

        /// <summary>
        /// Returns all layers whose axis filters pass for the current audible coordinate.
        /// A layer with no filters (or all disabled) plays at every coordinate.
        /// </summary>
        public List<TargetLayer> ResolveAudibleTargetLayers()
        {
            List<TargetLayer> result = new List<TargetLayer>();

            MusicSpaceDefinition space = GetAudibleSpace();
            if (space == null) return result;

            int[] coordinate = AudibleState.Coordinate;

            foreach (var layer in space.layers)
            {
                if (layer == null || layer.clip == null) continue;

                float volume;
                bool continuous = false;

                if (layer.volumeMode == LayerVolumeMode.VolumeEnvelope)
                {
                    // Always present and phase-locked while the space is audible;
                    // volume follows the continuous axis value (may be 0 → silent voice).
                    float axisValue = GetLiveAxis(layer.volumeEnvelope.axisId);
                    volume = layer.baseVolume * layer.volumeEnvelope.Evaluate(axisValue);
                    continuous = true;
                }
                else
                {
                    if (!layer.IsActiveForCoordinate(coordinate)) continue;
                    volume = layer.baseVolume;
                }

                double loopTail = layer.loopTailEnabled
                    ? layer.loopTailLength.ToSeconds(space.bpm, space.beatsPerBar)
                    : 0.0;

                result.Add(new TargetLayer
                {
                    key = $"{space.spaceId}/{layer.layerId}",
                    stateId = space.spaceId,
                    layerId = layer.layerId,
                    clip = layer.clip,
                    volume = volume,
                    entryQuantization = layer.entryQuantization,
                    continuousVolume = continuous,
                    loopTailSeconds = loopTail
                });
            }

            return result;
        }
    }
}
