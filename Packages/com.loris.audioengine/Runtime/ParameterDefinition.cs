using UnityEngine;

namespace Loris.AudioEngine
{
    /// <summary>
    /// Shared runtime variable that game systems write to and ContextPresets read from.
    /// Game code needs no knowledge of music; music code needs no knowledge of the game.
    /// </summary>
    public abstract class ParameterDefinition : ScriptableObject
    {
        public abstract float RawValue { get; }
    }
}
