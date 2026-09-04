using System;
using UnityEngine;

namespace Loris.AudioEngine
{
    [Serializable]
    public class TargetLayer
    {
        public string key;
        public string stateId;
        public string layerId;
        public AudioClip clip;
        public float volume;
        public QuantizationMode entryQuantization;

        /// <summary>
        /// True for VolumeEnvelope layers: the voice is created without an entry fade
        /// because its volume is driven continuously each frame by the envelope.
        /// </summary>
        public bool continuousVolume;

        /// <summary>
        /// Résonance (secondes) jouée après le point de boucle : la source sortante
        /// continue de sonner par-dessus la boucle suivante au lieu d'être coupée net.
        /// </summary>
        public double loopTailSeconds;
    }

    [Serializable]
    public class MusicRequestState
    {
        public string SpaceId;

        /// <summary>
        /// N-dimensional coordinate: one int per axis defined in the space.
        /// Length always matches space.axes.Count for the current space.
        /// </summary>
        public int[] Coordinate;
    }
}
