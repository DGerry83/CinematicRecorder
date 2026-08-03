// File: AdvancedCameraOptionsWindow.cs
using CinematicRecorder.Integration;
using CinematicRecorder.Core;
using UnityEngine;
using static CinematicRecorder.UI.CinematicUIStrings;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Docked panel window for advanced camera options. Locks position to follow RecordingControlsWindow.
    /// Contains: Camera Path Playback Timing, Camera Shake controls (placeholder), HullCam Overlay selector (placeholder).
    /// </summary>
    public class AdvancedCameraOptionsWindow : MonoBehaviour
    {
        #region Fields & State
        private RecordingControlsWindow parentWindow;
        private CameraPanelController cameraPanel;
        private Rect windowRect;
        private bool isVisible = false;
        private bool stylesInitialized = false;
        private GUIStyle windowStyle;
        private GUIStyle toggleStyleActive;
        private GUIStyle labelStyle;

        // Camera Shake State (pending API implementation)
        private bool camToolsShake = false;
        private bool camToolsVelShake = false;
        private float shakeIntensity = 0f;

        // HullCam Overlay State (pending API implementation)  
        private int selectedOverlayIndex = 0;
        private readonly string[] overlayOptions = new string[] { "None" }; // Placeholder until API available
        #endregion
        #region Initialization
        /// <summary>
        /// Initializes the window with parent reference and panel controller.
        /// </summary>
        public void Initialize(RecordingControlsWindow parent, CameraPanelController panelController)
        {
            parentWindow = parent;
            cameraPanel = panelController;

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
            float width = CinematicUIResources.Layout.AdvancedCamera.PANEL_WIDTH;

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
                CinematicUIResources.Windows.IDs.AdvancedCameraDocked,
                windowRect,
                DrawWindow,
                AdvancedCameraOptions.WindowTitle,
                windowStyle
            );
        }
        #endregion
        #region Window Layout
        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            DrawPathTimingSection();
            GUILayout.Space(CinematicUIResources.Spacing.LARGE);
            DrawCameraShakeSection();
            GUILayout.Space(CinematicUIResources.Spacing.LARGE);
            DrawHullCamOverlaySection();
            GUILayout.Space(CinematicUIResources.Spacing.LARGE);
            DrawStatusInfo();
            GUILayout.EndVertical();
            // no DragWindow call, docked window
        }
        private void DrawPathTimingSection()
        {
            var activeSlot = cameraPanel?.SlotManager?.ActiveSlot;
            bool hasCTSlot = activeSlot?.isCameraToolsSlot == true && activeSlot.ctSettings != null;

            GUI.enabled = hasCTSlot;

            if (!hasCTSlot)
            {
                GUIStyle infoStyle = CinematicUIResources.Styles.Info();
                infoStyle.wordWrap = true;
                GUILayout.Label(AdvancedCameraOptions.NoCameraToolsSlot, infoStyle);
                GUI.enabled = true;
                return;
            }

            GUIStyle toggleStyle = activeSlot.ctSettings.LockPathingToPlaybackRate ?
                toggleStyleActive :
                CinematicUIResources.Styles.Toggle();

            bool newTiming = GUILayout.Toggle(
                activeSlot.ctSettings.LockPathingToPlaybackRate,
                AdvancedCameraOptions.PathPlaybackTimingToggle,
                toggleStyle
            );

            if (newTiming != activeSlot.ctSettings.LockPathingToPlaybackRate)
            {
                activeSlot.ctSettings.LockPathingToPlaybackRate = newTiming;
                // Also update global default for future assignments
                SessionState.CameraPathPlaybackTiming = newTiming;
            }

            GUIStyle tooltipStyle = CinematicUIResources.Styles.Help();
            tooltipStyle.wordWrap = true;
            GUILayout.Label(AdvancedCameraOptions.PathPlaybackTimingTooltip, tooltipStyle);

            GUI.enabled = true;
        }
        private void DrawCameraShakeSection()
        {
            GUIStyle headerStyle = CinematicUIResources.Styles.Header();
            GUILayout.Label(AdvancedCameraOptions.ShakeHeader, headerStyle);
            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);

            // Exclusive radio toggle logic: Shake vs Velocity Shake
            // Both can be disabled, but only one can be enabled at a time

            GUILayout.BeginHorizontal();

            GUIStyle shakeStyle = camToolsShake ? toggleStyleActive : CinematicUIResources.Styles.Toggle();
            bool newShake = GUILayout.Toggle(camToolsShake, AdvancedCameraOptions.ShakeToggle, shakeStyle,
                GUILayout.Width(CinematicUIResources.Layout.AdvancedCamera.RADIO_WIDTH));

            if (newShake && !camToolsShake)
            {
                camToolsShake = true;
                camToolsVelShake = false;
            }
            else if (!newShake && camToolsShake)
            {
                camToolsShake = false;
            }

            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            GUIStyle velShakeStyle = camToolsVelShake ? toggleStyleActive : CinematicUIResources.Styles.Toggle();
            bool newVelShake = GUILayout.Toggle(camToolsVelShake, AdvancedCameraOptions.VelocityShakeToggle, velShakeStyle,
                GUILayout.Width(CinematicUIResources.Layout.AdvancedCamera.RADIO_WIDTH));

            if (newVelShake && !camToolsVelShake)
            {
                camToolsVelShake = true;
                camToolsShake = false;
            }
            else if (!newVelShake && camToolsVelShake)
            {
                camToolsVelShake = false;
            }

            GUILayout.EndHorizontal();

            GUIStyle helpStyle = CinematicUIResources.Styles.Help();
            GUILayout.BeginHorizontal();
            GUILayout.Label(AdvancedCameraOptions.ShakeTooltip, helpStyle, GUILayout.Width(CinematicUIResources.Layout.AdvancedCamera.RADIO_WIDTH));
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);
            GUILayout.Label(AdvancedCameraOptions.VelocityShakeTooltip, helpStyle);
            GUILayout.EndHorizontal();

            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);

            GUILayout.Label(string.Format(AdvancedCameraOptions.ShakeIntensityLabel, shakeIntensity));

            float newIntensity = GUILayout.HorizontalSlider(shakeIntensity,
                CinematicUIResources.Layout.AdvancedCamera.SLIDER_MIN,
                CinematicUIResources.Layout.AdvancedCamera.SLIDER_MAX);

            if (!Mathf.Approximately(newIntensity, shakeIntensity))
            {
                shakeIntensity = newIntensity;
            }

            GUILayout.Label(AdvancedCameraOptions.IntensityTooltip, helpStyle);
        }
        private void DrawHullCamOverlaySection()
        {
            GUIStyle headerStyle = CinematicUIResources.Styles.Header();
            GUILayout.Label(AdvancedCameraOptions.OverlayHeader, headerStyle);
            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);

            // Disabled arrow selector (placeholder UI)
            GUI.enabled = false; // Disable until API is available

            GUILayout.BeginHorizontal();
            GUILayout.Label(AdvancedCameraOptions.OverlaySelectorLabel, GUILayout.Width(60f));

            if (GUILayout.Button(CinematicUIStrings.Common.arrowL, GUILayout.Width(CinematicUIResources.Layout.FPS.SELECTOR_WIDTH)) && selectedOverlayIndex > 0)
                selectedOverlayIndex--;

            GUILayout.Label(overlayOptions[selectedOverlayIndex], GUILayout.Width(100f));

            if (GUILayout.Button(CinematicUIStrings.Common.arrowR, GUILayout.Width(CinematicUIResources.Layout.FPS.SELECTOR_WIDTH)) && selectedOverlayIndex < overlayOptions.Length - 1)
                selectedOverlayIndex++;

            GUILayout.EndHorizontal();

            GUI.enabled = true;

            GUIStyle tooltipStyle = CinematicUIResources.Styles.Help();
            tooltipStyle.wordWrap = true;
            GUILayout.Label(AdvancedCameraOptions.OverlayTooltip, tooltipStyle);
        }
        private void DrawStatusInfo()
        {
            var activeSlot = cameraPanel?.SlotManager?.ActiveSlot;
            if (activeSlot?.isCameraToolsSlot == true)
            {
                GUIStyle infoStyle = CinematicUIResources.Styles.Info(small: true);
                GUILayout.Label(AdvancedCameraOptions.SettingsPersisted, infoStyle);
            }
        }
        #endregion
        #region Public API
        /// <summary>
        /// Shows the advanced camera options window and refreshes position.
        /// </summary>
        public void Show()
        {
            isVisible = true;
            // Refresh position immediately when showing
            UpdatePosition();
        }
        /// <summary>
        /// Hides the advanced camera options window.
        /// </summary>
        public void Hide()
        {
            isVisible = false;
        }
        public bool IsVisible => isVisible;

        /// <summary>
        /// Returns the current window rectangle (useful for debugging or external positioning).
        /// </summary>
        public Rect GetWindowRect() => windowRect;
        #endregion
    }
}