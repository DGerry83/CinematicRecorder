// File: AdvancedSettingsWindow.cs
using CinematicRecorder.Core;
using UnityEngine;
using static CinematicRecorder.UI.CinematicUIStrings;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Docked panel window for advanced settings. Locks position to follow SettingsDialog.
    /// Contains: Encoding tab (Safe Mode, Audio Capture, PNG Sequence) and Rendering tab (TAB, Gradient Protection).
    /// </summary>
    public class AdvancedSettingsWindow : MonoBehaviour
    {
        #region Fields & State
        private SettingsDialog parentWindow;
        private Rect windowRect;
        private bool isVisible = false;
        private bool stylesInitialized = false;
        private GUIStyle windowStyle;
        private GUIStyle toggleStyleActive;
        private GUIStyle labelStyle;
        private GUIStyle tabButtonStyle;
        private GUIStyle tabButtonActiveStyle;

        private enum AdvancedTab { Encoding, Rendering }
        private AdvancedTab currentTab = AdvancedTab.Encoding;
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the window with parent reference.
        /// </summary>
        public void Initialize(SettingsDialog parent)
        {
            parentWindow = parent;
            UpdatePosition();
            InitStyles();
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;

            windowStyle = CinematicUIResources.Styles.Window();

            toggleStyleActive = new GUIStyle(HighLogic.Skin.toggle);
            toggleStyleActive.normal.textColor = CinematicUIResources.Colors.TOGGLE_ACTIVE_GREEN;
            toggleStyleActive.onNormal.textColor = CinematicUIResources.Colors.TOGGLE_ACTIVE_GREEN;
            toggleStyleActive.fontStyle = FontStyle.Bold;

            labelStyle = new GUIStyle(HighLogic.Skin.label);

            tabButtonStyle = new GUIStyle(HighLogic.Skin.button);
            
            tabButtonActiveStyle = new GUIStyle(HighLogic.Skin.button);
            tabButtonActiveStyle.normal.textColor = CinematicUIResources.Colors.TOGGLE_ACTIVE_GREEN;
            tabButtonActiveStyle.fontStyle = FontStyle.Bold;

            stylesInitialized = true;
        }
        #endregion

        #region Position Management
        private void UpdatePosition()
        {
            if (parentWindow == null) return;

            Rect parentRect = parentWindow.GetWindowRect();

            float x = parentWindow.GetDockEdgeX();
            float y = parentRect.y;
            float width = CinematicUIResources.Layout.AdvancedSettings.PANEL_WIDTH;

            if (windowRect.width == 0f)
                windowRect = new Rect(x, y, width, 10f);

            windowRect.x = x;
            windowRect.y = y;
            windowRect.width = width;
        }

        /// <summary>
        /// Forces the window to stay locked to parent position. Call before GUILayout.Window.
        /// </summary>
        private void EnforceLockedPosition()
        {
            if (parentWindow == null) return;

            Rect parentRect = parentWindow.GetWindowRect();

            windowRect.x = parentWindow.GetDockEdgeX();
            windowRect.y = parentRect.y;
        }
        #endregion

        #region Unity Lifecycle
        private void OnGUI()
        {
            if (!isVisible || parentWindow == null) return;

            EnforceLockedPosition();

            windowRect = GUILayout.Window(
                CinematicUIResources.Windows.IDs.AdvancedSettingsDocked,
                windowRect,
                DrawWindow,
                AdvancedSettings.WindowTitle,
                windowStyle
            );
        }
        #endregion

        #region Window Layout
        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            
            DrawTabs();
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);
            
            if (currentTab == AdvancedTab.Encoding)
            {
                DrawEncodingTab();
            }
            else
            {
                DrawRenderingTab();
            }
            
            GUILayout.EndVertical();
            // no DragWindow call, docked window
        }

        private void DrawTabs()
        {
            GUILayout.BeginHorizontal();
            
            GUIStyle encodingStyle = (currentTab == AdvancedTab.Encoding) ? tabButtonActiveStyle : tabButtonStyle;
            if (GUILayout.Button(AdvancedSettings.EncodingTab, encodingStyle, GUILayout.Height(CinematicUIResources.Layout.AdvancedSettings.TAB_HEIGHT), GUILayout.Width(CinematicUIResources.Layout.AdvancedSettings.TAB_BUTTON_WIDTH)))
            {
                currentTab = AdvancedTab.Encoding;
            }
            
            GUIStyle renderingStyle = (currentTab == AdvancedTab.Rendering) ? tabButtonActiveStyle : tabButtonStyle;
            if (GUILayout.Button(AdvancedSettings.RenderingTab, renderingStyle, GUILayout.Height(CinematicUIResources.Layout.AdvancedSettings.TAB_HEIGHT), GUILayout.Width(CinematicUIResources.Layout.AdvancedSettings.TAB_BUTTON_WIDTH)))
            {
                currentTab = AdvancedTab.Rendering;
            }
            
            GUILayout.EndHorizontal();
        }

        private void DrawEncodingTab()
        {
            DrawAudioCaptureToggle();
            GUILayout.Space(CinematicUIResources.Spacing.LARGE);
            DrawCaptureUiLayerToggle();
            GUILayout.Space(CinematicUIResources.Spacing.LARGE);
            DrawPngSequenceToggle();
        }

        private void DrawRenderingTab()
        {
            DrawTemporalAccumulationSection();
            GUILayout.Space(CinematicUIResources.Spacing.LARGE);
            DrawGradientProtectionToggle();
            
            // Sharpening only available when TAB is enabled
            if (SessionState.EnableTemporalAccumulation)
            {
                GUILayout.Space(CinematicUIResources.Spacing.LARGE);
                DrawSharpeningToggle();
            }
        }

        private void DrawAudioCaptureToggle()
        {
            bool wasEnabled = GUI.enabled;
            if (DeterministicCaptureSession.IsRunning)
                GUI.enabled = false;

            GUIStyle toggleStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (SessionState.EnableAudioCapture)
            {
                toggleStyle.normal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.onNormal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.fontStyle = FontStyle.Bold;
            }

            bool newValue = GUILayout.Toggle(
                SessionState.EnableAudioCapture,
                AdvancedSettings.AudioCaptureToggle,
                toggleStyle
            );

            if (newValue != SessionState.EnableAudioCapture && !DeterministicCaptureSession.IsRunning)
            {
                SessionState.EnableAudioCapture = newValue;
                UnityEngine.Debug.Log($"[CinematicRecorder] EnableAudioCapture = {newValue}");
            }

            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
            GUIStyle helpStyle = CinematicUIResources.Styles.Help();
            helpStyle.wordWrap = true;
            GUILayout.Label(AdvancedSettings.AudioCaptureTooltip, helpStyle);

            GUI.enabled = wasEnabled;
        }

        private void DrawCaptureUiLayerToggle()
        {
            bool wasEnabled = GUI.enabled;
            if (SessionState.EnableTemporalAccumulation || DeterministicCaptureSession.IsRunning)
                GUI.enabled = false;

            GUIStyle toggleStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (SessionState.CaptureUiLayer)
            {
                toggleStyle.normal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.onNormal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.fontStyle = FontStyle.Bold;
            }

            bool newValue = GUILayout.Toggle(
                SessionState.CaptureUiLayer,
                AdvancedSettings.CaptureUiToggle,
                toggleStyle
            );

            if (newValue != SessionState.CaptureUiLayer && !DeterministicCaptureSession.IsRunning)
            {
                SessionState.CaptureUiLayer = newValue;
                UnityEngine.Debug.Log($"[CinematicRecorder] CaptureUiLayer = {newValue}");
            }

            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
            GUIStyle helpStyle = CinematicUIResources.Styles.Help();
            helpStyle.wordWrap = true;
            GUILayout.Label(AdvancedSettings.CaptureUiTooltip, helpStyle);

            if (SessionState.EnableTemporalAccumulation)
            {
                GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
                GUIStyle conflictStyle = CinematicUIResources.Styles.Help();
                conflictStyle.wordWrap = true;
                GUILayout.Label(AdvancedSettings.CaptureUiTabConflict, conflictStyle);
            }

            GUI.enabled = wasEnabled;
        }

        private void DrawPngSequenceToggle()
        {
            bool wasEnabled = GUI.enabled;
            if (DeterministicCaptureSession.IsRunning)
                GUI.enabled = false;

            GUIStyle toggleStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (SessionState.PngSequence)
            {
                toggleStyle.normal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.onNormal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.fontStyle = FontStyle.Bold;
            }

            bool newValue = GUILayout.Toggle(
                SessionState.PngSequence,
                AdvancedSettings.PngSequenceToggle,
                toggleStyle
            );

            if (newValue != SessionState.PngSequence && !DeterministicCaptureSession.IsRunning)
            {
                SessionState.PngSequence = newValue;
                if (newValue)
                {
                    // Force software encoding when PNG mode is enabled
                    SessionState.ForceSoftwareEncoding = true;
                    UnityEngine.Debug.Log("[CinematicRecorder] PNG Sequence enabled - forcing software encoding path");

                    // Also disable TAB since PNG uses CPU path
                    if (SessionState.EnableTemporalAccumulation)
                    {
                        SessionState.EnableTemporalAccumulation = false;
                        UnityEngine.Debug.Log("[CinematicRecorder] TAB disabled due to PNG sequence mode");
                    }
                }
            }

            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
            GUIStyle helpStyle = CinematicUIResources.Styles.Help();
            helpStyle.wordWrap = true;
            GUILayout.Label(AdvancedSettings.PngSequenceTooltip, helpStyle);

            GUI.enabled = wasEnabled;
        }

        private void DrawTemporalAccumulationSection()
        {
            // TAB requires the GPU zero-copy path; supported on both AMF (AMD) and NVENC (Nvidia)
            bool hasGpuEncoder = SessionState.DetectedGpuEncoder == SessionState.GpuEncoder.Amd
                              || SessionState.DetectedGpuEncoder == SessionState.GpuEncoder.Nvidia;
            bool canUseTab = hasGpuEncoder && !SessionState.PngSequence && !SessionState.ForceSoftwareEncoding && !SessionState.CaptureUiLayer;

            bool wasEnabled = GUI.enabled;
            if (!canUseTab || DeterministicCaptureSession.IsRunning)
                GUI.enabled = false;

            // Main toggle
            GUIStyle toggleStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (SessionState.EnableTemporalAccumulation)
            {
                toggleStyle.normal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.onNormal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.fontStyle = FontStyle.Bold;
            }

            bool newValue = GUILayout.Toggle(
                SessionState.EnableTemporalAccumulation,
                AdvancedSettings.TemporalAccumulationToggle,
                toggleStyle
            );

            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
            if (SessionState.DetectedGpuEncoder == SessionState.GpuEncoder.None)
            {
                // No GPU encoder detected: explain why TAB is unavailable
                GUIStyle warningStyle = CinematicUIResources.Styles.Info();
                warningStyle.wordWrap = true;
                GUILayout.Label(AdvancedSettings.TabGpuRequiredWarning, warningStyle);
            }
            else
            {
                GUIStyle helpStyle = CinematicUIResources.Styles.Help();
                helpStyle.wordWrap = true;
                GUILayout.Label(AdvancedSettings.TemporalAccumulationTooltip, helpStyle);
            }

            if (SessionState.CaptureUiLayer)
            {
                GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
                GUIStyle conflictStyle = CinematicUIResources.Styles.Help();
                conflictStyle.wordWrap = true;
                GUILayout.Label(AdvancedSettings.CaptureUiTabConflict, conflictStyle);
            }

            // TAB only sticks when actually usable
            SessionState.EnableTemporalAccumulation = newValue && canUseTab;

            GUI.enabled = wasEnabled;
        }

        private void DrawGradientProtectionToggle()
        {
            bool wasEnabled = GUI.enabled;
            // Gradient protection only available for AMD encoder
            bool canUseGradient = SessionState.DetectedGpuEncoder == SessionState.GpuEncoder.Amd;
            if (!canUseGradient || DeterministicCaptureSession.IsRunning)
                GUI.enabled = false;

            GUIStyle toggleStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (SessionState.AmfUseBlueNoiseDither)
            {
                toggleStyle.normal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.onNormal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.fontStyle = FontStyle.Bold;
            }
            else if (!canUseGradient)
            {
                toggleStyle.normal.textColor = CinematicUIResources.Colors.INFO_ORANGE;
            }

            bool newValue = GUILayout.Toggle(
                SessionState.AmfUseBlueNoiseDither,
                AdvancedSettings.GradientProtectionToggle,
                toggleStyle
            );

            if (canUseGradient && newValue != SessionState.AmfUseBlueNoiseDither && !DeterministicCaptureSession.IsRunning)
            {
                SessionState.AmfUseBlueNoiseDither = newValue;
            }

            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
            GUIStyle tooltipStyle = CinematicUIResources.Styles.Help();
            tooltipStyle.wordWrap = true;

            if (!canUseGradient)
            {
                GUIStyle infoStyle = CinematicUIResources.Styles.Info();
                infoStyle.wordWrap = true;
                GUILayout.Label(AdvancedSettings.AMFOnlyWarning, infoStyle);
            }
            else if (SessionState.AmfUseBlueNoiseDither)
            {
                GUILayout.Label(Settings.GradientTooltip, tooltipStyle);
            }

            GUI.enabled = wasEnabled;
        }

        private void DrawSharpeningToggle()
        {
            bool wasEnabled = GUI.enabled;
            if (DeterministicCaptureSession.IsRunning)
                GUI.enabled = false;

            GUIStyle toggleStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (SessionState.TabEnableSharpening)
            {
                toggleStyle.normal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.onNormal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.fontStyle = FontStyle.Bold;
            }

            bool newValue = GUILayout.Toggle(
                SessionState.TabEnableSharpening,
                AdvancedSettings.SharpeningToggle,
                toggleStyle
            );

            if (newValue != SessionState.TabEnableSharpening && !DeterministicCaptureSession.IsRunning)
            {
                SessionState.TabEnableSharpening = newValue;
            }

            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
            GUIStyle helpStyle = CinematicUIResources.Styles.Help();
            helpStyle.wordWrap = true;
            GUILayout.Label(AdvancedSettings.SharpeningTooltip, helpStyle);

            // Strength slider (only if sharpening enabled)
            if (SessionState.TabEnableSharpening)
            {
                GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
                
                float strengthPercent = SessionState.TabSharpeningStrength * 100f;
                GUILayout.Label(string.Format(AdvancedSettings.SharpeningStrengthLabel, strengthPercent), HighLogic.Skin.label);
                
                float newStrength = GUILayout.HorizontalSlider(SessionState.TabSharpeningStrength, 0.0f, 1.0f);
                if (!Mathf.Approximately(newStrength, SessionState.TabSharpeningStrength))
                {
                    SessionState.TabSharpeningStrength = newStrength;
                }
            }

            GUI.enabled = wasEnabled;
        }
        #endregion

        #region Public API
        /// <summary>
        /// Shows the advanced settings window and refreshes position.
        /// </summary>
        public void Show()
        {
            isVisible = true;
            UpdatePosition();
        }

        /// <summary>
        /// Hides the advanced settings window.
        /// </summary>
        public void Hide()
        {
            isVisible = false;
        }

        public bool IsVisible => isVisible;

        /// <summary>
        /// Returns the current window rectangle.
        /// </summary>
        public Rect GetWindowRect() => windowRect;
        #endregion
    }
}
