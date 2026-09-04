using System;
using System.Collections.Generic;
using UnityEngine;

namespace Loris.AudioEngine
{
    /// <summary>
    /// IMGUI debug panel for the adaptive music engine.
    ///
    /// Everything adapts to the assigned MusicProject: it lists every space, the
    /// axes of the current space, and every ParameterDefinition referenced by the
    /// spaces' ContextPresets. Use it to:
    ///  - jump to any space with a chosen quantization, and watch the transition
    ///    fire relative to the current loop position (so you can test triggering at
    ///    different moments in the bar/loop);
    ///  - drive axes manually;
    ///  - set FloatParameter / IntParameter values by hand;
    ///  - see which layers are currently audible.
    /// </summary>
    [AddComponentMenu("SpaceMusicEngine/Debug/Music Debug UI")]
    public class MusicDebugUI : MonoBehaviour
    {
        [SerializeField] private AudioEngineBehaviour audioEngineBehaviour;

        [Tooltip("Upper bound of the parameter sliders (type exact values in the field for anything larger).")]
        [SerializeField] private float parameterSliderMax = 8f;

        private AudioEngineFacade engine;

        private Vector2 _scroll;
        private QuantizationMode _quant = QuantizationMode.NextBar;

        // Local axis slider values, resized when the axis count changes.
        private int[] _axisValues = Array.Empty<int>();

        // Distinct parameters gathered from the project's presets (rebuilt each frame — cheap).
        private readonly List<ParameterDefinition> _parameters = new List<ParameterDefinition>();

        private MusicProject Project => audioEngineBehaviour != null ? audioEngineBehaviour.musicProject : null;

        private void ResolveEngine()
        {
            audioEngineBehaviour ??= FindObjectOfType<AudioEngineBehaviour>();
            if (audioEngineBehaviour != null)
                engine = audioEngineBehaviour.Engine;
        }

        private void Awake() => ResolveEngine();

        private void OnGUI()
        {
            if (engine == null) ResolveEngine();

            GUILayout.BeginArea(new Rect(20, 20, 560, Screen.height - 40), GUI.skin.box);
            GUILayout.Label("<b>Adaptive Music — Debug</b>", RichLabel());

            if (audioEngineBehaviour == null)
            {
                GUILayout.Label("AudioEngineBehaviour: NULL");
                GUILayout.EndArea();
                return;
            }
            if (engine == null)
            {
                GUILayout.Label("AudioEngineFacade: NULL (assign a MusicProject).");
                GUILayout.EndArea();
                return;
            }
            if (Project == null)
            {
                GUILayout.Label("MusicProject: NULL");
                GUILayout.EndArea();
                return;
            }

            _scroll = GUILayout.BeginScrollView(_scroll);

            DrawStatus();
            DrawQuantizationSelector();
            DrawSpaces();
            DrawTransport();
            DrawAxes();
            DrawParameters();
            DrawLayers();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ sections

        private void DrawStatus()
        {
            Section("Runtime");

            string current = engine.GetCurrentSpace();
            string requested = engine.GetRequestedSpace();

            GUILayout.Label($"Current space:    {Or(current, "-")}");
            GUILayout.Label($"Requested space:  {Or(requested, "-")}"
                          + (current != requested ? "   (change pending)" : ""));
            GUILayout.Label($"Current coord:    [{CoordToString(engine.GetCurrentCoordinate())}]");
            GUILayout.Label($"Requested coord:  [{CoordToString(engine.GetRequestedCoordinate())}]");
            GUILayout.Label($"Active voices:    {engine.GetActiveVoiceCount()}");

            // --- Loop position: seconds + bar.beat + progress bar. Always shown, never
            //     hidden by a transition (they are two independent readouts). ---
            bool clockRunning = engine.IsClockRunning;
            double dur = engine.GetLoopDurationSeconds();
            double pos = engine.GetLoopPositionSeconds();
            if (clockRunning)
            {
                MetricPosition lm = engine.GetLoopMetricPosition();
                GUILayout.Label($"Loop:        {pos:F2}s / {dur:F2}s   ·   bar {lm.bar}.{lm.beat}");
            }
            else
            {
                GUILayout.Label("Loop:        —");
            }
            DrawBar(clockRunning && dur > 0.0 ? (float)(pos / dur) : 0f);

            // --- Transition position: SAME format (progress in the track + bar.beat),
            //     as an independent second readout. ---
            if (engine.IsTransitioning)
            {
                MetricPosition tm = engine.GetTransitionMetricPosition();
                double el = engine.GetTransitionElapsedSeconds();
                double tot = engine.GetTransitionTotalSeconds();
                double entry = engine.GetTransitionEntryCountdown();
                string barBeat = engine.TransitionHasTrack ? $"bar {tm.bar}.{tm.beat}" : "direct";
                string elapsed = el < 0.0 ? $"launch in {-el:F2}s" : $"{el:F2}s / {tot:F2}s";
                GUILayout.Label($"<color=yellow>Transition:</color>  {elapsed}   ·   {barBeat}   ·   entry in {entry:F2}s", RichLabel());
                DrawBar(tot > 0.0 ? Mathf.Clamp01((float)(el / tot)) : 0f);
            }
            else
            {
                GUILayout.Label("Transition:  —");
                DrawBar(0f);
            }
        }

        private static void DrawBar(float frac)
        {
            Rect r = GUILayoutUtility.GetRect(300, 12, GUILayout.Width(300));
            GUI.Box(r, GUIContent.none);
            GUI.Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(frac), r.height), GUIContent.none);
        }

        private void DrawQuantizationSelector()
        {
            Section("Quantization (applies to space & axis changes)");
            GUILayout.BeginHorizontal();
            foreach (QuantizationMode mode in Enum.GetValues(typeof(QuantizationMode)))
            {
                bool on = _quant == mode;
                bool next = GUILayout.Toggle(on, mode.ToString(), GUI.skin.button, GUILayout.Height(24));
                if (next && !on) _quant = mode;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawSpaces()
        {
            Section("Spaces — click to transition");
            string current = engine.GetCurrentSpace();
            string requested = engine.GetRequestedSpace();

            foreach (var space in Project.spaces)
            {
                if (space == null || string.IsNullOrEmpty(space.spaceId)) continue;

                bool isCurrent = space.spaceId == current;
                bool isRequested = space.spaceId == requested && !isCurrent;

                Color prev = GUI.color;
                if (isCurrent) GUI.color = Color.green;
                else if (isRequested) GUI.color = Color.yellow;

                string tag = isCurrent ? "  ● current" : isRequested ? "  ○ requested" : "";
                double loopLen = space.beatsPerBar * space.barsPerLoop * (60.0 / Mathf.Max(1f, space.bpm));
                if (GUILayout.Button($"{space.spaceId}   ({space.bpm:0} bpm · {space.barsPerLoop} bars · {loopLen:F1}s){tag}"))
                    engine.SetSpace(space.spaceId, _quant);

                GUI.color = prev;
            }
        }

        private void DrawTransport()
        {
            Section("Transport");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Stop")) engine.StopPlayback();
            if (GUILayout.Button("Restart from start")) engine.RestartCurrentFromBeginning();
            GUILayout.EndHorizontal();
        }

        private void DrawAxes()
        {
            int axisCount = engine.GetAxisCount();
            Section($"Axes — current space ({axisCount})");

            if (axisCount == 0)
            {
                GUILayout.Label("No axes in the current space.");
                return;
            }

            if (_axisValues.Length != axisCount) _axisValues = new int[axisCount];
            int[] requested = engine.GetRequestedCoordinate();
            if (requested != null)
                for (int i = 0; i < Mathf.Min(_axisValues.Length, requested.Length); i++)
                    _axisValues[i] = requested[i];

            for (int i = 0; i < axisCount; i++)
            {
                string id = engine.GetAxisId(i);
                if (string.IsNullOrEmpty(id)) id = $"Axis {i}";
                int maxLevel = engine.GetAxisLevelCount(i);

                GUILayout.Space(4);
                GUILayout.Label($"{id}  (0 – {maxLevel})");

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("-", GUILayout.Width(32)))
                    SetAxis(i, _axisValues[i] - 1, maxLevel);
                GUILayout.Label(_axisValues[i].ToString(), GUILayout.Width(28));
                if (GUILayout.Button("+", GUILayout.Width(32)))
                    SetAxis(i, _axisValues[i] + 1, maxLevel);

                int sliderVal = Mathf.RoundToInt(GUILayout.HorizontalSlider(_axisValues[i], 0, maxLevel, GUILayout.Width(240)));
                if (sliderVal != _axisValues[i]) SetAxis(i, sliderVal, maxLevel);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                for (int v = 0; v <= maxLevel; v++)
                    if (GUILayout.Button(v.ToString(), GUILayout.Width(28)))
                        SetAxis(i, v, maxLevel);
                GUILayout.EndHorizontal();
            }
        }

        private void SetAxis(int index, int value, int maxLevel)
        {
            _axisValues[index] = Mathf.Clamp(value, 0, maxLevel);
            engine.SetAxis(index, _axisValues[index], _quant);
        }

        private void DrawParameters()
        {
            GatherParameters();
            Section($"Parameters ({_parameters.Count})");

            if (_parameters.Count == 0)
            {
                GUILayout.Label("No ParameterDefinition referenced by any ContextPreset.");
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Slider max:", GUILayout.Width(70));
            string maxStr = GUILayout.TextField(parameterSliderMax.ToString("0.###"), GUILayout.Width(60));
            if (float.TryParse(maxStr, out float parsedMax) && parsedMax > 0f) parameterSliderMax = parsedMax;
            GUILayout.EndHorizontal();

            foreach (var p in _parameters)
            {
                if (p == null) continue;
                float cur = p.RawValue;
                string type = p is IntParameter ? "int" : "float";

                GUILayout.Space(4);
                GUILayout.Label($"{p.name}  ({type})   = {cur:0.###}");

                GUILayout.BeginHorizontal();
                float sliderVal = GUILayout.HorizontalSlider(cur, 0f, parameterSliderMax, GUILayout.Width(300));
                if (!Mathf.Approximately(sliderVal, cur)) SetParameter(p, sliderVal);

                if (GUILayout.Button("0", GUILayout.Width(28))) SetParameter(p, 0f);
                if (GUILayout.Button("½·max", GUILayout.Width(52))) SetParameter(p, parameterSliderMax * 0.5f);
                if (GUILayout.Button("max", GUILayout.Width(40))) SetParameter(p, parameterSliderMax);
                GUILayout.EndHorizontal();
            }
        }

        private void SetParameter(ParameterDefinition p, float value)
        {
            switch (p)
            {
                case FloatParameter fp: fp.Value = value; break;
                case IntParameter ip: ip.Value = Mathf.RoundToInt(value); break;
            }
        }

        private void GatherParameters()
        {
            _parameters.Clear();
            foreach (var space in Project.spaces)
            {
                ContextPreset preset = space?.contextPreset;
                if (preset == null) continue;
                foreach (var binding in preset.axisBindings)
                {
                    if (binding?.rules == null) continue;
                    foreach (var rule in binding.rules)
                        if (rule?.source != null && !_parameters.Contains(rule.source))
                            _parameters.Add(rule.source);
                }
            }
        }

        private void DrawLayers()
        {
            string current = engine.GetCurrentSpace();
            MusicSpaceDefinition space = string.IsNullOrEmpty(current) ? null : Project.GetSpace(current);
            Section("Layers — current space");

            if (space == null)
            {
                GUILayout.Label("No current space.");
                return;
            }

            var active = new HashSet<string>(engine.GetActiveLayerKeys());
            foreach (var layer in space.layers)
            {
                if (layer == null) continue;
                string key = $"{space.spaceId}/{layer.layerId}";
                bool on = active.Contains(key);
                string mode = layer.volumeMode == LayerVolumeMode.VolumeEnvelope
                    ? $"env→{Or(layer.volumeEnvelope?.axisId, "?")}"
                    : "stepped";

                Color prev = GUI.color;
                GUI.color = on ? Color.green : new Color(0.6f, 0.6f, 0.6f);
                GUILayout.Label($"{(on ? "♪" : "·")} {layer.layerId}   [{mode}]", RichLabel());
                GUI.color = prev;
            }
        }

        // ------------------------------------------------------------------ helpers

        private static void Section(string title)
        {
            GUILayout.Space(10);
            GUILayout.Label($"<b>{title}</b>", RichLabel());
        }

        private static GUIStyle _rich;
        private static GUIStyle RichLabel()
        {
            _rich ??= new GUIStyle(GUI.skin.label) { richText = true };
            return _rich;
        }

        private static string Or(string s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;

        private static string CoordToString(int[] coord)
            => (coord == null || coord.Length == 0) ? "-" : string.Join(", ", coord);
    }
}
