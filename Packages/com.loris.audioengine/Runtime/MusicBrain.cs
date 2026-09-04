using UnityEngine;

namespace Loris.AudioEngine
{
    /// <summary>
    /// Base class for game-side music brains.
    ///
    /// Subclass this in your game project. Override EvaluateState to read
    /// gameplay data and push it to the engine via SetSpace / SetAxis.
    /// The package never imports game-specific types — all that logic lives
    /// in the subclass.
    /// </summary>
    public abstract class MusicBrain : MonoBehaviour
    {
        [SerializeField] private AudioEngineBehaviour audioEngine;

        protected AudioEngineFacade Engine => audioEngine != null ? audioEngine.Engine : null;

        protected virtual void Update()
        {
            if (Engine != null)
                EvaluateState();
        }

        protected abstract void EvaluateState();

        protected void SetSpace(string spaceId, QuantizationMode quantization = QuantizationMode.NextBar)
            => Engine?.SetSpace(spaceId, quantization);

        protected void SetAxis(int axisIndex, int value, QuantizationMode quantization = QuantizationMode.NextBar)
            => Engine?.SetAxis(axisIndex, value, quantization);

        protected void SetAxisByName(string axisName, float normalizedValue, QuantizationMode quantization = QuantizationMode.Immediate)
            => Engine?.SetAxisByName(axisName, normalizedValue, quantization);
    }
}
