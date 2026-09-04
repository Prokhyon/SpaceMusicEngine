using System.Collections.Generic;
using Loris.AudioEngine;
using UnityEngine;

/// <summary>
/// Runtime overlay that displays adaptive music engine state.
/// Attach to any GameObject in the scene. Assign AudioEngineBehaviour
/// and the three parameter assets in the Inspector.
/// Toggle with the key set in <see cref="toggleKey"/> (default: F9).
/// </summary>
[AddComponentMenu("SpaceMusicEngine Demo/Debug/Audio Engine Debug UI")]
public class AudioEngineDebugUI : MonoBehaviour
{
    [Header("Engine")]
    [SerializeField] private AudioEngineBehaviour audioEngine;

    [Header("Parameters")]
    [SerializeField] private FloatParameter healthParameter;
    [SerializeField] private FloatParameter speedParameter;
    [SerializeField] private IntParameter enemyCountParameter;
    [SerializeField] private FloatParameter awarenessParameter;

    [Tooltip("Any other parameters to display (combat etc.): drag in PlayerTookDamage, "
           + "PlayerDealtDamage, EnemyHealth, BossDetected, PlayerUnderAttack… Each shows its "
           + "asset name and current value automatically.")]
    [SerializeField] private List<ParameterDefinition> extraParameters = new List<ParameterDefinition>();

    [Header("Display")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F9;
    [SerializeField] private int fontSize = 14;

    private bool _visible = true;

    // Reused to avoid allocation in OnGUI.
    private readonly List<string> _layerLines = new List<string>();

    private GUIStyle _boxStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _headerStyle;
    private GUIStyle _goodStyle;
    private GUIStyle _warnStyle;

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            _visible = !_visible;
    }

    private void OnGUI()
    {
        if (!_visible) return;

        InitStyles();

        AudioEngineFacade engine = audioEngine != null ? audioEngine.Engine : null;

        float panelW = 340f;
        float x = 10f;
        float y = 10f;
        float lineH = fontSize + 6f;
        float pad = 8f;

        // Measure height dynamically
        int lineCount = 19;
        if (engine != null) lineCount += engine.GetAxisCount() + 2;
        lineCount += extraParameters.Count;
        float panelH = pad * 2 + lineCount * lineH;

        GUI.Box(new Rect(x, y, panelW, panelH), GUIContent.none, _boxStyle);

        float cx = x + pad;
        float cy = y + pad;

        // Header
        GUI.Label(new Rect(cx, cy, panelW - pad * 2, lineH), "AUDIO ENGINE DEBUG  [F9]", _headerStyle);
        cy += lineH + 2f;
        DrawDivider(cx, cy, panelW - pad * 2);
        cy += 6f;

        // Parameters section
        GUI.Label(new Rect(cx, cy, panelW, lineH), "PARAMETERS", _headerStyle);
        cy += lineH;

        float health = healthParameter != null ? healthParameter.RawValue : -1f;
        float speed  = speedParameter  != null ? speedParameter.RawValue  : -1f;
        int   enemies = enemyCountParameter != null ? (int)enemyCountParameter.RawValue : -1;

        DrawBar(cx, cy, panelW - pad * 2, lineH, "Health",
            health >= 0 ? $"{health:P0}" : "—",
            health, health < 0.3f ? _warnStyle : _goodStyle);
        cy += lineH;

        DrawBar(cx, cy, panelW - pad * 2, lineH, "Speed",
            speed >= 0 ? $"{speed:F1} u/s" : "—",
            Mathf.Clamp01(speed / 8f), _goodStyle);
        cy += lineH;

        GUI.Label(new Rect(cx, cy, panelW - pad * 2, lineH),
            $"  Enemies nearby:  {(enemies >= 0 ? enemies.ToString() : "—")}",
            enemies > 0 ? _warnStyle : _labelStyle);
        cy += lineH;

        bool detected = awarenessParameter != null && awarenessParameter.RawValue > 0.01f;
        string awarenessLabel = awarenessParameter == null ? "  Detection:  —"
                              : detected              ? "  Detection:  DETECTED"
                                                      : "  Detection:  Undetected";
        GUI.Label(new Rect(cx, cy, panelW - pad * 2, lineH), awarenessLabel,
            detected ? _warnStyle : _labelStyle);
        cy += lineH;

        // Extra parameters (combat, etc.) — drag the assets into the Inspector list.
        for (int i = 0; i < extraParameters.Count; i++)
        {
            ParameterDefinition p = extraParameters[i];
            if (p == null) continue;

            float v = p.RawValue;
            string valStr = p is IntParameter ? ((int)v).ToString() : v.ToString("F2");
            DrawBar(cx, cy, panelW - pad * 2, lineH, p.name, valStr,
                Mathf.Clamp01(v), v > 0.5f ? _warnStyle : _goodStyle);
            cy += lineH;
        }

        cy += 4f;
        DrawDivider(cx, cy, panelW - pad * 2);
        cy += 6f;

        // Engine state
        GUI.Label(new Rect(cx, cy, panelW, lineH), "MUSIC ENGINE", _headerStyle);
        cy += lineH;

        if (engine == null)
        {
            GUI.Label(new Rect(cx, cy, panelW - pad * 2, lineH), "  AudioEngineBehaviour not assigned!", _warnStyle);
            return;
        }

        string currentSpace   = engine.GetCurrentSpace();
        string requestedSpace = engine.GetRequestedSpace();
        bool transitioning    = currentSpace != requestedSpace && !string.IsNullOrEmpty(requestedSpace);

        GUI.Label(new Rect(cx, cy, panelW - pad * 2, lineH),
            $"  Space:  {(string.IsNullOrEmpty(currentSpace) ? "<none>" : currentSpace)}",
            string.IsNullOrEmpty(currentSpace) ? _warnStyle : _goodStyle);
        cy += lineH;

        if (transitioning)
        {
            GUI.Label(new Rect(cx, cy, panelW - pad * 2, lineH),
                $"  → Pending: {requestedSpace}", _warnStyle);
            cy += lineH;
        }

        // Axes / coordinate vector
        int axisCount = engine.GetAxisCount();
        int[] coord   = engine.GetCurrentCoordinate();

        if (axisCount == 0)
        {
            GUI.Label(new Rect(cx, cy, panelW - pad * 2, lineH), "  No axes (space not active yet)", _warnStyle);
            cy += lineH;
        }
        else
        {
            GUI.Label(new Rect(cx, cy, panelW, lineH), "  Axes (music vector):", _labelStyle);
            cy += lineH;
            for (int i = 0; i < axisCount; i++)
            {
                string axisId   = engine.GetAxisId(i);
                int    levels   = engine.GetAxisLevelCount(i);
                int    curLevel = (coord != null && i < coord.Length) ? coord[i] : 0;
                float  norm     = levels > 0 ? (float)curLevel / levels : 0f;
                DrawBar(cx + 8, cy, panelW - pad * 2 - 8, lineH,
                    axisId, $"{curLevel}/{levels}", norm, _goodStyle);
                cy += lineH;
            }
        }

        cy += 4f;
        DrawDivider(cx, cy, panelW - pad * 2);
        cy += 6f;

        // Playback
        GUI.Label(new Rect(cx, cy, panelW, lineH), "PLAYBACK", _headerStyle);
        cy += lineH;

        double loopDur = engine.GetLoopDurationSeconds();
        double loopPos = engine.GetLoopPositionSeconds();
        float  loopNorm = loopDur > 0 ? (float)(loopPos / loopDur) : 0f;

        DrawBar(cx, cy, panelW - pad * 2, lineH, "Loop",
            loopDur > 0 ? $"{loopPos:F1} / {loopDur:F1} s" : "stopped",
            loopNorm, _goodStyle);
        cy += lineH;

        GUI.Label(new Rect(cx, cy, panelW - pad * 2, lineH),
            $"  Active voices:  {engine.GetActiveVoiceCount()}", _labelStyle);
        cy += lineH;

        GUI.Label(new Rect(cx, cy, panelW - pad * 2, lineH),
            $"  Transition:  {(engine.IsTransitioning ? "IN PROGRESS" : "-")}",
            engine.IsTransitioning ? _warnStyle : _labelStyle);
        cy += lineH;

        GUI.Label(new Rect(cx, cy, panelW - pad * 2, lineH), "  Active layers:", _labelStyle);
        cy += lineH;

        _layerLines.Clear();
        foreach (string key in engine.GetActiveLayerKeys())
            _layerLines.Add(key);

        if (_layerLines.Count == 0)
        {
            GUI.Label(new Rect(cx + 8, cy, panelW - pad * 2 - 8, lineH), "<none>", _warnStyle);
        }
        else
        {
            foreach (string layer in _layerLines)
            {
                GUI.Label(new Rect(cx + 8, cy, panelW - pad * 2 - 8, lineH), layer, _goodStyle);
                cy += lineH;
            }
        }
    }

    private void DrawBar(float x, float y, float w, float h, string label, string valueStr, float norm, GUIStyle valueStyle)
    {
        float labelW = 90f;
        float barW   = w - labelW - 50f;
        float valW   = 50f;

        GUI.Label(new Rect(x, y, labelW, h), $"  {label}", _labelStyle);

        // Background
        GUI.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        GUI.DrawTexture(new Rect(x + labelW, y + 3, barW, h - 6), Texture2D.whiteTexture);

        // Fill
        GUI.color = norm < 0.3f
            ? new Color(0.9f, 0.3f, 0.2f, 0.9f)
            : new Color(0.2f, 0.8f, 0.4f, 0.9f);
        GUI.DrawTexture(new Rect(x + labelW, y + 3, barW * Mathf.Clamp01(norm), h - 6), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(x + labelW + barW + 2, y, valW, h), valueStr, valueStyle);
    }

    private void DrawDivider(float x, float y, float w)
    {
        GUI.color = new Color(1f, 1f, 1f, 0.15f);
        GUI.DrawTexture(new Rect(x, y, w, 1f), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void InitStyles()
    {
        if (_boxStyle != null) return;

        _boxStyle = new GUIStyle(GUI.skin.box);
        _boxStyle.normal.background = MakeTex(1, 1, new Color(0.05f, 0.05f, 0.1f, 0.88f));
        _boxStyle.border = new RectOffset(4, 4, 4, 4);

        _labelStyle = new GUIStyle(GUI.skin.label);
        _labelStyle.fontSize  = fontSize;
        _labelStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

        _headerStyle = new GUIStyle(_labelStyle);
        _headerStyle.fontStyle = FontStyle.Bold;
        _headerStyle.normal.textColor = Color.white;

        _goodStyle = new GUIStyle(_labelStyle);
        _goodStyle.normal.textColor = new Color(0.4f, 1f, 0.5f);

        _warnStyle = new GUIStyle(_labelStyle);
        _warnStyle.normal.textColor = new Color(1f, 0.5f, 0.3f);
    }

    private static Texture2D MakeTex(int w, int h, Color col)
    {
        var tex = new Texture2D(w, h);
        var pix = new Color[w * h];
        for (int i = 0; i < pix.Length; i++) pix[i] = col;
        tex.SetPixels(pix);
        tex.Apply();
        return tex;
    }
}
