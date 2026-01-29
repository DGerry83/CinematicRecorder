using CinematicRecorder.Core;
using UnityEngine;

namespace CinematicRecorder.UI
{
    public class RecordingControlsWindow : MonoBehaviour
    {
        private Rect windowRect = new Rect(300, 480, 320, 220);
        private GUIStyle windowStyle;
        private GUIStyle buttonStyle;
        private GUIStyle activeButtonStyle;
        private GUIStyle labelStyle;
        private bool stylesInitialized = false;
        private bool shouldShow = false;


        // Speed ramps foldout state
        private bool showSpeedRamps = false;
        private float durationSlider;
        private float exponentSlider;

        // Button state tracking for visual feedback
        private enum SpeedMode { Normal, Slow, SuperSlow, KrakenTime }
        private SpeedMode currentSpeedMode = SpeedMode.Normal;

        void Start()
        {
            InitStyles();
            SubscribeToEvents();
            LoadFromSessionState();
        }

        void OnDestroy()
        {
            UnsubscribeFromEvents();
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;

            windowStyle = new GUIStyle(HighLogic.Skin.window);
            buttonStyle = new GUIStyle(HighLogic.Skin.button);

            activeButtonStyle = new GUIStyle(HighLogic.Skin.button);
            activeButtonStyle.normal.textColor = Color.green;
            activeButtonStyle.fontStyle = FontStyle.Bold;

            labelStyle = new GUIStyle(HighLogic.Skin.label);
            stylesInitialized = true;
        }

        private void LoadFromSessionState()
        {
            durationSlider = SessionState.RampDurationDefault;
            // Convert actual exponent (0.3-4.0) to 0-1 slider position via inverse log
            exponentSlider = Mathf.Log(SessionState.RampExponent / SessionState.RampExponentMin)
                           / Mathf.Log(SessionState.RampExponentMax / SessionState.RampExponentMin);
        }

        private void SubscribeToEvents()
        {
            DeterministicCaptureSession.OnRecordingStarted += OnRecordingStarted;
            DeterministicCaptureSession.OnRecordingStopped += OnRecordingStopped;
            DeterministicCaptureSession.OnTimeScaleChanged += OnTimeScaleChanged;
        }

        private void UnsubscribeFromEvents()
        {
            DeterministicCaptureSession.OnRecordingStarted -= OnRecordingStarted;
            DeterministicCaptureSession.OnRecordingStopped -= OnRecordingStopped;
            DeterministicCaptureSession.OnTimeScaleChanged -= OnTimeScaleChanged;
        }

        private void OnRecordingStarted()
        {
            shouldShow = true;
            currentSpeedMode = SpeedMode.Normal;
        }

        private void OnRecordingStopped()
        {
            currentSpeedMode = SpeedMode.Normal;
        }

        private void OnTimeScaleChanged(float newScale)
        {
            float tolerance = 0.01f;
            if (Mathf.Abs(newScale - 1.0f) < tolerance)
                currentSpeedMode = SpeedMode.Normal;
            else if (Mathf.Abs(newScale - DeterministicCaptureSession.SUPER_SLOW_SCALE) < tolerance)
                currentSpeedMode = SpeedMode.SuperSlow;
            else if (Mathf.Abs(newScale - DeterministicCaptureSession.SLOW_SCALE) < tolerance)
                currentSpeedMode = SpeedMode.Slow;
            else if (Mathf.Abs(newScale - DeterministicCaptureSession.KRAKEN_TIME_SCALE) < tolerance)
                currentSpeedMode = SpeedMode.KrakenTime;
        }

        void OnGUI()
        {
            if (!shouldShow && !DeterministicCaptureSession.IsRunning) return;

            // Dynamic window height based on Speed Ramps foldout
            float targetHeight = showSpeedRamps ? 430f : 220f; // Expanded vs Collapsed height
            windowRect.height = Mathf.Lerp(windowRect.height, targetHeight, 0.25f);

            windowRect = GUILayout.Window(
                12347,
                windowRect,
                DrawWindow,
                "Recording Controls",
                windowStyle);
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.BeginVertical();

            DrawStatusDisplay();
            GUILayout.Space(8);

            DrawSpeedButtons();
            GUILayout.Space(8);

            DrawProgressInfo();
            GUILayout.Space(8);

            // NEW: Speed Ramps foldout (moved from AdvancedOptionsWindow)
            DrawSpeedRampsFoldout();
            GUILayout.Space(8);

            // Advanced button - opens empty window for future use
            if (GUILayout.Button("Advanced...", GUILayout.Height(25)))
            {
                var adv = FindObjectOfType<AdvancedOptionsWindow>();
                if (adv == null)
                {
                    var go = new GameObject("AdvancedOptionsWindow");
                    DontDestroyOnLoad(go);
                    adv = go.AddComponent<AdvancedOptionsWindow>();
                }
                adv.Toggle();
            }

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void DrawStatusDisplay()
        {
            if (!DeterministicCaptureSession.IsRunning)
            {
                GUILayout.Label("Recording stopped", labelStyle);
                return;
            }

            float multiplier = DeterministicCaptureSession.CurrentTimeScale < 1.0f ?
                1.0f / DeterministicCaptureSession.CurrentTimeScale : 1.0f;

            string speedText = DeterministicCaptureSession.CurrentTimeScale >= 0.999f ?
                "Normal Speed" :
                $"{multiplier:F1}× Slow Motion";

            GUIStyle headerStyle = new GUIStyle(labelStyle);
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.fontSize = 14;
            headerStyle.alignment = TextAnchor.MiddleCenter;

            GUILayout.Label(speedText, headerStyle);

            if (DeterministicCaptureSession.IsTransitioning)
            {
                string transitionText = DeterministicCaptureSession.CurrentTransitionDirection ==
                    DeterministicCaptureSession.TransitionDirection.Slowing ?
                    "Slowing..." : "Resuming...";

                GUIStyle transitionStyle = new GUIStyle(labelStyle);
                transitionStyle.normal.textColor = Color.yellow;
                transitionStyle.alignment = TextAnchor.MiddleCenter;
                GUILayout.Label($"Transition: {transitionText}", transitionStyle);
            }
        }

        private void DrawSpeedButtons()
        {
            bool running = DeterministicCaptureSession.IsRunning;
            GUI.enabled = running;

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Kraken-Time", currentSpeedMode == SpeedMode.KrakenTime ? activeButtonStyle : buttonStyle, GUILayout.Width(70)))
                DeterministicCaptureSession.RequestKrakenTime();

            if (GUILayout.Button("Super-Slow", currentSpeedMode == SpeedMode.SuperSlow ? activeButtonStyle : buttonStyle, GUILayout.Width(70)))
                DeterministicCaptureSession.RequestSuperSlow();

            if (GUILayout.Button("Slow", currentSpeedMode == SpeedMode.Slow ? activeButtonStyle : buttonStyle, GUILayout.Width(70)))
                DeterministicCaptureSession.RequestSlow();

            if (GUILayout.Button("Resume", currentSpeedMode == SpeedMode.Normal ? activeButtonStyle : buttonStyle, GUILayout.Width(70)))
                DeterministicCaptureSession.RequestNormalSpeed();

            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        private void DrawProgressInfo()
        {
            if (!DeterministicCaptureSession.IsRunning) return;

            // NEW: Check for unlimited mode
            bool unlimited = DeterministicCaptureSession.IsUnlimitedMode;

            if (unlimited)
            {
                // Unlimited mode: show only captured frames and sim time, no target
                string progress = $"Simulated: {DeterministicCaptureSession.AccumulatedSimulatedSeconds:F1}s elapsed";
                GUILayout.Label(progress, labelStyle);

                GUILayout.Label($"Frames: {DeterministicCaptureSession.CapturedFrames:N0}", labelStyle);

                // Indeterminate progress bar (pulsing animation)
                GUILayout.BeginHorizontal(GUI.skin.box);
                float pulse = Mathf.PingPong(Time.time * 2f, 1f); // Pulse between 0 and 1
                GUIStyle barStyle = new GUIStyle(GUI.skin.box);
                barStyle.normal.background = MakeTexture(2, 2, new Color(0.2f, 0.6f, 0.9f));
                // Show a moving segment instead of filling
                float barWidth = 280f;
                float segmentWidth = 60f;
                float xPos = pulse * (barWidth - segmentWidth);

                // Create space on left
                GUILayout.Box("", GUIStyle.none, GUILayout.Width(xPos), GUILayout.Height(16));
                // Pulsing segment
                GUILayout.Box("", barStyle, GUILayout.Width(segmentWidth), GUILayout.Height(16));
                // Fill rest with empty
                GUILayout.Box("", GUIStyle.none, GUILayout.Width(barWidth - xPos - segmentWidth), GUILayout.Height(16));

                GUILayout.EndHorizontal();
            }
            else
            {
                // Limited mode: existing behavior
                string progress = $"Simulated: {DeterministicCaptureSession.AccumulatedSimulatedSeconds:F1}s / " +
                                $"{DeterministicCaptureSession.TargetSeconds:F1}s";

                GUILayout.Label(progress, labelStyle);

                float progressPercent = DeterministicCaptureSession.TargetSeconds > 0 ?
                    DeterministicCaptureSession.AccumulatedSimulatedSeconds / DeterministicCaptureSession.TargetSeconds : 0f;

                GUILayout.BeginHorizontal(GUI.skin.box);
                GUIStyle barStyle = new GUIStyle(GUI.skin.box);
                barStyle.normal.background = MakeTexture(2, 2, new Color(0.2f, 0.6f, 0.9f));
                GUILayout.Box("", barStyle, GUILayout.Width(280 * Mathf.Clamp01(progressPercent)), GUILayout.Height(16));
                GUILayout.EndHorizontal();
            }
        }

        // NEW: Speed Ramps foldout section
        private void DrawSpeedRampsFoldout()
        {
            string label = showSpeedRamps ? "▼ Speed Ramps" : "► Speed Ramps";

            if (GUILayout.Button(label, HighLogic.Skin.button))
            {
                showSpeedRamps = !showSpeedRamps;
                // Adjust window height target if needed (optional refinement)
            }

            if (!showSpeedRamps) return;

            GUIStyle helpStyle = new GUIStyle(HighLogic.Skin.label);
            helpStyle.fontSize = 10;
            helpStyle.normal.textColor = Color.gray;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Space(5);

            // Ramp Duration
            GUILayout.Label($"Ramp Duration: {durationSlider:F2}s", HighLogic.Skin.label);
            float newDuration = GUILayout.HorizontalSlider(durationSlider, 0.1f, 3.0f);
            if (!Mathf.Approximately(newDuration, durationSlider))
            {
                durationSlider = newDuration;
                SessionState.RampDurationDefault = newDuration;
            }
            GUILayout.Label("Total wall-clock time for speed transitions", helpStyle);
            GUILayout.Space(10);

            // Curve Bias (Logarithmic slider)
            GUILayout.Label($"Curve Bias: {SessionState.RampExponent:F2}", HighLogic.Skin.label);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Linger Slow-mo", helpStyle, GUILayout.Width(70));
            float sliderPos = GUILayout.HorizontalSlider(exponentSlider, 0f, 1f);
            GUILayout.Label("Linger Normal", helpStyle, GUILayout.Width(70));
            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(sliderPos, exponentSlider))
            {
                exponentSlider = sliderPos;
                SessionState.RampExponent = SessionState.RampExponentMin
                    * Mathf.Pow(SessionState.RampExponentMax / SessionState.RampExponentMin, sliderPos);
            }
            GUILayout.Label(GetCurveDescription(SessionState.RampExponent), helpStyle);
            GUILayout.Space(10);

            // Presets
            GUILayout.Label("Presets", HighLogic.Skin.label);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Linear", GUILayout.Width(80)))
                SetExponentWithSliderUpdate(1.0f);

            if (GUILayout.Button("Linger Normal", GUILayout.Width(80)))
                SetExponentWithSliderUpdate(3.0f);

            if (GUILayout.Button("Snap to Slow", GUILayout.Width(80)))
                SetExponentWithSliderUpdate(0.5f);

            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            GUILayout.EndVertical();
        }

        private void SetExponentWithSliderUpdate(float value)
        {
            SessionState.RampExponent = value;
            exponentSlider = Mathf.Log(value / SessionState.RampExponentMin)
                           / Mathf.Log(SessionState.RampExponentMax / SessionState.RampExponentMin);
        }

        private string GetCurveDescription(float exp)
        {
            if (Mathf.Abs(exp - 1.0f) < 0.1f) return "Linear transition";
            if (exp > 2.5f) return "Linger at normal speed, rush through slow-motion";
            if (exp > 1.5f) return "Gradual entry, fast exit to slow-mo";
            if (exp < 0.5f) return "Snap to slow-mo, linger there";
            if (exp < 0.8f) return "Fast entry, gentle exit";
            return "Moderate curve";
        }

        private Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pixels);
            result.Apply();
            return result;
        }

        public void Show() { shouldShow = true; }
        public void Hide() { shouldShow = false; }
    }
}