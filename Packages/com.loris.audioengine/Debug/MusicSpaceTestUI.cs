using System;
using System.Collections.Generic;
using UnityEngine;

namespace Loris.AudioEngine
{
    /// <summary>
    /// Panneau de test des espaces musicaux (IMGUI), disposé en trois colonnes pour
    /// tenir à l'écran sans défilement.
    ///
    /// Reprend le principe de MusicDebugUI pour écouter les espaces et leurs
    /// transitions (liste d'espaces cliquable + quantification), mais remplace les
    /// sliders de paramètres génériques par des contrôles dérivés du ContextPreset
    /// de l'espace courant :
    ///
    ///  - un paramètre à seuils devient un bouton (ou une rangée de boutons quand il
    ///    a plus de deux paliers). Le bouton actif écrit une valeur qui franchit le
    ///    seuil : seuil de vitesse à 3 → le bouton met PlayerSpeed à 5 ;
    ///  - un paramètre continu — mode Curve, ou axe qui pilote des couches en
    ///    VolumeEnvelope (Portail, Courtyard) — devient un slider unique de proximité ;
    ///  - un axe déclaré en « ligne unique » (axisOverrides) regroupe tous ses
    ///    paramètres en une seule rangée qui pilote l'axe de bout en bout. C'est le
    ///    mode voulu pour la Tension des espaces de combat : la détection n'y est
    ///    que le premier palier, et les paramètres inversés (santé du boss, pleine
    ///    au repos) sont résolus automatiquement.
    ///
    /// Les valeurs sont écrites dans les ParameterDefinition ; c'est le MusicBrain de
    /// la scène qui évalue le preset et pilote les axes, exactement comme en jeu.
    /// </summary>
    [AddComponentMenu("SpaceMusicEngine/Debug/Space Player")]
    public class MusicSpaceTestUI : MonoBehaviour
    {
        /// <summary>Libellé lisible pour un paramètre, à la place du nom de l'asset.</summary>
        [Serializable]
        public class ParameterLabel
        {
            public ParameterDefinition parameter;
            public string label;
        }

        /// <summary>
        /// Personnalisation de la ligne d'un axe : regroupement en une seule rangée,
        /// libellé de la ligne et libellés des boutons.
        /// </summary>
        [Serializable]
        public class AxisOverride
        {
            [Tooltip("Espace concerné. Laisser vide pour l'appliquer à tous les espaces.")]
            public string spaceId;

            [Tooltip("axisId concerné, tel qu'il est écrit dans l'espace et le preset (ex. Tension).")]
            public string axisName;

            [Tooltip("Regroupe tous les paramètres de cet axe en une seule rangée de boutons " +
                     "qui pilote l'axe (au lieu d'une ligne par paramètre).")]
            public bool singleRow;

            [Tooltip("Libellé de la ligne. Vide = nom de l'axe ou du paramètre.")]
            public string label;

            [Tooltip("Libellés des boutons, dans l'ordre des paliers (du plus bas au plus haut). " +
                     "Une entrée vide garde le libellé par défaut.")]
            public List<string> stepLabels = new List<string>();
        }

        /// <summary>Un projet sélectionnable, avec le nom affiché sur son bouton.</summary>
        [Serializable]
        public class ProjectEntry
        {
            public MusicProject project;

            [Tooltip("Nom affiché sur le bouton. Vide = nom de l'asset.")]
            public string label;
        }

        [SerializeField] private AudioEngineBehaviour audioEngineBehaviour;

        [Header("Projets")]
        [Tooltip("Projets sélectionnables par les boutons en haut du panneau. " +
                 "Le projet assigné à l'AudioEngineBehaviour est ajouté automatiquement.")]
        [SerializeField] private List<ProjectEntry> projects = new List<ProjectEntry>();

        [Header("Panneau")]
        [Tooltip("Le panneau est disposé comme sur un écran de cette hauteur, puis mis à " +
                 "l'échelle de l'écran réel : sur un écran haute résolution tout reste lisible. " +
                 "Baisser la valeur agrandit l'interface, 0 la désactive.")]
        [SerializeField] private float referenceHeight = 1080f;

        [Tooltip("Marge entre le panneau et le bord de l'écran.")]
        [SerializeField] private float margin = 14f;

        [Tooltip("Répartition horizontale des trois colonnes (espaces · paramètres · état).")]
        [SerializeField] private Vector3 columnWeights = new Vector3(0.28f, 0.40f, 0.32f);

        [Tooltip("Affiche / masque le panneau pendant le jeu.")]
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;

        [Header("Boutons de paramètres")]
        [Tooltip("Marge ajoutée au-dessus du dernier seuil pour les paramètres flottants non " +
                 "normalisés (seuil de vitesse à 3 → bouton actif = 5). Les paramètres entiers " +
                 "et ceux dont les seuils tiennent dans [0..1] prennent la valeur du seuil.")]
        [SerializeField] private float activationMargin = 2f;

        [Tooltip("Remet tous les paramètres à leur valeur de départ quand l'espace courant change, " +
                 "pour qu'un espace ne démarre pas avec l'état laissé par le précédent.")]
        [SerializeField] private bool resetParametersOnSpaceChange = true;

        [Header("Libellés")]
        [Tooltip("Facultatif : libellés lisibles des paramètres. Sans entrée, le nom de l'asset est utilisé.")]
        [SerializeField] private List<ParameterLabel> parameterLabels = new List<ParameterLabel>();

        [Tooltip("Facultatif : regroupement et libellés personnalisés, par espace et par axe.")]
        [SerializeField] private List<AxisOverride> axisOverrides = new List<AxisOverride>();

        [Header("Thème")]
        [Tooltip("Fond du panneau. L'écran entier est peint avec cette couleur.")]
        [SerializeField] private Color backgroundColor = new Color(0.055f, 0.058f, 0.070f, 1f);

        [Tooltip("Couleur des éléments sélectionnés et des jauges.")]
        [SerializeField] private Color accentColor = new Color(0.18f, 0.68f, 0.44f, 1f);

        [Tooltip("Couleur des états en attente (transition demandée, valeurs suspectes).")]
        [SerializeField] private Color pendingColor = new Color(0.91f, 0.70f, 0.25f, 1f);

        [Header("Visualiseur")]
        [SerializeField] private bool showSpaceGraph = true;

        [Tooltip("Hauteur réservée au graphe de l'espace, en pixels.")]
        [SerializeField] private float graphHeight = 250f;

        // ------------------------------------------------------------------ état

        private AudioEngineFacade engine;
        private MusicProject Project => audioEngineBehaviour != null ? audioEngineBehaviour.musicProject : null;

        private Vector2 _scrollLeft, _scrollMiddle, _scrollRight;

        // Taille de l'écran exprimée dans l'espace virtuel de mise en page.
        private Vector2 _viewSize;

        private QuantizationMode _quant = QuantizationMode.NextBar;
        private bool _visible = true;
        private bool _showManualAxes;
        private bool _showDiagnostics;
        private MusicProject _pendingProject;

        // Références prises au démarrage pour mesurer la dérive de l'horloge DSP.
        private double _dspReferenceTime;
        private double _realReferenceTime;

        /// <summary>
        /// Suit la vitesse de lecture réelle d'une couche : rapport entre l'avancée de sa
        /// position de lecture et l'avancée du temps DSP. Doit valoir 1,000. Une valeur
        /// stable mais fausse trahit un rééchantillonnage ; une valeur qui varie d'un tour
        /// à l'autre trahit le rebouclage.
        /// </summary>
        private class RateProbe
        {
            public bool primed;
            public double lastPosition;
            public double lastDspTime;
            public double rate = 1.0;
            public double minRate = double.MaxValue;
            public double maxRate;
            public double maxPhaseError;
            public int loops;
        }

        private readonly Dictionary<string, RateProbe> _probes = new Dictionary<string, RateProbe>();

        // Thème : skin dérivée de la skin par défaut, plus les textures unies qu'elle utilise.
        private GUISkin _skin;
        private GUIStyle _selectedButton;
        private GUIStyle _graphLabel;
        private GUIStyle _graphLabelLeft;
        private GUIStyle _pendingButton;
        private Texture2D _texBackground, _texPanel, _texTrack, _texAccent, _texPending, _texGraph;
        private readonly List<Texture2D> _themeTextures = new List<Texture2D>();

        private string _accentHex = "8AE0AE";
        private string _dimHex = "8A8D95";

        private enum ControlKind { Steps, Slider, AxisLadder }

        /// <summary>Un axe alimenté par un paramètre, avec la règle qui fait la conversion.</summary>
        private class AxisEffect
        {
            public string axisName;
            public MappingRule rule;
        }

        /// <summary>Valeur à écrire dans un paramètre pour atteindre un palier.</summary>
        private struct ParamWrite
        {
            public ParameterDefinition parameter;
            public float value;
        }

        /// <summary>Un palier d'une rangée qui pilote un axe de bout en bout.</summary>
        private class LadderStep
        {
            public float axisValue;
            public int level;
            public string label;
            public readonly List<ParamWrite> writes = new List<ParamWrite>();
        }

        private class Control
        {
            public ControlKind kind;
            public string label;

            // --- Steps / Slider : un paramètre ---
            public ParameterDefinition source;
            public MappingRule primary;
            public bool proximity;
            public float min, max;
            public readonly List<ThresholdEntry> steps = new List<ThresholdEntry>();
            public readonly List<AxisEffect> affects = new List<AxisEffect>();

            // --- AxisLadder : un axe, tous ses paramètres ---
            public string axisName;
            public AxisBinding binding;
            public readonly List<LadderStep> ladder = new List<LadderStep>();

            /// <summary>Libellés personnalisés, indexés par palier.</summary>
            public List<string> stepLabels;
        }

        private readonly List<Control> _controls = new List<Control>();
        private readonly HashSet<string> _envelopeAxes = new HashSet<string>();
        private readonly HashSet<string> _activeKeys = new HashSet<string>();

        // Espace pour lequel _controls a été construit.
        private string _controlsSpaceId;
        private ContextPreset _controlsPreset;
        private bool _controlsDirty = true;

        // Valeurs initiales des paramètres, pour la remise à zéro.
        private readonly Dictionary<ParameterDefinition, float> _initialValues =
            new Dictionary<ParameterDefinition, float>();

        private const float Epsilon = 1e-4f;

        // ------------------------------------------------------------------ cycle de vie

        private void Awake()
        {
            ResolveEngine();

            if (Project != null && !ProjectsContain(Project))
                projects.Insert(0, new ProjectEntry { project = Project });

            foreach (var entry in projects)
                CaptureInitialParameterValues(entry?.project);

            _dspReferenceTime = AudioSettings.dspTime;
            _realReferenceTime = Time.realtimeSinceStartupAsDouble;
        }

        private void Update()
        {
            if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
                _visible = !_visible;

            // Le changement de projet est différé hors du pass IMGUI : il renouvelle
            // la façade et la liste des espaces, donc le nombre de contrôles dessinés.
            if (_pendingProject != null)
            {
                SwitchProject(_pendingProject);
                _pendingProject = null;
            }

            TrackPlaybackRates();
        }

        /// <summary>
        /// Échantillonne, une fois par frame, la position de lecture de chaque couche pour
        /// en déduire sa vitesse réelle et son écart de phase maximal.
        /// </summary>
        private void TrackPlaybackRates()
        {
            if (engine == null) return;

            MusicSpaceDefinition space = CurrentSpace();
            if (space == null) return;

            double now = AudioSettings.dspTime;
            double loopPosition = engine.GetLoopPositionSeconds();
            double loopDuration = engine.GetLoopDurationSeconds();

            foreach (var layer in space.layers)
            {
                if (layer?.clip == null) continue;

                string key = $"{space.spaceId}/{layer.layerId}";
                double position = engine.GetLayerPlaybackPositionSeconds(key);
                if (position < 0.0)
                {
                    _probes.Remove(key);
                    continue;
                }

                if (!_probes.TryGetValue(key, out RateProbe probe))
                {
                    probe = new RateProbe();
                    _probes[key] = probe;
                }

                // Écart de phase : une couche calée lit à la position de boucle de l'horloge.
                double phase = position - loopPosition;
                if (loopDuration > 0.0)
                    phase -= Math.Floor(phase / loopDuration + 0.5) * loopDuration;
                probe.maxPhaseError = Math.Max(probe.maxPhaseError, Math.Abs(phase));

                if (!probe.primed)
                {
                    probe.lastPosition = position;
                    probe.lastDspTime = now;
                    probe.primed = true;
                    continue;
                }

                double elapsedDsp = now - probe.lastDspTime;
                if (elapsedDsp < 0.5) continue;

                double advanced = position - probe.lastPosition;
                if (advanced < 0.0)
                {
                    // La position est repartie en arrière : tour de boucle, pas une mesure.
                    probe.loops++;
                }
                else if (elapsedDsp > 0.0)
                {
                    double rate = advanced / elapsedDsp;
                    probe.rate = rate;
                    probe.minRate = Math.Min(probe.minRate, rate);
                    probe.maxRate = Math.Max(probe.maxRate, rate);
                }

                probe.lastPosition = position;
                probe.lastDspTime = now;
            }
        }

        private void ResolveEngine()
        {
            if (audioEngineBehaviour == null)
                audioEngineBehaviour = FindAnyObjectByType<AudioEngineBehaviour>();
            if (audioEngineBehaviour != null)
                engine = audioEngineBehaviour.Engine;
        }

        private bool ProjectsContain(MusicProject project)
        {
            foreach (var entry in projects)
                if (entry != null && entry.project == project)
                    return true;
            return false;
        }

        private void CaptureInitialParameterValues(MusicProject project)
        {
            if (project == null) return;

            foreach (var space in project.spaces)
            {
                ContextPreset preset = space?.contextPreset;
                if (preset == null) continue;
                foreach (var binding in preset.axisBindings)
                {
                    if (binding?.rules == null) continue;
                    foreach (var rule in binding.rules)
                        if (rule?.source != null && !_initialValues.ContainsKey(rule.source))
                            _initialValues[rule.source] = rule.source.RawValue;
                }
            }
        }

        private void ResetAllParameters()
        {
            foreach (var kv in _initialValues)
                SetParameter(kv.Key, kv.Value);
        }

        /// <summary>
        /// Ramène l'espace à son état neutre : chaque axe piloté par son preset repart à 0.
        ///
        /// Ce n'est pas la même chose que « tous les paramètres à zéro ». Un paramètre inversé
        /// — la santé du boss, dont 0 signifie mort, donc tension maximale — doit au contraire
        /// être remis à 1 pour que l'axe retombe à 0. La valeur neutre est donc déduite des
        /// seuils de chaque règle, exactement comme le premier palier d'une rangée d'axe.
        /// </summary>
        private void ResetParametersForSpace(MusicSpaceDefinition space)
        {
            // 1. Tout revient d'abord à la valeur d'origine des assets, y compris les
            //    paramètres qui n'appartiennent pas à cet espace.
            ResetAllParameters();

            ContextPreset preset = space?.contextPreset;
            if (preset == null) return;

            // 2. Puis les paramètres de l'espace courant sont amenés sur leur palier neutre.
            foreach (var binding in preset.axisBindings)
            {
                if (binding?.rules == null) continue;

                foreach (var rule in binding.rules)
                {
                    if (rule?.source == null) continue;

                    List<ThresholdEntry> steps = SortedThresholds(rule);
                    // Une règle continue (courbe, proximité) n'a pas de palier : sa valeur
                    // d'origine fait déjà l'affaire.
                    if (steps.Count == 0) continue;

                    int neutral = BestStepForTarget(steps, 0f);
                    if (neutral >= 0)
                        SetParameter(rule.source, ValueForStep(steps, neutral, rule.source is IntParameter));
                }
            }
        }

        /// <summary>Charge un autre MusicProject à chaud : le moteur est reconstruit, tout est réinitialisé.</summary>
        private void SwitchProject(MusicProject project)
        {
            if (project == null || audioEngineBehaviour == null || project == Project) return;

            audioEngineBehaviour.LoadProject(project);
            engine = audioEngineBehaviour.Engine;

            CaptureInitialParameterValues(project);
            ResetAllParameters();

            _controlsDirty = true;
        }

        // ------------------------------------------------------------------ rendu

        private void OnGUI()
        {
            if (!_visible) return;

            if (audioEngineBehaviour == null) ResolveEngine();
            else engine = audioEngineBehaviour.Engine;

            if (_skin == null) BuildTheme();

            // Toute l'interface est composée dans un espace virtuel d'une hauteur de
            // référence, puis agrandie par la matrice GUI. IMGUI applique l'inverse de
            // cette matrice aux positions de souris, les clics restent donc justes.
            float scale = UiScale();
            _viewSize = new Vector2(Screen.width / scale, Screen.height / scale);

            GUISkin previousSkin = GUI.skin;
            Matrix4x4 previousMatrix = GUI.matrix;

            GUI.skin = _skin;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            try { DrawPanel(); }
            finally
            {
                GUI.skin = previousSkin;
                GUI.matrix = previousMatrix;
            }
        }

        /// <summary>
        /// Facteur d'agrandissement de l'interface. Jamais inférieur à 1 : sur un écran plus
        /// petit que la référence, on garde la taille native plutôt que de tout rétrécir.
        /// </summary>
        private float UiScale()
            => referenceHeight > 1f ? Mathf.Max(1f, Screen.height / referenceHeight) : 1f;

        private void DrawPanel()
        {
            // Fond opaque plein écran : sans lui, le panneau se lit par-dessus la vue de
            // jeu et les libellés deviennent illisibles selon ce qu'il y a derrière.
            GUI.DrawTexture(new Rect(0f, 0f, _viewSize.x, _viewSize.y), _texBackground);

            Rect area = new Rect(margin, margin, _viewSize.x - 2f * margin, _viewSize.y - 2f * margin);
            GUILayout.BeginArea(area, GUI.skin.box);

            DrawHeader();

            if (audioEngineBehaviour == null)
            {
                GUILayout.Label("AudioEngineBehaviour : introuvable dans la scène.");
                GUILayout.EndArea();
                return;
            }
            if (engine == null)
            {
                GUILayout.Label("AudioEngineFacade : null (assigne un MusicProject).");
                GUILayout.EndArea();
                return;
            }
            if (Project == null)
            {
                GUILayout.Label("MusicProject : null.");
                GUILayout.EndArea();
                return;
            }

            MusicSpaceDefinition current = CurrentSpace();
            SyncControls(current);

            // Trois colonnes : espaces · paramètres · état du moteur.
            float inner = area.width - 26f;
            float weightSum = Mathf.Max(0.01f, columnWeights.x + columnWeights.y + columnWeights.z);
            float wLeft = inner * columnWeights.x / weightSum;
            float wMiddle = inner * columnWeights.y / weightSum;
            float wRight = inner - wLeft - wMiddle;

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(wLeft));
            _scrollLeft = GUILayout.BeginScrollView(_scrollLeft, GUILayout.Width(wLeft));
            DrawSpaces();
            DrawQuantizationSelector();
            DrawTransport();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(wMiddle));
            _scrollMiddle = GUILayout.BeginScrollView(_scrollMiddle, GUILayout.Width(wMiddle));
            DrawSpaceControls(current);
            DrawSpaceGraph(current);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(wRight));
            _scrollRight = GUILayout.BeginScrollView(_scrollRight, GUILayout.Width(wRight));
            DrawStatus();
            DrawAxes();
            DrawLayers(current);
            DrawDiagnostics(current);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private MusicSpaceDefinition CurrentSpace()
        {
            string id = engine != null ? engine.GetCurrentSpace() : null;
            return string.IsNullOrEmpty(id) || Project == null ? null : Project.GetSpace(id);
        }

        // ------------------------------------------------------------------ sections

        /// <summary>Titre + sélection du MusicProject.</summary>
        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Espaces musicaux — test</b>", RichLabel(), GUILayout.Width(190));

            if (projects.Count > 0)
            {
                GUILayout.Label("Projet :", GUILayout.Width(52));
                foreach (var entry in projects)
                {
                    if (entry?.project == null) continue;

                    bool isCurrent = entry.project == Project;
                    if (GUILayout.Button(Or(entry.label, entry.project.name),
                                         isCurrent ? _selectedButton : GUI.skin.button,
                                         GUILayout.Height(22), GUILayout.MinWidth(140)))
                        _pendingProject = entry.project;
                }
            }

            GUILayout.FlexibleSpace();
            if (toggleKey != KeyCode.None)
                GUILayout.Label($"<i>{toggleKey} pour masquer</i>", RichLabel(), GUILayout.Width(140));
            GUILayout.EndHorizontal();
        }

        private void DrawStatus()
        {
            Section("Runtime");

            string current = engine.GetCurrentSpace();
            string requested = engine.GetRequestedSpace();

            GUILayout.Label($"Espace : {Or(current, "-")}"
                          + (current != requested ? $"  → <color=#E8B341>{Or(requested, "silence")}</color>" : ""),
                            RichLabel());
            GUILayout.Label($"Coordonnée : [{CoordToString(engine.GetCurrentCoordinate())}]   ·   "
                          + $"voix : {engine.GetActiveVoiceCount()}");

            bool clockRunning = engine.IsClockRunning;
            double dur = engine.GetLoopDurationSeconds();
            double pos = engine.GetLoopPositionSeconds();
            if (clockRunning)
            {
                MetricPosition lm = engine.GetLoopMetricPosition();
                GUILayout.Label($"Boucle : {pos:F2}s / {dur:F2}s   ·   mesure {lm.bar}.{lm.beat}");
            }
            else
            {
                GUILayout.Label("Boucle : —");
            }
            DrawBar(clockRunning && dur > 0.0 ? (float)(pos / dur) : 0f);

            if (engine.IsTransitioning)
            {
                MetricPosition tm = engine.GetTransitionMetricPosition();
                double el = engine.GetTransitionElapsedSeconds();
                double tot = engine.GetTransitionTotalSeconds();
                double entry = engine.GetTransitionEntryCountdown();
                string barBeat = engine.TransitionHasTrack ? $"mesure {tm.bar}.{tm.beat}" : "directe";
                string elapsed = el < 0.0 ? $"départ dans {-el:F2}s" : $"{el:F2}s / {tot:F2}s";
                GUILayout.Label($"<color=#E8B341>Transition :</color> {elapsed}   ·   {barBeat}", RichLabel());
                GUILayout.Label($"arrivée dans {entry:F2}s");
                DrawBar(tot > 0.0 ? Mathf.Clamp01((float)(el / tot)) : 0f);
            }
            else
            {
                GUILayout.Label("Transition : —");
                GUILayout.Label(" ");
                DrawBar(0f);
            }
        }

        /// <summary>
        /// La quantification ne sert qu'aux départs sans TransitionDefinition : quand une
        /// transition existe pour le couple d'espaces, c'est elle qui décide de tout
        /// (déclenchement, sortie de la source, point d'arrivée) et ce réglage est ignoré.
        /// </summary>
        private void DrawQuantizationSelector()
        {
            Section("Quantification du fondu");
            GUILayout.Label("<i>utilisée pour les ≈ ci-dessus et le forçage manuel des axes ;"
                          + " ignorée quand une transition est définie</i>", RichLabel());

            foreach (QuantizationMode mode in Enum.GetValues(typeof(QuantizationMode)))
            {
                bool on = _quant == mode;
                bool next = GUILayout.Toggle(on, QuantizationLabel(mode), GUI.skin.button, GUILayout.Height(22));
                if (next && !on) _quant = mode;
            }
        }

        private static string QuantizationLabel(QuantizationMode mode) => mode switch
        {
            QuantizationMode.Immediate => "Immédiat",
            QuantizationMode.NextBeat => "Prochain temps",
            QuantizationMode.NextBar => "Prochaine mesure",
            QuantizationMode.NextLoop => "Prochaine boucle",
            _ => mode.ToString()
        };

        private void DrawSpaces()
        {
            Section("Espaces — cliquer pour transitionner");
            GUILayout.Label("<i>⇢ transition définie   ·   ≈ fondu quantifié</i>", RichLabel());

            string current = engine.GetCurrentSpace();
            string requested = engine.GetRequestedSpace();

            foreach (var space in Project.spaces)
            {
                if (space == null || string.IsNullOrEmpty(space.spaceId)) continue;

                bool isCurrent = space.spaceId == current;
                bool isRequested = space.spaceId == requested && !isCurrent;

                GUIStyle style = isCurrent ? _selectedButton
                               : isRequested ? _pendingButton
                               : GUI.skin.button;

                // Ce qui se passera au clic depuis l'espace courant : transition dédiée
                // (le réglage de quantification ne s'applique pas) ou fondu quantifié.
                string route = isCurrent
                    ? "●"
                    : Project.GetTransition(current ?? "", space.spaceId) != null ? "⇢" : "≈";

                string tag = isRequested ? "  ○" : "";
                double loopLen = space.beatsPerBar * space.barsPerLoop * (60.0 / Mathf.Max(1f, space.bpm));
                if (GUILayout.Button($"{route} {space.spaceId}{tag}"
                                   + $"\n<i>{space.bpm:0} bpm · {space.barsPerLoop} mes. · {loopLen:F1}s</i>",
                                     style, GUILayout.Height(38)))
                    engine.SetSpace(space.spaceId, _quant);
            }
        }

        private void DrawTransport()
        {
            Section("Transport");
            if (GUILayout.Button("Stop")) engine.StopPlayback();
            if (GUILayout.Button("Reprendre au début")) engine.RestartCurrentFromBeginning();
            if (GUILayout.Button("Réinitialiser les paramètres")) ResetParametersForSpace(CurrentSpace());
        }

        /// <summary>Contrôles dérivés du ContextPreset de l'espace courant.</summary>
        private void DrawSpaceControls(MusicSpaceDefinition space)
        {
            Section($"Paramètres — {(space != null ? space.spaceId : "aucun espace")}");

            if (space == null)
            {
                GUILayout.Label("Aucun espace courant : choisis un espace à gauche.");
                return;
            }
            if (space.contextPreset == null)
            {
                GUILayout.Label("Cet espace n'a pas de ContextPreset : rien à piloter.");
                return;
            }
            if (_controls.Count == 0)
            {
                GUILayout.Label("Le ContextPreset de cet espace ne référence aucun paramètre.");
                return;
            }

            foreach (var control in _controls)
            {
                GUILayout.Space(8);
                switch (control.kind)
                {
                    case ControlKind.AxisLadder:
                        DrawAxisLadder(space, control);
                        break;
                    case ControlKind.Slider:
                        DrawParameterHeader(space, control);
                        DrawProximitySlider(control);
                        break;
                    default:
                        DrawParameterHeader(space, control);
                        DrawStepButtons(control);
                        break;
                }
            }
        }

        private void DrawParameterHeader(MusicSpaceDefinition space, Control control)
        {
            float raw = control.source.RawValue;
            string type = control.source is IntParameter ? "int" : "float";

            GUILayout.Label($"<b>{control.label}</b>   <i>({control.source.name} · {type})</i>"
                          + $"   = {FormatValue(control.source, raw)}"
                          + (control.proximity ? "   <i>· couches graduelles</i>" : ""),
                            RichLabel());

            foreach (var effect in control.affects)
                GUILayout.Label("     " + AxisEffectText(space, effect.axisName, effect.rule.Evaluate()));
        }

        /// <summary>Une seule rangée pilotant un axe : chaque bouton fixe tous ses paramètres.</summary>
        private void DrawAxisLadder(MusicSpaceDefinition space, Control control)
        {
            float value = control.binding.Evaluate();

            GUILayout.Label($"<b>{control.label}</b>   <i>(axe {control.axisName}"
                          + $" · {control.binding.rules.Count} paramètre(s))</i>", RichLabel());
            GUILayout.Label("     " + AxisEffectText(space, control.axisName, value));

            int active = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < control.ladder.Count; i++)
            {
                float distance = Mathf.Abs(control.ladder[i].axisValue - value);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    active = i;
                }
            }

            GUILayout.BeginHorizontal();
            for (int i = 0; i < control.ladder.Count; i++)
            {
                LadderStep step = control.ladder[i];

                if (GUILayout.Button($"{StepLabel(control, i, step.label)}\n<i>niv. {step.level}</i>",
                                     i == active ? _selectedButton : GUI.skin.button,
                                     GUILayout.Height(40)))
                    foreach (var write in step.writes)
                        SetParameter(write.parameter, write.value);
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>Paramètre discret : un bouton par palier de seuil (bascule simple si deux paliers).</summary>
        private void DrawStepButtons(Control control)
        {
            bool isInt = control.source is IntParameter;

            if (control.steps.Count == 0)
            {
                // Aucun seuil déclaré : bascule 0 / 1 par défaut.
                bool active = control.source.RawValue > 0f;
                DrawToggleButton(control, active, active ? 0f : 1f);
                return;
            }

            int activeStep = ActiveStepIndex(control);

            if (control.steps.Count <= 2 && control.stepLabels == null)
            {
                int onIndex = control.steps.Count - 1;
                bool on = activeStep == onIndex;
                DrawToggleButton(control, on, ValueForStep(control.steps, on ? 0 : onIndex, isInt));
                return;
            }

            GUILayout.BeginHorizontal();
            for (int i = 0; i < control.steps.Count; i++)
            {
                float value = ValueForStep(control.steps, i, isInt);
                if (GUILayout.Button($"{StepLabel(control, i, FormatValue(control.source, value))}"
                                   + $"\n<i>→ {control.steps[i].result:0.##}</i>",
                                     i == activeStep ? _selectedButton : GUI.skin.button,
                                     GUILayout.Height(40)))
                    SetParameter(control.source, value);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawToggleButton(Control control, bool on, float valueOnClick)
        {
            if (GUILayout.Button($"{control.label}   {(on ? "● actif" : "○ inactif")}"
                               + $"   ·   cliquer → {FormatValue(control.source, valueOnClick)}",
                                 on ? _selectedButton : GUI.skin.button, GUILayout.Height(28)))
                SetParameter(control.source, valueOnClick);
        }

        /// <summary>
        /// Paramètre continu : slider unique. C'est le contrôle des espaces à couches
        /// graduelles (Portail, Courtyard), où il fait office de slider de proximité.
        /// </summary>
        private void DrawProximitySlider(Control control)
        {
            float raw = control.source.RawValue;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{control.min:0.##}", GUILayout.Width(30));
            float slider = GUILayout.HorizontalSlider(Mathf.Clamp(raw, control.min, control.max),
                                                      control.min, control.max);
            GUILayout.Label($"{control.max:0.##}", GUILayout.Width(30));
            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(slider, raw))
                SetParameter(control.source, slider);
        }

        private void DrawAxes()
        {
            int axisCount = engine.GetAxisCount();
            Section($"Axes ({axisCount})");

            if (axisCount == 0)
            {
                GUILayout.Label("Aucun axe dans l'espace courant.");
                return;
            }

            int[] coord = engine.GetRequestedCoordinate();
            for (int i = 0; i < axisCount; i++)
            {
                string id = Or(engine.GetAxisId(i), $"Axe {i}");
                int levelCount = engine.GetAxisLevelCount(i);
                int value = coord != null && i < coord.Length ? coord[i] : 0;

                GUILayout.BeginHorizontal();
                GUILayout.Label(_envelopeAxes.Contains(id) ? $"{id} ~" : id, GUILayout.Width(110));
                GUILayout.Label($"{value}/{levelCount}", GUILayout.Width(38));
                DrawBar(levelCount > 0 ? (float)value / levelCount : 0f);
                GUILayout.EndHorizontal();
            }

            _showManualAxes = GUILayout.Toggle(_showManualAxes, "Forçage manuel");
            if (!_showManualAxes) return;

            GUILayout.Label("<i>ignoré sur les axes pilotés par le preset</i>", RichLabel());
            for (int i = 0; i < axisCount; i++)
            {
                int levelCount = engine.GetAxisLevelCount(i);
                int value = coord != null && i < coord.Length ? coord[i] : 0;

                GUILayout.BeginHorizontal();
                GUILayout.Label(Or(engine.GetAxisId(i), $"Axe {i}"), GUILayout.Width(110));
                for (int v = 0; v <= levelCount; v++)
                {
                    if (GUILayout.Button(v.ToString(), v == value ? _selectedButton : GUI.skin.button,
                                         GUILayout.Width(26)))
                        engine.SetAxis(i, v, _quant);
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawLayers(MusicSpaceDefinition space)
        {
            Section("Couches");

            if (space == null)
            {
                GUILayout.Label("Aucun espace courant.");
                return;
            }

            _activeKeys.Clear();
            foreach (string key in engine.GetActiveLayerKeys())
                _activeKeys.Add(key);

            foreach (var layer in space.layers)
            {
                if (layer == null) continue;

                string key = $"{space.spaceId}/{layer.layerId}";
                bool playing = _activeKeys.Contains(key);

                if (layer.volumeMode == LayerVolumeMode.VolumeEnvelope && layer.volumeEnvelope != null)
                {
                    float axisValue = NormalizedAxisValue(space, layer.volumeEnvelope.axisId);
                    float volume = playing ? layer.baseVolume * layer.volumeEnvelope.Evaluate(axisValue) : 0f;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(Tint($"{(volume > 0.001f ? "♪" : "·")} {layer.layerId} ~",
                                         volume > 0.001f), RichLabel());
                    GUILayout.FlexibleSpace();
                    DrawBar(volume, 70f);
                    GUILayout.Label(volume.ToString("0.00"), GUILayout.Width(34));
                    GUILayout.EndHorizontal();
                }
                else
                {
                    GUILayout.Label(Tint($"{(playing ? "♪" : "·")} {layer.layerId}", playing), RichLabel());
                }
            }
        }

        /// <summary>
        /// Mesures utiles hors de l'éditeur, notamment en WebGL où l'horloge audio est
        /// celle de l'AudioContext du navigateur et où les clips sont ré-encodés :
        ///  - fréquence de sortie réelle comparée à celle des clips (un écart change
        ///    la hauteur et la vitesse) ;
        ///  - dérive de l'horloge DSP par rapport au temps réel ;
        ///  - position de lecture de chaque couche comparée à la position de boucle,
        ///    ce qui chiffre directement la désynchronisation.
        /// </summary>
        private void DrawDiagnostics(MusicSpaceDefinition space)
        {
            Section("Diagnostic audio");
            _showDiagnostics = GUILayout.Toggle(_showDiagnostics, "Afficher les mesures");
            if (!_showDiagnostics) return;

            AudioConfiguration config = AudioSettings.GetConfiguration();
            int outputRate = AudioSettings.outputSampleRate;
            GUILayout.Label($"Sortie : {outputRate} Hz · {config.speakerMode} · buffer {config.dspBufferSize}");

            // Dérive de l'horloge DSP : elle doit avancer à la même vitesse que le temps réel.
            double dspElapsed = AudioSettings.dspTime - _dspReferenceTime;
            double realElapsed = Time.realtimeSinceStartupAsDouble - _realReferenceTime;
            if (realElapsed > 2.0)
            {
                double ratio = dspElapsed / realElapsed;
                bool drifting = Math.Abs(ratio - 1.0) > 0.002;
                GUILayout.Label($"Horloge DSP : {dspElapsed:F1}s pour {realElapsed:F1}s réelles"
                              + $"   → ×{ratio:F4}{(drifting ? "   <color=#E8B341>⚠ dérive</color>" : "   ✓")}",
                                RichLabel());
            }
            else
            {
                GUILayout.Label("Horloge DSP : mesure en cours…");
            }

            if (space == null) return;

            double loopPosition = engine.GetLoopPositionSeconds();
            double loopDuration = engine.GetLoopDurationSeconds();
            double expectedLoop = space.beatsPerBar * space.barsPerLoop * (60.0 / Mathf.Max(1f, space.bpm));

            GUILayout.Space(4);
            GUILayout.Label($"Boucle : {loopDuration:F3}s (théorique {expectedLoop:F3}s)");

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Couches — vitesse · phase</b>", RichLabel());
            if (GUILayout.Button("Remettre à zéro", GUILayout.Width(120))) _probes.Clear();
            if (GUILayout.Button("→ console", GUILayout.Width(90))) DumpDiagnostics(space);
            GUILayout.EndHorizontal();

            foreach (var layer in space.layers)
            {
                if (layer == null || layer.clip == null) continue;
                GUILayout.Label(LayerDiagnosticText(space, layer, outputRate, loopPosition, loopDuration, true),
                                RichLabel());
            }
        }

        /// <summary>Une ligne de diagnostic par couche, en balisage riche ou en texte brut.</summary>
        private string LayerDiagnosticText(MusicSpaceDefinition space, MusicLayerDefinition layer,
                                           int outputRate, double loopPosition, double loopDuration, bool rich)
        {
            AudioClip clip = layer.clip;
            string key = $"{space.spaceId}/{layer.layerId}";
            double position = engine.GetLayerPlaybackPositionSeconds(key);

            // Un clip dont la fréquence ne correspond pas à la sortie est rééchantillonné :
            // c'est le cas typique d'une hauteur et d'une vitesse fausses en WebGL.
            bool rateMismatch = outputRate > 0 && clip.frequency != outputRate;

            string phase = "—";
            if (position >= 0.0)
            {
                double delta = position - loopPosition;
                if (loopDuration > 0.0)
                {
                    // Ramène l'écart dans [-boucle/2, +boucle/2] pour lire un décalage signé.
                    delta -= Math.Floor(delta / loopDuration + 0.5) * loopDuration;
                }
                phase = $"{delta * 1000.0:+0;-0} ms";
                if (rich && Math.Abs(delta) > 0.05) phase = $"<color=#E8B341>{phase}</color>";
            }

            string speed = "—";
            if (_probes.TryGetValue(key, out RateProbe probe) && probe.maxRate > 0.0)
            {
                bool wrong = Math.Abs(probe.rate - 1.0) > 0.005;
                bool unstable = probe.maxRate - probe.minRate > 0.01;
                speed = $"×{probe.rate:F3} ({probe.minRate:F3}–{probe.maxRate:F3})"
                      + $" · {probe.loops} tours · phase max {probe.maxPhaseError * 1000.0:F0} ms";
                if (rich && (wrong || unstable)) speed = $"<color=#E8B341>{speed}</color>";
            }

            return $"{layer.layerId}\n"
                 + $"   {clip.frequency} Hz · {clip.channels} ch · {clip.length:F2}s · {clip.loadState}"
                 + $"{(rateMismatch ? (rich ? "  <color=#E8B341>≠ sortie</color>" : "  ≠ sortie") : "")}\n"
                 + $"   vitesse {speed} · phase {phase}";
        }

        /// <summary>
        /// Envoie tout le diagnostic dans la console (celle du navigateur en WebGL),
        /// pour pouvoir le copier-coller au lieu de le relever à l'écran.
        /// </summary>
        private void DumpDiagnostics(MusicSpaceDefinition space)
        {
            AudioConfiguration config = AudioSettings.GetConfiguration();
            var sb = new System.Text.StringBuilder(1024);

            sb.AppendLine("=== Diagnostic audio ===");
            sb.AppendLine($"Plateforme : {Application.platform} · Unity {Application.unityVersion}");
            sb.AppendLine($"Sortie : {AudioSettings.outputSampleRate} Hz · {config.speakerMode} "
                        + $"· buffer {config.dspBufferSize} · {config.numRealVoices} voix");

            double dspElapsed = AudioSettings.dspTime - _dspReferenceTime;
            double realElapsed = Time.realtimeSinceStartupAsDouble - _realReferenceTime;
            sb.AppendLine($"Horloge DSP : {dspElapsed:F2}s pour {realElapsed:F2}s réelles "
                        + $"→ ×{(realElapsed > 0 ? dspElapsed / realElapsed : 0):F4}");

            if (space != null)
            {
                double expected = space.beatsPerBar * space.barsPerLoop * (60.0 / Mathf.Max(1f, space.bpm));
                sb.AppendLine($"Espace {space.spaceId} : boucle {engine.GetLoopDurationSeconds():F3}s "
                            + $"(théorique {expected:F3}s) · {space.bpm} bpm · {space.barsPerLoop} mesures");

                int outputRate = AudioSettings.outputSampleRate;
                double loopPosition = engine.GetLoopPositionSeconds();
                double loopDuration = engine.GetLoopDurationSeconds();
                foreach (var layer in space.layers)
                {
                    if (layer?.clip == null) continue;
                    sb.AppendLine(LayerDiagnosticText(space, layer, outputRate, loopPosition, loopDuration, false));
                }
            }

            UnityEngine.Debug.Log(sb.ToString());
        }

        // ------------------------------------------------------------------ visualiseur

        /// <summary>
        /// Représente l'espace courant comme un graphe radial : un rayon par axe, gradué
        /// selon ses propres paliers, et deux tracés superposés —
        ///  - la valeur continue issue du ContextPreset (ce que « voit » le brain) ;
        ///  - le niveau quantifié réellement demandé au moteur (ce qui filtre les couches).
        /// L'écart entre les deux montre à quel moment un palier va basculer.
        /// </summary>
        private void DrawSpaceGraph(MusicSpaceDefinition space)
        {
            Section("Espace — position en temps réel");
            showSpaceGraph = GUILayout.Toggle(showSpaceGraph, "Afficher le graphe");
            if (!showSpaceGraph) return;

            int axisCount = space != null ? space.axes.Count : 0;
            if (axisCount == 0)
            {
                GUILayout.Label("Aucun axe à représenter.");
                return;
            }

            int extraCount = Mathf.Max(0, axisCount - 3);
            float height = graphHeight + extraCount * ExtraRowHeight;
            Rect rect = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true));

            // Rien d'autre ne doit être émis en layout après ce point : le tracé pur
            // ne s'exécute qu'au repaint.
            if (Event.current.type != EventType.Repaint) return;

            GUI.DrawTexture(rect, _texGraph);

            // Bandes réservées : axes supplémentaires puis légende, sous le volume 3D.
            Rect legendRect = new Rect(rect.x + 8f, rect.yMax - 19f, rect.width - 16f, 17f);
            Rect extraRect = new Rect(rect.x, legendRect.y - extraCount * ExtraRowHeight,
                                      rect.width, extraCount * ExtraRowHeight);
            Rect plot = new Rect(rect.x, rect.y, rect.width, extraRect.y - rect.y);

            int[] coordinate = engine.GetRequestedCoordinate();
            int dimensions = Mathf.Min(3, axisCount);

            // Repère isométrique : X vers la droite, Y vers le haut, Z en profondeur.
            float scale = Mathf.Min((plot.width - 20f) / SpanX, (plot.height - 20f) / SpanY) * 0.84f;
            if (scale < 20f) return;
            Vector2 origin = new Vector2(plot.center.x - SpanX * 0.5f * scale,
                                         plot.center.y + SpanY * 0.5f * scale);

            Color grid = new Color(1f, 1f, 1f, 0.09f);
            Color edge = new Color(1f, 1f, 1f, 0.30f);
            Color guide = new Color(accentColor.r, accentColor.g, accentColor.b, 0.28f);

            DrawFloorGrid(origin, scale, dimensions, grid);
            DrawUnitFrame(origin, scale, dimensions, grid, edge);
            DrawAxisTicksAndLabels(space, coordinate, origin, scale, dimensions, edge);

            Vector3 quantized = SpacePosition(space, coordinate, dimensions, false);
            Vector3 continuous = SpacePosition(space, coordinate, dimensions, true);

            // Niveau quantifié : ce que le moteur applique réellement aux couches.
            DrawDot(ToScreen(quantized, origin, scale), 7f,
                    new Color(pendingColor.r, pendingColor.g, pendingColor.b, 0.9f));

            // Vecteur position continu, avec ses projections sur le sol et les arêtes.
            Vector2 tip = ToScreen(continuous, origin, scale);
            DrawGuides(continuous, origin, scale, dimensions, guide);
            DrawLine(ToScreen(Vector3.zero, origin, scale), tip, accentColor, 2f);
            DrawDot(tip, 9f, accentColor);

            DrawExtraAxes(space, coordinate, extraRect);

            // Avancement dans la boucle, en filet tout en bas du cadre.
            if (engine.IsClockRunning)
            {
                double duration = engine.GetLoopDurationSeconds();
                float progress = duration > 0.0
                    ? Mathf.Clamp01((float)(engine.GetLoopPositionSeconds() / duration))
                    : 0f;
                Rect loopBar = new Rect(rect.x, rect.yMax - 3f, rect.width, 3f);
                GUI.DrawTexture(loopBar, _texTrack);
                GUI.DrawTexture(new Rect(loopBar.x, loopBar.y, loopBar.width * progress, loopBar.height),
                                _texAccent);
            }

            GUI.Label(legendRect,
                      $"<size=11><color=#{_accentHex}>● continu (preset)</color>   "
                    + $"<color=#{ColorUtility.ToHtmlStringRGB(pendingColor)}>● niveaux (moteur)</color></size>",
                      _graphLabel);
        }

        // Base de projection : X droite, Y haut, Z profondeur. L'emprise écran du cube
        // unité en découle, et sert à cadrer le tracé.
        private static readonly Vector2 BasisX = new Vector2(1f, 0f);
        private static readonly Vector2 BasisY = new Vector2(0f, -1f);
        private static readonly Vector2 BasisZ = new Vector2(0.45f, -0.42f);
        private const float SpanX = 1.45f;
        private const float SpanY = 1.42f;
        private const float ExtraRowHeight = 20f;

        private static Vector2 ToScreen(Vector3 position, Vector2 origin, float scale)
            => origin + (BasisX * position.x + BasisY * position.y + BasisZ * position.z) * scale;

        /// <summary>Position normalisée dans l'espace, continue (preset) ou quantifiée (moteur).</summary>
        private Vector3 SpacePosition(MusicSpaceDefinition space, int[] coordinate,
                                      int dimensions, bool continuous)
        {
            Vector3 position = Vector3.zero;

            for (int i = 0; i < dimensions; i++)
            {
                AxisDefinition axis = space.axes[i];
                int levelCount = Mathf.Max(1, axis.levelCount);

                float value;
                if (continuous)
                {
                    value = NormalizedAxisValue(space, axis.axisId);
                }
                else
                {
                    int level = coordinate != null && i < coordinate.Length ? coordinate[i] : 0;
                    value = (float)level / levelCount;
                }

                position[i] = Mathf.Clamp01(value);
            }

            return position;
        }

        /// <summary>Arêtes du cube unité : deux sommets reliés dès qu'ils diffèrent d'une seule coordonnée.</summary>
        private void DrawUnitFrame(Vector2 origin, float scale, int dimensions, Color grid, Color edge)
        {
            int corners = 1 << dimensions;

            for (int a = 0; a < corners; a++)
            {
                for (int b = a + 1; b < corners; b++)
                {
                    int diff = a ^ b;
                    if ((diff & (diff - 1)) != 0) continue;

                    // Les trois arêtes issues de l'origine portent les graduations : plus visibles.
                    Color color = a == 0 ? edge : grid;
                    DrawLine(ToScreen(Corner(a, dimensions), origin, scale),
                             ToScreen(Corner(b, dimensions), origin, scale),
                             color, a == 0 ? 1.5f : 1f);
                }
            }
        }

        private static Vector3 Corner(int mask, int dimensions)
        {
            Vector3 corner = Vector3.zero;
            for (int i = 0; i < dimensions; i++)
                corner[i] = ((mask >> i) & 1);
            return corner;
        }

        private void DrawFloorGrid(Vector2 origin, float scale, int dimensions, Color color)
        {
            if (dimensions < 3) return;

            for (int step = 1; step < 4; step++)
            {
                float t = step / 4f;
                DrawLine(ToScreen(new Vector3(t, 0f, 0f), origin, scale),
                         ToScreen(new Vector3(t, 0f, 1f), origin, scale), color, 1f);
                DrawLine(ToScreen(new Vector3(0f, 0f, t), origin, scale),
                         ToScreen(new Vector3(1f, 0f, t), origin, scale), color, 1f);
            }
        }

        private void DrawAxisTicksAndLabels(MusicSpaceDefinition space, int[] coordinate,
                                            Vector2 origin, float scale, int dimensions, Color color)
        {
            for (int i = 0; i < dimensions; i++)
            {
                AxisDefinition axis = space.axes[i];
                int levelCount = Mathf.Max(1, axis.levelCount);

                // Crans : un par palier, en travers de l'arête.
                for (int level = 1; level <= levelCount; level++)
                {
                    Vector3 at = Vector3.zero;
                    at[i] = (float)level / levelCount;

                    Vector2 point = ToScreen(at, origin, scale);
                    Vector2 direction = (ToScreen(Axis(i), origin, scale) - origin).normalized;
                    Vector2 normal = new Vector2(-direction.y, direction.x) * 4f;
                    DrawLine(point - normal, point + normal, color, 1.5f);
                }

                int value = coordinate != null && i < coordinate.Length ? coordinate[i] : 0;
                Vector3 labelAt = Axis(i) * 1.06f;
                Vector2 anchor = ToScreen(labelAt, origin, scale);
                GUI.Label(new Rect(anchor.x - 54f, anchor.y - 15f, 108f, 30f),
                          $"<size=11>{Or(axis.axisId, "?")}\n<b>{value}</b>/{levelCount}</size>",
                          _graphLabel);
            }
        }

        private static Vector3 Axis(int index)
            => index == 0 ? Vector3.right : index == 1 ? Vector3.up : Vector3.forward;

        /// <summary>Projections du point sur le sol puis sur les arêtes, pour lire ses coordonnées.</summary>
        private void DrawGuides(Vector3 position, Vector2 origin, float scale, int dimensions, Color color)
        {
            if (dimensions < 2) return;

            Vector3 onFloor = new Vector3(position.x, 0f, position.z);

            DrawLine(ToScreen(position, origin, scale), ToScreen(onFloor, origin, scale), color, 1f);
            DrawLine(ToScreen(onFloor, origin, scale),
                     ToScreen(new Vector3(position.x, 0f, 0f), origin, scale), color, 1f);

            if (dimensions >= 3)
                DrawLine(ToScreen(onFloor, origin, scale),
                         ToScreen(new Vector3(0f, 0f, position.z), origin, scale), color, 1f);

            // Hauteur (axe Y) reportée sur l'arête verticale.
            DrawLine(ToScreen(position, origin, scale),
                     ToScreen(new Vector3(0f, position.y, 0f), origin, scale), color, 1f);
        }

        /// <summary>Axes au-delà du troisième : une ligne graduée chacun, sous le volume.</summary>
        private void DrawExtraAxes(MusicSpaceDefinition space, int[] coordinate, Rect area)
        {
            for (int i = 3; i < space.axes.Count; i++)
            {
                AxisDefinition axis = space.axes[i];
                int levelCount = Mathf.Max(1, axis.levelCount);
                int level = coordinate != null && i < coordinate.Length ? coordinate[i] : 0;
                float continuous = Mathf.Clamp01(NormalizedAxisValue(space, axis.axisId));

                float y = area.y + (i - 3) * ExtraRowHeight;
                GUI.Label(new Rect(area.x + 8f, y, 96f, ExtraRowHeight),
                          $"<size=11>{Or(axis.axisId, "?")}</size>", _graphLabelLeft);

                Rect track = new Rect(area.x + 106f, y + 7f, area.width - 158f, 6f);
                GUI.DrawTexture(track, _texTrack);
                GUI.DrawTexture(new Rect(track.x, track.y, track.width * continuous, track.height),
                                _texAccent);

                // Repère du niveau quantifié, et crans des paliers.
                for (int step = 1; step <= levelCount; step++)
                {
                    float t = (float)step / levelCount;
                    DrawLine(new Vector2(track.x + track.width * t, track.y - 2f),
                             new Vector2(track.x + track.width * t, track.yMax + 2f),
                             new Color(1f, 1f, 1f, 0.18f), 1f);
                }

                float quantized = (float)level / levelCount;
                DrawDot(new Vector2(track.x + track.width * quantized, track.center.y), 7f,
                        new Color(pendingColor.r, pendingColor.g, pendingColor.b, 0.9f));

                GUI.Label(new Rect(track.xMax + 8f, y, 44f, ExtraRowHeight),
                          $"<size=11><b>{level}</b>/{levelCount}</size>", _graphLabelLeft);
            }
        }

        /// <summary>
        /// Segment tracé en rectangles alignés sur les axes.
        ///
        /// IMGUI ne sait pivoter une texture qu'en passant par GUI.matrix, dont la composition
        /// avec les groupes ouverts (zone, colonne, vue défilante) et la mise à l'échelle
        /// globale s'est révélée impossible à prédire de façon fiable : seuls les traits
        /// horizontaux tombaient juste, les autres partaient à des centaines de pixels. On
        /// rasterise donc le segment soi-même — le placement d'un rectangle droit, lui, est
        /// exact dans tous les cas.
        ///
        /// Le découpage suit l'axe mineur : un rectangle par pixel franchi dans cette
        /// direction. Un trait horizontal ou vertical ne coûte donc qu'un seul rectangle,
        /// et une diagonale reste fine et régulière.
        /// </summary>
        private void DrawLine(Vector2 a, Vector2 b, Color color, float width)
        {
            float deltaX = Mathf.Abs(b.x - a.x);
            float deltaY = Mathf.Abs(b.y - a.y);

            if (deltaX < 0.5f && deltaY < 0.5f)
            {
                DrawDot(a, width, color);
                return;
            }

            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Min(deltaX, deltaY)));
            float half = width * 0.5f;

            Color savedColor = GUI.color;
            GUI.color = color;

            Vector2 previous = a;
            for (int i = 1; i <= steps; i++)
            {
                Vector2 current = Vector2.Lerp(a, b, (float)i / steps);

                float x0 = Mathf.Min(previous.x, current.x) - half;
                float y0 = Mathf.Min(previous.y, current.y) - half;
                float x1 = Mathf.Max(previous.x, current.x) + half;
                float y1 = Mathf.Max(previous.y, current.y) + half;

                GUI.DrawTexture(new Rect(x0, y0, x1 - x0, y1 - y0), Texture2D.whiteTexture);
                previous = current;
            }

            GUI.color = savedColor;
        }

        private void DrawDot(Vector2 point, float size, Color color)
        {
            Color savedColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(point.x - size * 0.5f, point.y - size * 0.5f, size, size),
                            Texture2D.whiteTexture);
            GUI.color = savedColor;
        }

        private string AxisEffectText(MusicSpaceDefinition space, string axisName, float normalized)
        {
            int axisIndex = AxisIndex(space, axisName);
            if (axisIndex < 0) return $"→ {axisName} = {normalized:0.00}   (axe absent de l'espace)";

            int levelCount = space.axes[axisIndex].levelCount;
            int level = Mathf.RoundToInt(Mathf.Clamp01(normalized) * levelCount);
            return $"→ {axisName} = {normalized:0.00}   (niveau {level}/{levelCount})";
        }

        // ------------------------------------------------------------------ modèle des contrôles

        /// <summary>Reconstruit les contrôles quand l'espace courant (ou son preset) change.</summary>
        private void SyncControls(MusicSpaceDefinition space)
        {
            string spaceId = space != null ? space.spaceId : null;
            ContextPreset preset = space != null ? space.contextPreset : null;
            if (!_controlsDirty && spaceId == _controlsSpaceId && preset == _controlsPreset) return;

            bool spaceChanged = _controlsDirty || spaceId != _controlsSpaceId;
            _controlsSpaceId = spaceId;
            _controlsPreset = preset;
            _controlsDirty = false;

            RebuildControls(space);

            if (spaceChanged && resetParametersOnSpaceChange)
                ResetParametersForSpace(space);
        }

        private void RebuildControls(MusicSpaceDefinition space)
        {
            _controls.Clear();
            _envelopeAxes.Clear();
            if (space == null) return;

            // Axes qui pilotent des couches graduelles : leurs paramètres deviennent des sliders.
            foreach (var layer in space.layers)
            {
                if (layer == null || layer.volumeMode != LayerVolumeMode.VolumeEnvelope) continue;
                if (layer.volumeEnvelope == null || string.IsNullOrEmpty(layer.volumeEnvelope.axisId)) continue;
                _envelopeAxes.Add(layer.volumeEnvelope.axisId);
            }

            ContextPreset preset = space.contextPreset;
            if (preset == null) return;

            foreach (var binding in preset.axisBindings)
            {
                if (binding?.rules == null || binding.rules.Count == 0) continue;

                AxisOverride axisOverride = FindOverride(space.spaceId, binding.axisName);
                bool continuousAxis = _envelopeAxes.Contains(binding.axisName);

                // Ligne unique pilotant l'axe : les paramètres sont résolus ensemble.
                // Réservé au mode HighestWins, seul cas où la combinaison est inversible.
                if (axisOverride != null && axisOverride.singleRow && !continuousAxis
                    && binding.combinationMode == CombinationMode.HighestWins)
                {
                    Control ladder = new Control
                    {
                        kind = ControlKind.AxisLadder,
                        axisName = binding.axisName,
                        binding = binding,
                        label = Or(axisOverride.label, binding.axisName),
                        stepLabels = axisOverride.stepLabels
                    };
                    BuildLadder(ladder, binding, space);
                    if (ladder.ladder.Count > 0)
                    {
                        _controls.Add(ladder);
                        continue;
                    }
                }

                foreach (var rule in binding.rules)
                {
                    if (rule?.source == null) continue;

                    Control control = FindControl(rule.source);
                    if (control == null)
                    {
                        control = new Control
                        {
                            source = rule.source,
                            label = Or(axisOverride?.label, LabelFor(rule.source)),
                            stepLabels = axisOverride != null && axisOverride.stepLabels.Count > 0
                                ? axisOverride.stepLabels
                                : null
                        };
                        _controls.Add(control);
                    }

                    control.affects.Add(new AxisEffect { axisName = binding.axisName, rule = rule });
                    control.proximity |= continuousAxis;
                    if (continuousAxis || rule.mode == MappingMode.Curve)
                        control.kind = ControlKind.Slider;

                    // Règle de référence : la courbe si le paramètre est continu,
                    // sinon la table de seuils la plus détaillée.
                    bool better = control.primary == null
                        || (control.kind == ControlKind.Slider && rule.mode == MappingMode.Curve
                            && control.primary.mode != MappingMode.Curve)
                        || (control.kind == ControlKind.Steps
                            && CountThresholds(rule) > CountThresholds(control.primary));
                    if (better) control.primary = rule;
                }
            }

            foreach (var control in _controls)
                if (control.kind != ControlKind.AxisLadder)
                    FinalizeControl(control);
        }

        /// <summary>
        /// Construit les paliers d'une rangée qui pilote un axe.
        ///
        /// Les cibles sont tous les résultats déclarés par les règles de l'axe. Pour une
        /// cible donnée, chaque paramètre est amené sur son palier au résultat le plus haut
        /// sans dépasser la cible : en HighestWins la valeur de l'axe vaut alors exactement
        /// la cible. Les paramètres inversés (résultat qui décroît quand la valeur monte,
        /// comme la santé du boss) sont donc réglés correctement sans cas particulier.
        /// </summary>
        private void BuildLadder(Control control, AxisBinding binding, MusicSpaceDefinition space)
        {
            List<float> targets = new List<float> { 0f };
            foreach (var rule in binding.rules)
            {
                if (rule?.thresholds == null) continue;
                foreach (var entry in rule.thresholds)
                {
                    float result = Mathf.Clamp01(entry.result);
                    if (!ContainsApprox(targets, result)) targets.Add(result);
                }
            }
            targets.Sort();

            int axisIndex = AxisIndex(space, binding.axisName);
            int levelCount = axisIndex >= 0 ? space.axes[axisIndex].levelCount : 0;

            foreach (float target in targets)
            {
                LadderStep step = new LadderStep
                {
                    axisValue = target,
                    level = Mathf.RoundToInt(target * levelCount)
                };
                step.label = $"Niv. {step.level}";

                bool reachable = target <= Epsilon;

                foreach (var rule in binding.rules)
                {
                    if (rule?.source == null) continue;

                    List<ThresholdEntry> steps = SortedThresholds(rule);
                    if (steps.Count == 0) continue;

                    int best = BestStepForTarget(steps, target);
                    if (Mathf.Abs(steps[best].result - target) <= Epsilon) reachable = true;

                    step.writes.Add(new ParamWrite
                    {
                        parameter = rule.source,
                        value = ValueForStep(steps, best, rule.source is IntParameter)
                    });
                }

                if (reachable && step.writes.Count > 0)
                    control.ladder.Add(step);
            }
        }

        private void FinalizeControl(Control control)
        {
            control.steps.Clear();
            if (control.primary?.thresholds != null)
                control.steps.AddRange(SortedThresholds(control.primary));

            if (control.kind != ControlKind.Slider) return;

            float min = 0f, max = 1f;
            AnimationCurve curve = control.primary?.curve;
            if (control.primary != null && control.primary.mode == MappingMode.Curve && curve != null && curve.length > 1)
            {
                min = curve.keys[0].time;
                max = curve.keys[curve.length - 1].time;
            }
            else if (control.steps.Count > 0)
            {
                min = control.steps[0].atLeast;
                max = control.steps[control.steps.Count - 1].atLeast;
            }

            if (max <= 1f) max = 1f;
            if (max <= min) max = min + 1f;

            control.min = min;
            control.max = max;
        }

        private Control FindControl(ParameterDefinition source)
        {
            foreach (var control in _controls)
                if (control.kind != ControlKind.AxisLadder && control.source == source)
                    return control;
            return null;
        }

        private AxisOverride FindOverride(string spaceId, string axisName)
        {
            if (string.IsNullOrEmpty(axisName)) return null;

            AxisOverride fallback = null;
            foreach (var entry in axisOverrides)
            {
                if (entry == null || entry.axisName != axisName) continue;
                if (entry.spaceId == spaceId) return entry;           // le plus spécifique gagne
                if (string.IsNullOrEmpty(entry.spaceId)) fallback = entry;
            }
            return fallback;
        }

        /// <summary>
        /// Palier d'une règle qui approche au mieux une valeur d'axe visée sans la dépasser ;
        /// à défaut, le palier au résultat le plus bas. C'est ce choix qui rend les paramètres
        /// inversés transparents : pour viser 0 sur la tension, la santé du boss est amenée
        /// à 1 et non à 0.
        /// </summary>
        private static int BestStepForTarget(List<ThresholdEntry> steps, float target)
        {
            int best = -1;
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].result > target + Epsilon) continue;
                if (best < 0 || steps[i].result > steps[best].result) best = i;
            }
            if (best < 0)
                for (int i = 0; i < steps.Count; i++)
                    if (best < 0 || steps[i].result < steps[best].result) best = i;
            return best;
        }

        /// <summary>Copie triée par seuil croissant — la liste de l'asset n'est jamais réordonnée.</summary>
        private static List<ThresholdEntry> SortedThresholds(MappingRule rule)
        {
            List<ThresholdEntry> steps = new List<ThresholdEntry>(rule.thresholds);
            steps.Sort((a, b) => a.atLeast.CompareTo(b.atLeast));
            return steps;
        }

        private static int CountThresholds(MappingRule rule)
            => rule?.thresholds != null ? rule.thresholds.Count : 0;

        private static bool ContainsApprox(List<float> values, float value)
        {
            foreach (float v in values)
                if (Mathf.Abs(v - value) <= Epsilon) return true;
            return false;
        }

        /// <summary>Palier actuellement sélectionné : le seuil le plus haut encore franchi.</summary>
        private static int ActiveStepIndex(Control control)
        {
            float raw = control.source.RawValue;
            int active = -1;
            for (int i = 0; i < control.steps.Count; i++)
                if (raw >= control.steps[i].atLeast)
                    active = i;
            return active;
        }

        /// <summary>
        /// Valeur à écrire dans le paramètre pour sélectionner un palier :
        ///  - premier palier : le seuil lui-même (typiquement 0, donc « inactif ») ;
        ///  - palier intermédiaire : au milieu de la bande, pour rester franchement dedans ;
        ///  - dernier palier : 1 si le seuil est normalisé, le seuil lui-même pour un entier
        ///    (une porte à 2 reste à 2), sinon le seuil + une marge (vitesse 3 → 5).
        /// </summary>
        private float ValueForStep(List<ThresholdEntry> steps, int index, bool isInt)
        {
            float lo = steps[index].atLeast;
            bool hasNext = index + 1 < steps.Count;
            float hi = hasNext
                ? steps[index + 1].atLeast
                : (lo <= 1f ? 1f : (isInt ? lo : lo + Mathf.Max(activationMargin, lo * 0.5f)));

            float value = index == 0 ? lo : (hasNext ? Mathf.Lerp(lo, hi, 0.5f) : hi);

            if (isInt)
            {
                float rounded = Mathf.Ceil(value);
                if (hasNext && rounded >= hi) rounded = Mathf.Floor(value);
                if (rounded < lo) rounded = Mathf.Ceil(lo);
                value = rounded;
            }

            return value;
        }

        private static void SetParameter(ParameterDefinition parameter, float value)
        {
            switch (parameter)
            {
                case FloatParameter fp: fp.Value = value; break;
                case IntParameter ip: ip.Value = Mathf.RoundToInt(value); break;
            }
        }

        /// <summary>
        /// Valeur normalisée que le MusicBrain pousse sur un axe : c'est l'évaluation
        /// du binding correspondant dans le preset de l'espace.
        /// </summary>
        private static float NormalizedAxisValue(MusicSpaceDefinition space, string axisId)
        {
            ContextPreset preset = space?.contextPreset;
            if (preset == null || string.IsNullOrEmpty(axisId)) return 0f;

            foreach (var binding in preset.axisBindings)
                if (binding != null && binding.axisName == axisId)
                    return Mathf.Clamp01(binding.Evaluate());
            return 0f;
        }

        private static int AxisIndex(MusicSpaceDefinition space, string axisId)
        {
            if (space == null) return -1;
            for (int i = 0; i < space.axes.Count; i++)
                if (space.axes[i].axisId == axisId)
                    return i;
            return -1;
        }

        private static string StepLabel(Control control, int index, string fallback)
        {
            if (control.stepLabels != null && index < control.stepLabels.Count
                && !string.IsNullOrEmpty(control.stepLabels[index]))
                return control.stepLabels[index];
            return fallback;
        }

        private string LabelFor(ParameterDefinition parameter)
        {
            foreach (var entry in parameterLabels)
                if (entry != null && entry.parameter == parameter && !string.IsNullOrEmpty(entry.label))
                    return entry.label;
            return Humanize(parameter.name);
        }

        /// <summary>« EnemyCount » → « Enemy Count ».</summary>
        private static string Humanize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            var sb = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]) && name[i - 1] != ' ')
                    sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string FormatValue(ParameterDefinition parameter, float value)
            => parameter is IntParameter ? Mathf.RoundToInt(value).ToString() : value.ToString("0.##");

        // ------------------------------------------------------------------ helpers

        private void DrawBar(float frac)
        {
            DrawBar(GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true)), frac);
        }

        private void DrawBar(float frac, float width)
        {
            DrawBar(GUILayoutUtility.GetRect(width, 10, GUILayout.Width(width)), frac);
        }

        private void DrawBar(Rect r, float frac)
        {
            if (Event.current.type != EventType.Repaint) return;

            GUI.DrawTexture(r, _texTrack);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(frac), r.height), _texAccent);
        }

        /// <summary>Colore un libellé en accent (actif) ou en gris (inactif).</summary>
        private string Tint(string text, bool active)
            => $"<color=#{(active ? _accentHex : _dimHex)}>{text}</color>";

        private static void Section(string title)
        {
            GUILayout.Space(8);
            GUILayout.Label($"<b>{title}</b>", RichLabel());
        }

        // La skin du thème active déjà le texte riche et le retour à la ligne : ces deux
        // accesseurs restent pour la lisibilité des appels existants.
        private static GUIStyle RichLabel() => GUI.skin.label;
        private static GUIStyle RichButton() => GUI.skin.button;

        // ------------------------------------------------------------------ thème

        /// <summary>
        /// Construit une skin sombre opaque à partir de la skin par défaut. Tout est peint
        /// avec des textures unies : la skin intégrée d'Unity est semi-transparente, ce qui
        /// rend les boutons illisibles par-dessus la vue de jeu.
        /// </summary>
        private void BuildTheme()
        {
            ReleaseTheme();

            Color face = Color.Lerp(backgroundColor, Color.white, 0.11f);
            Color faceHover = Color.Lerp(backgroundColor, Color.white, 0.19f);
            Color facePressed = Color.Lerp(backgroundColor, accentColor, 0.45f);
            Color text = new Color(0.88f, 0.89f, 0.91f);
            Color dim = new Color(0.54f, 0.56f, 0.60f);

            _texBackground = SolidTexture(backgroundColor);
            _texPanel = SolidTexture(Color.Lerp(backgroundColor, Color.white, 0.04f));
            _texTrack = SolidTexture(Color.Lerp(backgroundColor, Color.white, 0.09f));
            _texAccent = SolidTexture(accentColor);
            _texPending = SolidTexture(Color.Lerp(backgroundColor, pendingColor, 0.55f));
            _texGraph = SolidTexture(Color.Lerp(backgroundColor, Color.black, 0.4f));

            Texture2D faceTex = SolidTexture(face);
            Texture2D hoverTex = SolidTexture(faceHover);
            Texture2D pressedTex = SolidTexture(facePressed);

            _accentHex = ColorUtility.ToHtmlStringRGB(Color.Lerp(accentColor, Color.white, 0.35f));
            _dimHex = ColorUtility.ToHtmlStringRGB(dim);

            _skin = Instantiate(GUI.skin);

            Paint(_skin.box, _texPanel, text);
            _skin.box.padding = new RectOffset(10, 10, 8, 8);

            GUIStyle button = _skin.button;
            Paint(button, faceTex, text);
            button.hover.background = hoverTex;
            button.hover.textColor = Color.white;
            button.active.background = pressedTex;
            button.active.textColor = Color.white;
            // Un GUILayout.Toggle dessiné avec le style bouton utilise les états « on ».
            button.onNormal.background = _texAccent;
            button.onNormal.textColor = Color.white;
            button.onHover.background = _texAccent;
            button.onHover.textColor = Color.white;
            button.onActive.background = pressedTex;
            button.onActive.textColor = Color.white;
            button.padding = new RectOffset(8, 8, 5, 5);
            button.margin = new RectOffset(3, 3, 3, 3);
            button.alignment = TextAnchor.MiddleCenter;
            button.richText = true;
            button.wordWrap = true;

            Paint(_skin.label, null, text);
            _skin.label.richText = true;
            _skin.label.wordWrap = true;
            _skin.label.padding = new RectOffset(2, 2, 2, 2);

            Paint(_skin.toggle, null, text);
            _skin.toggle.onNormal.textColor = Color.white;
            _skin.toggle.richText = true;

            Paint(_skin.horizontalSlider, _texTrack, text);
            _skin.horizontalSlider.fixedHeight = 8f;
            _skin.horizontalSlider.margin = new RectOffset(4, 4, 8, 8);

            Paint(_skin.horizontalSliderThumb, _texAccent, text);
            _skin.horizontalSliderThumb.fixedWidth = 14f;
            _skin.horizontalSliderThumb.fixedHeight = 14f;

            Paint(_skin.verticalScrollbar, _texTrack, text);
            _skin.verticalScrollbar.fixedWidth = 8f;
            Paint(_skin.verticalScrollbarThumb, hoverTex, text);
            _skin.verticalScrollbarThumb.fixedWidth = 8f;
            HideStyle(_skin.verticalScrollbarUpButton);
            HideStyle(_skin.verticalScrollbarDownButton);
            Paint(_skin.scrollView, null, text);

            _selectedButton = new GUIStyle(button);
            _selectedButton.normal.background = _texAccent;
            _selectedButton.normal.textColor = Color.white;
            _selectedButton.hover.background = _texAccent;
            _selectedButton.hover.textColor = Color.white;

            _graphLabel = new GUIStyle(_skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                richText = true
            };
            _graphLabel.normal.textColor = text;

            _graphLabelLeft = new GUIStyle(_graphLabel) { alignment = TextAnchor.MiddleLeft };
            _graphLabelLeft.normal.textColor = text;

            _pendingButton = new GUIStyle(button);
            _pendingButton.normal.background = _texPending;
            _pendingButton.normal.textColor = Color.white;
            _pendingButton.hover.background = _texPending;
            _pendingButton.hover.textColor = Color.white;
        }

        /// <summary>Applique un fond et une couleur de texte à tous les états d'un style.</summary>
        private static void Paint(GUIStyle style, Texture2D background, Color textColor)
        {
            // Les textures unies 1×1 ne doivent pas être découpées en 9 tranches.
            style.border = new RectOffset(0, 0, 0, 0);

            style.normal.background = background;
            style.hover.background = background;
            style.active.background = background;
            style.focused.background = background;

            style.normal.textColor = textColor;
            style.hover.textColor = textColor;
            style.active.textColor = textColor;
            style.focused.textColor = textColor;
            style.onNormal.textColor = textColor;
            style.onHover.textColor = textColor;
            style.onActive.textColor = textColor;
        }

        private static void HideStyle(GUIStyle style)
        {
            style.normal.background = null;
            style.hover.background = null;
            style.active.background = null;
            style.fixedHeight = 0f;
            style.fixedWidth = 0f;
        }

        private Texture2D SolidTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();

            _themeTextures.Add(texture);
            return texture;
        }

        private void ReleaseTheme()
        {
            foreach (var texture in _themeTextures)
                if (texture != null) Destroy(texture);
            _themeTextures.Clear();

            if (_skin != null) Destroy(_skin);
            _skin = null;
        }

        private void OnDestroy() => ReleaseTheme();

        private static string Or(string s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;

        private static string CoordToString(int[] coord)
            => (coord == null || coord.Length == 0) ? "-" : string.Join(", ", coord);
    }
}
