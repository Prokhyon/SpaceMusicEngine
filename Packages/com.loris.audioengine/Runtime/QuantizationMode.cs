namespace Loris.AudioEngine
{
    /// <summary>
    /// Modes de quantization utilisables pour demander un changement.
    /// </summary>
    public enum QuantizationMode
    {
        Immediate,
        NextBeat,
        NextBar,
        NextLoop
    }
}
