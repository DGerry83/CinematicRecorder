using CinematicRecorder.Core;
using CinematicRecorder.Integration;
using static CinematicRecorder.UI.CinematicUIStrings;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Controller for the camera assignment panel UI. Manages slot assignments, 
    /// camera transitions with fade effects, zoom controls, and preset persistence.
    /// </summary>
    public class CameraPanelController
    {
        #region Services
        private readonly CameraSlotManager slotManager;
        private readonly CameraTransitionCoordinator transitionCoordinator;
        private readonly ZoomControlService zoomService;
        private readonly CinematicCameraManager cameraManager;
        private readonly CameraToolsCameraController ctController;
        #endregion
        #region UI State
        private readonly GUIStyle[] cameraButtonStyles = new GUIStyle[7];
        private bool cameraPanelStylesInitialized = false;
        private bool showCameraPanel = false;
        private bool showPresetList = false;
        private bool showDeleteConfirm = false;
        private int pendingUnassignSlot = -1;
        private string presetNameBuffer = "";
        private bool _showOverwriteDialog = false;
        private string _pendingOverwritePresetName;
        private CameraPanelConfig _pendingOverwriteScenario;
        private int _lastCTSlotIndex = -1;

        private enum ZoomMode { Rate, Target }
        private ZoomMode currentZoomMode = ZoomMode.Rate;
        private float targetDuration = 0f;
        private ZoomCurve targetZoomCurve = ZoomCurve.Linear;
        private bool targetIsConsistentFraming = false;
        private float targetFOVValue = 60f;
        #endregion
        #region Cached Layout State (IMGUI Safety)
        private bool _cachedCamActive;
        private bool _cachedHasCurrentCam;
        private bool _cachedCTAvailable;
        private Vessel _cachedVessel;
        private CameraPanelConfig _cachedScenario;
        private bool _cachedHasPresets;
        private string[] _cachedPresetNames;
        #endregion
        #region External Dependencies
        private readonly MonoBehaviour host;
        private Rect parentWindowRect;
        #endregion
        #region Properties
        public bool IsVisible => showCameraPanel;
        public bool IsFading => transitionCoordinator.IsFading;
        public float FadeAlpha => transitionCoordinator.FadeAlpha;
        public bool UseFadeOnSwap => transitionCoordinator.UseFadeOnSwap;
        public CameraSlotManager SlotManager => slotManager;
        #endregion
        #region Constructor
        /// <summary>
        /// Initializes the controller with host MonoBehaviour for coroutine support.
        /// </summary>
        public CameraPanelController(MonoBehaviour hostBehaviour)
        {
            host = hostBehaviour ?? throw new ArgumentNullException(nameof(hostBehaviour));

            slotManager = new CameraSlotManager();
            transitionCoordinator = new CameraTransitionCoordinator();
            zoomService = new ZoomControlService();
            ctController = new CameraToolsCameraController();
            cameraManager = CinematicCameraManager.Instance;

            InitializeStyles();
            SubscribeToEvents();

            slotManager.OnActiveSlotChanged += OnActiveSlotChanged;
        }
        #endregion
        #region Initialization & Cleanup
        private void InitializeStyles()
        {
            if (cameraPanelStylesInitialized) return;
            for (int i = 0; i < 7; i++)
            {
                cameraButtonStyles[i] = CinematicUIResources.Styles.CameraButton(i);
            }
            cameraPanelStylesInitialized = true;
        }
        private void SubscribeToEvents()
        {
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);
            GameEvents.onVesselChange.Add(OnVesselChange);

            if (CameraPanelConfig.Instance != null)
            {
                CameraPanelConfig.Instance.OnPresetLoaded += OnPresetLoaded;
            }
        }
        private void UnsubscribeFromEvents()
        {
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
            GameEvents.onVesselChange.Remove(OnVesselChange);
        }
        /// <summary>
        /// Cleans up event subscriptions and references. Call before destroying host.
        /// </summary>
        public void Shutdown()
        {
            UnsubscribeFromEvents();
            slotManager.OnActiveSlotChanged -= OnActiveSlotChanged;

            if (CameraPanelConfig.Instance != null)
            {
                CameraPanelConfig.Instance.OnPresetLoaded -= OnPresetLoaded;
            }
        }
        #endregion
        #region Event Handlers
        private void OnVesselWillDestroy(Vessel v)
        {
            if (v == FlightGlobals.ActiveVessel)
            {
                HullCamBridge.ClearHullCamStaticState();
                cameraManager.ClearActiveSlot();
            }
        }
        private void OnGameSceneLoadRequested(GameScenes scene)
        {
            HullCamBridge.ClearHullCamStaticState();
            slotManager.HandleSceneChange();
        }
        private void OnVesselChange(Vessel v)
        {
            slotManager.HandleVesselChange();
        }
        private void OnPresetLoaded(CameraPanelPreset preset)
        {
            slotManager.LoadPreset(preset);
            presetNameBuffer = preset?.presetName ?? "";
        }
        private void OnActiveSlotChanged(int slotIndex) {}
        #endregion
        #region Main Rendering
        /// <summary>
        /// Renders the camera panel UI inside the given parent window rectangle.
        /// Call from OnGUI.
        /// </summary>
        public void Draw(Rect parentWindowRect)
        {
            this.parentWindowRect = parentWindowRect;

            if (!HullCamBridge.IsAvailable)
            {
                DrawDisabledPanel();
                return;
            }

            _cachedCamActive = cameraManager.HasActiveCamera;
            _cachedHasCurrentCam = HullCamBridge.GetCurrentCamera() != null;
            _cachedCTAvailable = new CameraToolsCameraController().IsAvailable;
            _cachedVessel = FlightGlobals.ActiveVessel;
            _cachedScenario = CameraPanelConfig.Instance;
            var presetList = _cachedScenario?.GetPresetNames();
            _cachedHasPresets = presetList?.Count > 0;
            _cachedPresetNames = presetList?.ToArray();

            try
            {
                InitializeStyles();

                GUILayout.Space(CinematicUIResources.Spacing.SECTION);
                DrawFoldoutButton();

                if (!showCameraPanel) return;

                DrawFadeControls();
                DrawGridContainer();

                if (_cachedCamActive)
                {
                    DrawZoomControls();
                }

                GUILayout.Space(CinematicUIResources.Spacing.SECTION);
                DrawProfilesInterface();
            }
            catch (ArgumentException)
            {
                // Suppress IMGUI control count mismatches during rapid camera state changes
            }
        }
        /// <summary>
        /// Renders the fade-to-black overlay. Call from OnGUI before other UI.
        /// Updates real-time fades when not in deterministic mode.
        /// </summary>
        public void DrawFadeOverlay()
        {
            if (!DeterministicCaptureSession.IsRunning)
            {
                transitionCoordinator.UpdateFade();
            }
            // Note: Deterministic fade is updated in ProcessZoomLateUpdate (per physics step)

            if (!transitionCoordinator.IsFading) return;

            if (transitionCoordinator.IsCompletingSwitch)
            {
                var activeSlot = slotManager.ActiveSlot;
                if (activeSlot != null && activeSlot.isCameraToolsSlot && activeSlot.ctSettings != null)
                {
                    var ctCam = cameraManager.ActiveCamera as CameraToolsCamera;
                    if (ctCam != null)
                    {
                        if (activeSlot.ctSettings.UseConsistentAutoZoom)
                        {
                            ctController.ApplyConsistentAutoZoom(true, activeSlot.ctSettings.ZoomPadding);
                        }
                        else if (activeSlot.ctSettings.AutoZoom)
                        {
                            ApplyNativeAutoZoom(activeSlot, ctController);
                        }
                    }
                }
            }

            GUI.color = transitionCoordinator.GetFadeColor();
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
        /// <summary>
        /// Renders confirmation dialogs (delete, unassign, overwrite). 
        /// Call from OnGUI after main window.
        /// </summary>
        public void DrawConfirmationDialogs()
        {
            DrawDeleteDialog();
            DrawUnassignDialog();
            DrawOverwriteDialog(); 
        }

        /// <summary>
        /// Processes zoom input and deterministic fade updates. 
        /// Call from LateUpdate once per frame.
        /// </summary>
        public void ProcessZoomLateUpdate()
        {
            if (!showCameraPanel) return;

            // Handle deterministic fade updates (per physics step) when recording
            if (DeterministicCaptureSession.IsRunning && transitionCoordinator.IsFading)
            {
                transitionCoordinator.UpdateDeterministicFade();
            }

            slotManager.CheckExternalDeactivation();
            var activeSlot = slotManager.ActiveSlot;

            if (DeterministicCaptureSession.IsRunning)
            {
                HandleDeterministicZoom(activeSlot);
            }
            else
            {
                HandleRealTimeZoom(activeSlot);
            }
        }
        private void HandleDeterministicZoom(CameraSlot activeSlot)
        {
            var detZoom = DeterministicCaptureSession.ActiveZoomController;
            if (detZoom == null) return;

            bool isHullCam = cameraManager.ActiveCamera is HullCamController;
            bool isCT = cameraManager.ActiveCamera is CameraToolsCamera;

            if (currentZoomMode == ZoomMode.Rate)
            {
                // ALWAYS pass rate input in Rate mode, regardless of consistent framing setting
                // Consistent framing check happens inside OnPhysicsStepped if UseConsistentAutoZoom is true
                detZoom.SetRateInput(zoomService.ZoomIntent);
                if (isCT)
                {
                    detZoom.UseConsistentAutoZoom = ctController.UseConsistentAutoZoom;
                    detZoom.ConsistentZoomPadding = ctController.ConsistentZoomPadding;
                }
                else if (isHullCam)
                {
                    detZoom.UseConsistentAutoZoom = zoomService.UseConsistentAutoZoom;
                    detZoom.ConsistentZoomPadding = zoomService.ConsistentZoomPadding;
                }
                zoomService.DecayZoomIntent(Time.deltaTime);
            }
            else // Target mode
            {
                // Interrupt target zoom with rate slider
                if (Mathf.Abs(zoomService.ZoomIntent) > 0.1f)
                {
                    detZoom.Interrupt(new RateBasedZoomStrategy(60f));
                    detZoom.SetRateInput(zoomService.ZoomIntent);
                    zoomService.DecayZoomIntent(Time.deltaTime);
                }
            }
        }
        private void HandleRealTimeZoom(CameraSlot activeSlot)
        {
            ICamera activeCam = cameraManager.ActiveCamera;
            bool isCT = activeCam is CameraToolsCamera;
            bool isHullCam = activeCam is HullCamController;

            // For CT: Check both slot settings (saved state) AND controller (active transition state)
            // This catches instant transitions that set controller.UseConsistentAutoZoom immediately
            bool ctConsistentEnabled = (activeSlot?.ctSettings?.UseConsistentAutoZoom ?? false) ||
                                       (isCT && ctController.UseConsistentAutoZoom);
            bool hullConsistentEnabled = zoomService.UseConsistentAutoZoom;

            if (currentZoomMode == ZoomMode.Rate)
            {
                if (isHullCam)
                {
                    if (hullConsistentEnabled)
                        zoomService.ApplyConsistentFramingToHullCam();
                    else
                    {
                        zoomService.SetRateInput(zoomService.ZoomIntent);
                        zoomService.Update();
                    }
                    zoomService.DecayZoomIntent(Time.deltaTime);
                }
                else if (isCT)
                {
                    if (ctConsistentEnabled)
                        ctController.ApplyConsistentFraming();
                    else
                        ctController.UpdateRate(zoomService.ZoomIntent);

                    zoomService.DecayZoomIntent(Time.deltaTime);
                }
            }
            else // Target mode
            {
                if (isHullCam)
                {
                    if (hullConsistentEnabled)
                        zoomService.ApplyConsistentFramingToHullCam();
                    else
                        zoomService.Update();
                }
                else if (isCT)
                {
                    if (ctConsistentEnabled)
                    {
                        ctController.ApplyConsistentFraming();
                    }
                    else
                    {
                        bool hadStrategy = ctController.HasActiveStrategy;
                        ctController.UpdateTarget();

                        // Handoff: When consistent transition completes, copy to slot settings
                        if (hadStrategy && !ctController.HasActiveStrategy && ctController.UseConsistentAutoZoom && activeSlot?.ctSettings != null)
                        {
                            activeSlot.ctSettings.UseConsistentAutoZoom = true;
                        }
                    }
                }
            }
        }
        #endregion
        #region UI Sections
        private void DrawDisabledPanel()
        {
            GUIStyle disabledStyle = CinematicUIResources.Styles.Label(
                CinematicUIResources.Colors.TEXT_DIM,
                alignment: TextAnchor.MiddleCenter
            );
            GUILayout.Label(CameraController.RequiresHullCam, disabledStyle);
        }
        private void DrawFoldoutButton()
        {
            string label = showCameraPanel ? CameraController.FoldoutCollapse : CameraController.FoldoutExpand;
            if (GUILayout.Button(label, HighLogic.Skin.button))
            {
                showCameraPanel = !showCameraPanel;
            }
        }
        private void DrawFadeControls()
        {
            GUILayout.BeginHorizontal();
            GUIStyle toggleStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (transitionCoordinator.UseFadeOnSwap)
            {
                toggleStyle.normal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.onNormal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.fontStyle = FontStyle.Bold;
            }
            transitionCoordinator.UseFadeOnSwap = GUILayout.Toggle(transitionCoordinator.UseFadeOnSwap, CameraController.FadeOnSwapToggle, toggleStyle);
            GUILayout.EndHorizontal();

            if (transitionCoordinator.UseFadeOnSwap)
            {
                float duration = Mathf.Lerp(
                    CinematicUIResources.Layout.Crossfade.DURATION_MIN,
                    CinematicUIResources.Layout.Crossfade.DURATION_MAX,
                    transitionCoordinator.FadeDurationSlider
                );
                GUILayout.Label(string.Format(CameraController.FadeDurationFormat, duration), HighLogic.Skin.label);
                transitionCoordinator.FadeDurationSlider = GUILayout.HorizontalSlider(
                    transitionCoordinator.FadeDurationSlider,
                    0f,
                    CinematicUIResources.Layout.Crossfade.SLIDER_MAX
                );
                GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
            }
            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
        }
        private void DrawGridContainer()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();

            // LEFT: Camera Grid + Return Button
            GUILayout.BeginVertical(GUILayout.Width(CinematicUIResources.Layout.Camera.GRID_COLUMN_WIDTH));
            DrawGrid();

            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            bool ctActive = ctController.IsActive;
            bool hullCamActive = HullCamBridge.IsAnyCameraActive();
            bool hasActiveCam = ctActive || hullCamActive;
            GUI.enabled = hasActiveCam;
            if (GUILayout.Button(CameraController.ReturnToMain, GUILayout.Height(CinematicUIResources.Layout.SpeedControl.BUTTON_HEIGHT)))
            {
                CaptureCTStateIfActive();
                BeginCameraSwitch(() =>
                {
                    cameraManager.ReturnToMain(immediate: true);
                    slotManager.ClearActiveSlot();
                    _lastCTSlotIndex = -1;
                });
            }
            GUI.enabled = true;
            GUILayout.EndVertical();

            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            // RIGHT: Consistent Framing controls for BOTH camera types
            GUILayout.BeginVertical(GUILayout.Width(CinematicUIResources.Layout.Camera.GRID_TEXT_COLUMN_WIDTH));

            ICamera activeCam = cameraManager.ActiveCamera;
            if (activeCam is CameraToolsCamera)
            {
                var activeSlot = slotManager.ActiveSlot;
                if (activeSlot?.ctSettings != null)
                {
                    DrawConsistentFramingControls(
                        activeSlot.ctSettings.UseConsistentAutoZoom,
                        activeSlot.ctSettings.ZoomPadding,
                        activeCam.FieldOfView,
                        (newVal) => {
                            activeSlot.ctSettings.UseConsistentAutoZoom = newVal;
                            ctController.UseConsistentAutoZoom = newVal;
                            if (newVal) ctController.ApplyConsistentFraming();
                        },
                        (newVal) => {
                            activeSlot.ctSettings.ZoomPadding = newVal;
                            ctController.ConsistentZoomPadding = newVal;
                            if (activeSlot.ctSettings.UseConsistentAutoZoom) ctController.ApplyConsistentFraming();
                        }
                    );
                }
                else
                {
                    DrawInstructions();
                }
            }
            else if (activeCam is HullCamController)
            {
                DrawConsistentFramingControls(
                    zoomService.UseConsistentAutoZoom,
                    zoomService.ConsistentZoomPadding,
                    zoomService.CurrentFoV,
                    (newVal) => {
                        zoomService.UseConsistentAutoZoom = newVal;
                        if (newVal) zoomService.ApplyConsistentFramingToHullCam();
                    },
                    (newVal) => {
                        zoomService.ConsistentZoomPadding = newVal;
                        if (zoomService.UseConsistentAutoZoom) zoomService.ApplyConsistentFramingToHullCam();
                    }
                );
            }
            else
            {
                DrawInstructions();
            }

            GUILayout.FlexibleSpace();

            GUI.enabled = _cachedHasCurrentCam;
            if (GUILayout.Button(CameraController.AssignCurrent, GUILayout.Height(CinematicUIResources.Layout.SpeedControl.BUTTON_HEIGHT)))
            {
                AssignCurrentToFirstOpenSlot();
            }
            GUI.enabled = true;
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
        private void DrawConsistentFramingControls(
            bool useConsistent,
            float padding,
            float currentFOV,
            Action<bool> onToggleChanged,
            Action<float> onPaddingChanged)
        {
            GUIStyle header = CinematicUIResources.Styles.Header();
            GUILayout.Label(CameraController.AutoZoomHeader, header);
            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);

            GUIStyle toggleStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (useConsistent)
            {
                toggleStyle.normal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.onNormal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.fontStyle = FontStyle.Bold;
            }

            bool newUseConsistent = GUILayout.Toggle(
                useConsistent,
                CameraController.ConsistentFramingToggle,
                toggleStyle
            );

            if (newUseConsistent != useConsistent)
            {
                onToggleChanged(newUseConsistent);
            }

            if (useConsistent)
            {
                GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
                GUILayout.Label(string.Format(CameraController.PaddingLabel, padding),
                    HighLogic.Skin.label);

                float newPadding = GUILayout.HorizontalSlider(padding, 0.5f, 3.0f);
                if (!Mathf.Approximately(newPadding, padding))
                {
                    onPaddingChanged(newPadding);
                }

                GUILayout.Label(CameraController.PaddingTooltip, CinematicUIResources.Styles.Help());
            }

            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            GUIStyle infoStyle = CinematicUIResources.Styles.Label(CinematicUIResources.Colors.INFO_ORANGE,
                fontSize: CinematicUIResources.Typography.INFO);
            GUILayout.Label(string.Format(CameraController.CurrentFOVFormat, currentFOV), infoStyle);
        }
        private void DrawGrid()
        {
            Vessel currentVessel = _cachedVessel;

            for (int row = 0; row < CinematicUIResources.Layout.Camera.GRID_ROWS; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < CinematicUIResources.Layout.Camera.GRID_COLS; col++)
                {
                    int index = row * 4 + col;
                    DrawCameraButton(index, currentVessel);
                }
                GUILayout.EndHorizontal();
            }
        }
        private void DrawCameraButton(int index, Vessel currentVessel)
        {
            CameraSlot.SlotStatus status = slotManager.GetSlotStatus(index, currentVessel);
            var slot = slotManager.GetSlot(index);
            int styleIndex = GetStyleIndexForStatus(status, slot?.isCameraToolsSlot ?? false);
            string buttonLabel = (index + 1).ToString();

            Rect buttonRect = GUILayoutUtility.GetRect(
                CinematicUIResources.Layout.Camera.BUTTON_SIZE,
                CinematicUIResources.Layout.Camera.BUTTON_HEIGHT,
                cameraButtonStyles[styleIndex]);

            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 1 && buttonRect.Contains(evt.mousePosition))
            {
                if (status != CameraSlot.SlotStatus.Unassigned)
                {
                    pendingUnassignSlot = index;
                    evt.Use();
                }
            }
            else if (GUI.Button(buttonRect, buttonLabel, cameraButtonStyles[styleIndex]))
            {
                OnButtonClicked(index);
            }
        }
        private void DrawInstructions()
        {
            GUIStyle header = CinematicUIResources.Styles.Header();
            GUILayout.Label(CameraController.ControlsHeader, header);
            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);

            GUIStyle small = CinematicUIResources.Styles.Label(Color.white, fontSize: CinematicUIResources.Typography.INFO);
            small.wordWrap = true;
            GUILayout.Label(CameraController.ControlLeftClick, small);
            GUILayout.Label(CameraController.ControlRightClick, small);
            GUILayout.Label(CameraController.ControlAssignCurrent, small);
        }
        private void DrawZoomControls()
        {
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();

            GUIStyle modeStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (currentZoomMode == ZoomMode.Rate)
            {
                modeStyle.normal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                modeStyle.fontStyle = FontStyle.Bold;
            }
            bool wantRateMode = GUILayout.Toggle(currentZoomMode == ZoomMode.Rate, CameraController.RateModeToggle, modeStyle, GUILayout.Width(85f));

            GUILayout.Space(10f);
            GUIStyle targetStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (currentZoomMode == ZoomMode.Target)
            {
                targetStyle.normal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                targetStyle.fontStyle = FontStyle.Bold;
            }
            bool wantTargetMode = GUILayout.Toggle(currentZoomMode == ZoomMode.Target, CameraController.TargetModeToggle, targetStyle, GUILayout.Width(85f));

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (wantRateMode && currentZoomMode != ZoomMode.Rate)
            {
                zoomService.CancelActiveZoom();
                ctController.CancelActiveZoom();
                if (DeterministicCaptureSession.IsRunning)
                {
                    DeterministicCaptureSession.ActiveZoomController?.Clear();
                }
                currentZoomMode = ZoomMode.Rate;
            }
            else if (wantTargetMode && currentZoomMode != ZoomMode.Target)
            {
                zoomService.CancelActiveZoom();
                ctController.CancelActiveZoom();
                if (DeterministicCaptureSession.IsRunning)
                {
                    DeterministicCaptureSession.ActiveZoomController?.Clear();
                }
                zoomService.ZoomIntent = 0f;
                currentZoomMode = ZoomMode.Target;
            }

            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);

            if (currentZoomMode == ZoomMode.Rate)
            {
                DrawRateModeControls();
            }
            else
            {
                DrawTargetModeControls();
            }
            GUILayout.EndVertical();
        }
        private void DrawRateModeControls()
        {
            GUILayout.Label(CameraController.ZoomControlLabel, HighLogic.Skin.label);

            GUILayout.BeginHorizontal();
            GUILayout.Label(CameraController.ZoomOut, GUILayout.Width(CinematicUIResources.Layout.Zoom.LABEL_WIDTH));

            GUIStyle intentStyle = new GUIStyle(HighLogic.Skin.horizontalSlider);
            GUIStyle thumbStyle = new GUIStyle(HighLogic.Skin.horizontalSliderThumb);
            zoomService.ZoomIntent = GUILayout.HorizontalSlider(zoomService.ZoomIntent, -1f, 1f, intentStyle, thumbStyle);

            GUILayout.Label(CameraController.ZoomIn, GUILayout.Width(CinematicUIResources.Layout.Zoom.LABEL_WIDTH));
            GUILayout.EndHorizontal();

            float maxFov = cameraManager.GetMaxFOV();
            GUILayout.Label(string.Format(CameraController.FOVFormat, zoomService.CurrentFoV, maxFov), HighLogic.Skin.label);

            if (GUILayout.Button(CameraController.ResetZoom, GUILayout.Width(CinematicUIResources.Layout.Zoom.RESET_BUTTON_WIDTH)))
            {
                if (DeterministicCaptureSession.IsRunning)
                {
                    var detZoom = DeterministicCaptureSession.ActiveZoomController;
                    if (detZoom != null)
                        detZoom.Interrupt(new InstantZoomStrategy(maxFov));
                }
                else
                {
                    zoomService.ResetZoom(maxFov);
                    ctController.ResetZoom();
                }
            }
        }
        private void DrawTargetModeControls()
        {
            bool newConsistentTarget = GUILayout.Toggle(targetIsConsistentFraming, CameraController.TargetConsistentFramingToggle);
            if (newConsistentTarget != targetIsConsistentFraming)
            {
                targetIsConsistentFraming = newConsistentTarget;
            }

            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);

            if (!targetIsConsistentFraming)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(CameraController.TargetFOVLabel, GUILayout.Width(80f));
                string targetStr = GUILayout.TextField(targetFOVValue.ToString("F1"), GUILayout.Width(60f));
                if (float.TryParse(targetStr, out float parsedTarget))
                {
                    targetFOVValue = Mathf.Clamp(parsedTarget, 2f, 120f);
                }
                GUILayout.Label("°", GUILayout.Width(20f));
                GUILayout.EndHorizontal();
                GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
            }
            GUILayout.Label(string.Format(CameraController.DurationLabel, targetDuration), HighLogic.Skin.label);
            GUILayout.BeginHorizontal();
            GUILayout.Label("0.0", GUILayout.Width(30f));
            targetDuration = GUILayout.HorizontalSlider(targetDuration, 0f, 5f);
            GUILayout.Label("5.0", GUILayout.Width(30f));
            GUILayout.EndHorizontal();
            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);

            GUILayout.BeginHorizontal();
            GUILayout.Label(CameraController.CurveLabel, GUILayout.Width(50f));
            string[] curveOptions = new string[]
            {
                CameraController.CurveLinear,
                CameraController.CurveEaseIn,
                CameraController.CurveEaseOut,
                CameraController.CurveEaseInOut
            };
            int selectedCurve = (int)targetZoomCurve;
            selectedCurve = GUILayout.SelectionGrid(selectedCurve, curveOptions, 2);
            targetZoomCurve = (ZoomCurve)selectedCurve;
            GUILayout.EndHorizontal();
            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);

            GUIStyle goStyle = CinematicUIResources.Styles.ColoredButton(
                CinematicUIResources.Colors.GLOW_GREEN,
                Color.black,
                FontStyle.Bold
            );

            if (GUILayout.Button(CameraController.GoButton, goStyle, GUILayout.Height(30f)))
            {
                ExecuteTargetZoom();
            }
        }
        private void ExecuteTargetZoom()
        {
            if (!targetIsConsistentFraming && targetFOVValue <= 0)
                targetFOVValue = cameraManager.GetCurrentFOV();

            if (DeterministicCaptureSession.IsRunning)
            {
                var detZoom = DeterministicCaptureSession.ActiveZoomController;
                if (detZoom == null) return;

                if (targetIsConsistentFraming)
                {
                    detZoom.QueueConsistentFramingTransition(targetDuration, targetZoomCurve, zoomService.ConsistentZoomPadding);
                }
                else
                {
                    if (targetDuration < 0.001f)
                        detZoom.Interrupt(new InstantZoomStrategy(targetFOVValue));
                    else
                        detZoom.Interrupt(new TargetBasedZoomStrategy(targetFOVValue, targetDuration, targetZoomCurve));
                }
            }
            else
            {
                ICamera activeCam = cameraManager.ActiveCamera;

                if (targetIsConsistentFraming)
                {
                    if (activeCam is CameraToolsCamera ctCam)
                    {
                        ctController.QueueConsistentTransition(targetDuration, targetZoomCurve);
                        // For instant, update slot immediately so UI reflects it next frame
                        if (targetDuration < 0.001f && slotManager.ActiveSlot?.ctSettings != null)
                        {
                            slotManager.ActiveSlot.ctSettings.UseConsistentAutoZoom = true;
                        }
                    }
                    else if (activeCam is HullCamController)
                    {
                        zoomService.QueueConsistentTransition(targetDuration, targetZoomCurve);
                    }
                }
                else
                {
                    if (activeCam is CameraToolsCamera)
                        ctController.QueueTargetZoom(targetFOVValue, targetDuration, targetZoomCurve);
                    else if (activeCam is HullCamController)
                        zoomService.QueueTargetZoom(targetFOVValue, targetDuration, targetZoomCurve);
                }
            }
        }
        private void DrawProfilesInterface()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);

            GUILayout.BeginHorizontal();

            CameraPanelPreset activePreset = _cachedScenario?.GetActivePreset();
            EnsurePresetNameBuffer();

            presetNameBuffer = GUILayout.TextField(presetNameBuffer, GUILayout.Width(150));

            if (GUILayout.Button(CameraController.SavePreset, GUILayout.Width(50)))
            {
                SavePreset(_cachedScenario);
            }

            GUI.enabled = activePreset != null;
            if (GUILayout.Button(CameraController.DeletePreset, GUILayout.Width(50)))
            {
                _cachedScenario?.DeletePreset(presetNameBuffer);
                presetNameBuffer = GetDefaultPresetName();
                showDeleteConfirm = false;
            }
            GUI.enabled = true;

            GUILayout.FlexibleSpace();

            GUI.enabled = _cachedHasPresets;
            if (GUILayout.Button(CameraController.LoadPreset, GUILayout.Width(60)))
            {
                showPresetList = !showPresetList;
            }
            GUI.enabled = true;

            GUILayout.EndHorizontal();

            if (showPresetList && _cachedPresetNames != null)
            {
                DrawPresetDropdown();
            }

            GUILayout.EndVertical();
        }
        private void DrawPresetDropdown()
        {
            GUILayout.BeginVertical(GUI.skin.box);

            foreach (string name in _cachedPresetNames)
            {
                if (GUILayout.Button(name))
                {
                    _cachedScenario?.LoadPreset(name);
                    showPresetList = false;
                }
            }

            GUILayout.EndVertical();
        }
        #endregion
        #region Dialogs
        private void DrawDeleteDialog()
        {
            if (!showDeleteConfirm) return;

            Rect dialogRect = new Rect(
                parentWindowRect.x + CinematicUIResources.Layout.Dialog.OFFSET_X,
                parentWindowRect.y + CinematicUIResources.Layout.Dialog.OFFSET_Y,
                CinematicUIResources.Layout.Dialog.WIDTH,
                CinematicUIResources.Layout.Dialog.HEIGHT
            );

            GUI.ModalWindow(CinematicUIResources.Windows.IDs.DialogDelete, dialogRect, (id) =>
            {
                GUILayout.Label(string.Format(CameraController.DeleteConfirmFormat, presetNameBuffer));
                GUILayout.Space(CinematicUIResources.Spacing.SECTION);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Common.Yes, GUILayout.Height(CinematicUIResources.Layout.Dialog.BUTTON_HEIGHT)))
                {
                    _cachedScenario?.DeletePreset(presetNameBuffer);
                    presetNameBuffer = GetDefaultPresetName();
                    showDeleteConfirm = false;
                }

                if (GUILayout.Button(Common.No, GUILayout.Height(CinematicUIResources.Layout.Dialog.BUTTON_HEIGHT)))
                {
                    showDeleteConfirm = false;
                }
                GUILayout.EndHorizontal();
            }, CameraController.ConfirmDeleteTitle);
        }
        private void DrawUnassignDialog()
        {
            if (pendingUnassignSlot < 0) return;

            Rect dialogRect = new Rect(
                parentWindowRect.x + CinematicUIResources.Layout.Dialog.OFFSET_X,
                parentWindowRect.y + CinematicUIResources.Layout.Dialog.OFFSET_Y,
                CinematicUIResources.Layout.Dialog.WIDTH,
                CinematicUIResources.Layout.Dialog.HEIGHT
            );
            int slotIndex = pendingUnassignSlot;

            GUI.ModalWindow(CinematicUIResources.Windows.IDs.DialogUnassign, dialogRect, (id) =>
            {
                GUILayout.Label(string.Format(CameraController.UnassignConfirmFormat, slotIndex + 1));
                GUILayout.Space(CinematicUIResources.Spacing.SECTION);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Common.Yes, GUILayout.Height(CinematicUIResources.Layout.Dialog.BUTTON_HEIGHT)))
                {
                    slotManager.ClearSlot(slotIndex);
                    pendingUnassignSlot = -1;
                }

                if (GUILayout.Button(Common.No, GUILayout.Height(CinematicUIResources.Layout.Dialog.BUTTON_HEIGHT)))
                {
                    pendingUnassignSlot = -1;
                }
                GUILayout.EndHorizontal();
            }, CameraController.ConfirmUnassignTitle);
        }
        #endregion
        #region Camera Interaction
        private void OnButtonClicked(int index)
        {
            Debug.Log($"[FOV Debug] === CLICK START === index={index}, _lastCTSlotIndex={_lastCTSlotIndex}");

            // Capture current CT state before switching away
            
            var slot = slotManager.GetSlot(index);
            Debug.Log($"[FOV Debug] Switching to slot {index}, isCT={slot?.isCameraToolsSlot}");

            if (slot?.isCameraToolsSlot == true)
            {
                if (slot.ctSettings == null) return;

                if (slot.ctSettings.Mode == ToolModes.Pathing)
                {
                    if (slot.ctSettings.SelectedPathIndex < 0)
                    {
                        ScreenMessages.PostScreenMessage("Cannot activate - invalid path index", 2f); // Belongs in UI strings
                        return;
                    }
                    if (!ctController.PathExists(slot.ctSettings.SelectedPathIndex))
                    {
                        ScreenMessages.PostScreenMessage("Saved path no longer exists", 2f); // Belongs in UI strings.
                        return;
                    }
                }
                if (_lastCTSlotIndex >= 0 && _lastCTSlotIndex != index)
                {
                    CaptureCTStateIfActive();
                }

                BeginCameraSwitch(() =>
                {
                    ctController.UseConsistentAutoZoom = slot.ctSettings.UseConsistentAutoZoom;
                    ctController.ConsistentZoomPadding = slot.ctSettings.ZoomPadding;
                    ctController.CancelActiveZoom();

                    slotManager.SetActiveSlot(index);
                    _lastCTSlotIndex = index;
                    Debug.Log($"[FOV Debug] Set _lastCTSlotIndex = {index}");

                    cameraManager.SwitchToCamera(slot, immediate: true);

                    if (slot.ctSettings.UseConsistentAutoZoom)
                    {
                        ctController.ApplyConsistentFraming();
                    }
                    else if (slot.ctSettings.AutoZoom)
                    {
                        ApplyNativeAutoZoom(slot, ctController);
                    }

                    var ctCam = cameraManager.ActiveCamera as CameraToolsCamera;
                    if (ctCam != null && slot.ctSettings.UseGeographicPosition)
                    {
                        if (ctController.HasPendingGeographicRestoration())
                        {
                            ctController.PostActivationPositionFixup();
                        }
                    }
                });
                return;
            }

            _lastCTSlotIndex = -1;
            Debug.Log($"[FOV Debug] Reset _lastCTSlotIndex to -1 (HullCam/non-CT)");

            Vessel vessel = FlightGlobals.ActiveVessel;
            CameraSlot.SlotStatus status = slot.GetStatus(vessel);

            switch (status)
            {
                case CameraSlot.SlotStatus.Unassigned:
                    AssignCurrentToSlot(index);
                    break;
                case CameraSlot.SlotStatus.Active:
                    return;
                case CameraSlot.SlotStatus.Assigned:
                case CameraSlot.SlotStatus.Remote:
                    BeginCameraSwitch(() =>
                    {
                        slotManager.SetActiveSlot(index);
                        cameraManager.SwitchToCamera(slot, immediate: true);
                    });
                    break;
                case CameraSlot.SlotStatus.Unavailable:
                    ScreenMessages.PostScreenMessage(CameraController.CameraUnavailable, 2f);
                    break;
            }

            Debug.Log($"[FOV Debug] === CLICK END ===");
        }
        private void BeginCameraSwitch(Action cameraAction)
        {
            bool useDeterministic = DeterministicCaptureSession.IsRunning;
            transitionCoordinator.BeginTransition(cameraAction, useDeterministic);
        }
        private void CaptureCTStateIfActive()
        {
            if (_lastCTSlotIndex >= 0)
            {
                var slot = slotManager.GetSlot(_lastCTSlotIndex);
                if (slot?.isCameraToolsSlot == true && ctController.IsAvailable && ctController.IsActive)
                {
                    // Use API to get current FOV instead of reflection
                    float currentFOV = CameraToolsAPIManager.GetActualFOV();

                    if (currentFOV > 0)
                    {
                        // Clone then modify to avoid mutating stored reference
                        var newSettings = slot.ctSettings.Clone();
                        newSettings.ManualFOV = currentFOV;
                        newSettings.UseConsistentAutoZoom = ctController.UseConsistentAutoZoom;
                        newSettings.ZoomPadding = ctController.ConsistentZoomPadding;

                        // Re-assign to trigger property setter cloning
                        slot.ctSettings = newSettings;

                        UnityEngine.Debug.Log($"[FOV Capture] Slot {_lastCTSlotIndex}: Updated ManualFOV to {currentFOV:F1} via API");
                    }
                }
            }
        }
        private void AssignCurrentToSlot(int index)
        {
            if (ctController.IsAvailable && ctController.IsActive)
            {
                var settings = ctController.CaptureCurrentSettings();
                if (settings != null)
                {
                    settings.LockPathingToPlaybackRate = SessionState.CameraPathPlaybackTiming;
                    settings.UseDeterministicControl = DeterministicCaptureSession.IsRunning;

                    UnityEngine.Debug.Log($"[AssignCurrentToSlot] Slot {index}: Capturing CT {settings.Mode} " +
                        $"(PathIndex: {settings.SelectedPathIndex}, UsePlaybackTiming: {settings.LockPathingToPlaybackRate})");

                    if (slotManager.AssignCameraToolsToSlot(index, settings))
                    {
                        ScreenMessages.PostScreenMessage(string.Format(CameraController.SavedCameraToolsFormat, settings.GetDisplayName()), 2f);
                    }
                    return;
                }
                else
                {
                    UnityEngine.Debug.LogError($"[AssignCurrentToSlot] Failed to capture CT settings for slot {index}");
                }
            }

            if (!ValidateAssignmentPrerequisites(out object currentCam, out Vessel vessel)) return;
            if (slotManager.AssignHullCamToSlot(index, currentCam, vessel))
            {
                string camName = HullCamBridge.GetCameraName(currentCam) ?? "";
                ScreenMessages.PostScreenMessage(string.Format(CameraController.SavedHullCamFormat, camName), 2f);
            }
        }
        private void AssignCurrentToFirstOpenSlot()
        {
            int openSlot = slotManager.FindFirstOpenSlot();
            if (openSlot >= 0)
            {
                AssignCurrentToSlot(openSlot);
            }
        }
        private bool ValidateAssignmentPrerequisites(out object currentCam, out Vessel vessel)
        {
            currentCam = null;
            vessel = null;

            if (!HullCamBridge.IsAvailable) return false;

            currentCam = HullCamBridge.GetCurrentCamera();
            if (currentCam == null)
            {
                ScreenMessages.PostScreenMessage(CameraController.NoCameraToAssign, 2f);
                return false;
            }

            vessel = _cachedVessel;
            return vessel != null;
        }
        private void ApplyNativeAutoZoom(CameraSlot slot, CameraToolsCameraController controller)
        {
            Vessel currentVessel = _cachedVessel;
            if (currentVessel == null || FlightCamera.fetch == null) return;

            Vector3 targetPos = (slot.ctSettings.HasTarget && !slot.ctSettings.TargetSelf)
                ? controller.CamTarget?.transform.position ?? currentVessel.CoM
                : currentVessel.CoM;

            float distance = Vector3.Distance(FlightCamera.fetch.transform.position, targetPos);
            float margin = 30f;
            float nativeFOV = (7000f / (distance + 100f)) - 14f + margin;
            nativeFOV = Mathf.Clamp(nativeFOV, 2f, 60f);

            controller.EnforceAutoZoomFOVImmediate(nativeFOV);
            FlightCamera.fetch.SetFoV(nativeFOV);
        }
        #endregion
        #region Preset Management
        private void SavePreset(CameraPanelConfig scenario)
        {
            if (scenario == null) return;

            string nameToSave = string.IsNullOrWhiteSpace(presetNameBuffer)
                ? GetDefaultPresetName()
                : presetNameBuffer;

            // Check if this exact name exists
            var existingNames = scenario.GetPresetNames();

            if (existingNames.Contains(nameToSave))
            {
                // Prompt for overwrite
                ShowOverwriteDialog(nameToSave, scenario);
                return;
            }

            string uniqueName = GetUniquePresetName(nameToSave, existingNames);

            if (uniqueName != nameToSave)
            {
                SavePresetWithName(scenario, uniqueName);
                presetNameBuffer = uniqueName;
            }
            else
            {
                SavePresetWithName(scenario, nameToSave);
                presetNameBuffer = nameToSave;
            }
        }
        private void ShowOverwriteDialog(string presetName, CameraPanelConfig scenario)
        {
            // Store state for dialog callback
            _pendingOverwritePresetName = presetName;
            _pendingOverwriteScenario = scenario;
            _showOverwriteDialog = true;
        }
        private void DrawOverwriteDialog()
        {
            if (!_showOverwriteDialog) return;

            Rect dialogRect = new Rect(
                parentWindowRect.x + CinematicUIResources.Layout.Dialog.OFFSET_X,
                parentWindowRect.y + CinematicUIResources.Layout.Dialog.OFFSET_Y,
                CinematicUIResources.Layout.Dialog.WIDTH + 100, 
                CinematicUIResources.Layout.Dialog.HEIGHT
            );

            GUI.ModalWindow(99997, dialogRect, (id) =>
            {
                GUILayout.Label(string.Format(CameraController.OverwriteConfirm, _pendingOverwritePresetName));
                GUILayout.Space(CinematicUIResources.Spacing.SECTION);

                GUILayout.BeginHorizontal();

                // Overwrite button
                if (GUILayout.Button(CameraController.OverwriteYes, GUILayout.Height(CinematicUIResources.Layout.Dialog.BUTTON_HEIGHT)))
                {
                    // Overwrite existing
                    SavePresetWithName(_pendingOverwriteScenario, _pendingOverwritePresetName);
                    presetNameBuffer = _pendingOverwritePresetName;
                    _showOverwriteDialog = false;
                }

                // Create New button - generates [i] variant
                if (GUILayout.Button(CameraController.OverwriteNo, GUILayout.Height(CinematicUIResources.Layout.Dialog.BUTTON_HEIGHT)))
                {
                    var existingNames = _pendingOverwriteScenario.GetPresetNames();
                    string newName = GetUniquePresetName(_pendingOverwritePresetName, existingNames);
                    SavePresetWithName(_pendingOverwriteScenario, newName);
                    presetNameBuffer = newName;
                    _showOverwriteDialog = false;
                }

                GUILayout.EndHorizontal();

                GUILayout.Space(CinematicUIResources.Spacing.TIGHT);

                // Cancel button
                if (GUILayout.Button(Common.Cancel, GUILayout.Height(CinematicUIResources.Layout.Dialog.BUTTON_HEIGHT)))
                {
                    _showOverwriteDialog = false;
                }
            }, CameraController.ConfirmOverwriteTitle);
        }
        private void SavePresetWithName(CameraPanelConfig scenario, string name)
        {
            scenario.SavePreset(name, false, new List<CameraSlot>(slotManager.Slots),
                parentWindowRect.x, parentWindowRect.y);
        }
        private string GetUniquePresetName(string baseName, List<string> existingNames)
        {
            // If base name doesn't exist, use it as-is
            if (!existingNames.Contains(baseName))
                return baseName;

            string rootName = baseName;
            int existingIndex = 0;

            int bracketStart = baseName.LastIndexOf('[');
            int bracketEnd = baseName.LastIndexOf(']');

            if (bracketStart > 0 && bracketEnd > bracketStart && bracketEnd == baseName.Length - 1)
            {
                string numStr = baseName.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                if (int.TryParse(numStr, out int parsedNum))
                {
                    rootName = baseName.Substring(0, bracketStart);
                    existingIndex = parsedNum;
                }
            }

            int counter = existingIndex + 1;
            string candidate;

            do
            {
                candidate = $"{rootName}[{counter}]";
                counter++;
            } while (existingNames.Contains(candidate));

            return candidate;
        }
        private string GetDefaultPresetName()
        {
            return _cachedVessel?.vesselName ?? CameraController.Preset;
        }
        private void EnsurePresetNameBuffer()
        {
            if (string.IsNullOrEmpty(presetNameBuffer))
            {
                presetNameBuffer = GetDefaultPresetName();
            }
        }
        #endregion
        #region Helpers
        private int GetStyleIndexForStatus(CameraSlot.SlotStatus status, bool isCameraTools)
        {
            if (isCameraTools)
            {
                switch (status)
                {
                    case CameraSlot.SlotStatus.Active: return 5;
                    case CameraSlot.SlotStatus.Assigned: return 6;
                    default: return 3;
                }
            }

            switch (status)
            {
                case CameraSlot.SlotStatus.Active: return 0;
                case CameraSlot.SlotStatus.Assigned: return 1;
                case CameraSlot.SlotStatus.Unavailable: return 2;
                case CameraSlot.SlotStatus.Remote: return 4;
                default: return 3;
            }
        }
        #endregion
    }
}