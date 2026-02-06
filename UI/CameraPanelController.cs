using CinematicRecorder.Core;
using CinematicRecorder.Integration;
using static CinematicRecorder.UI.CinematicUIStrings;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CinematicRecorder.UI
{
    public class CameraPanelController
    {
        #region Services
        private readonly CameraSlotManager slotManager;
        private readonly CameraTransitionCoordinator transitionCoordinator;
        private readonly ZoomControlService zoomService;
        private readonly CinematicCameraManager cameraManager;
        #endregion

        #region UI State
        private readonly GUIStyle[] cameraButtonStyles = new GUIStyle[7];
        private bool cameraPanelStylesInitialized = false;
        private bool showCameraPanel = false;
        private bool showPresetList = false;
        private bool showDeleteConfirm = false;
        private int pendingUnassignSlot = -1;
        private string presetNameBuffer = "";
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
        public CameraPanelController(MonoBehaviour hostBehaviour)
        {
            host = hostBehaviour ?? throw new ArgumentNullException(nameof(hostBehaviour));

            slotManager = new CameraSlotManager();
            transitionCoordinator = new CameraTransitionCoordinator();
            zoomService = new ZoomControlService();
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

        public void Shutdown()
        {
            UnsubscribeFromEvents();
            slotManager.OnActiveSlotChanged -= OnActiveSlotChanged;

            if (CameraPanelConfig.Instance != null)
            {
                CameraPanelConfig.Instance.OnPresetLoaded -= OnPresetLoaded;
            }
        }

        private void UnsubscribeFromEvents()
        {
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
            GameEvents.onVesselChange.Remove(OnVesselChange);
        }
        #endregion

        #region Event Handlers
        private void OnVesselWillDestroy(Vessel v)
        {
            if (v == FlightGlobals.ActiveVessel)
            {
                // Always clear HullCam static state when vessel destroyed, regardless of tracking
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

        private void OnActiveSlotChanged(int slotIndex)
        {
            // Slot changed externally
        }
        #endregion

        #region Main Rendering
        public void Draw(Rect parentWindowRect)
        {
            this.parentWindowRect = parentWindowRect;

            if (!HullCamBridge.IsAvailable)
            {
                DrawDisabledPanel();
                return;
            }

            // Cache structural state
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

        public void DrawFadeOverlay()
        {
            transitionCoordinator.UpdateFade();

            if (!transitionCoordinator.IsFading) return;

            // Execute post-peak actions for CameraTools (consistent zoom)
            if (transitionCoordinator.IsCompletingSwitch)
            {
                var activeSlot = slotManager.ActiveSlot;
                if (activeSlot != null && activeSlot.isCameraToolsSlot && activeSlot.ctSettings != null)
                {
                    var ctCam = cameraManager.ActiveCamera as CameraToolsCamera;
                    if (ctCam != null)
                    {
                        var controller = new CameraToolsCameraController();
                        if (activeSlot.ctSettings.UseConsistentAutoZoom)
                        {
                            controller.ApplyConsistentAutoZoom(true, activeSlot.ctSettings.ZoomPadding);
                        }
                        else if (activeSlot.ctSettings.AutoZoom)
                        {
                            ApplyNativeAutoZoom(activeSlot, controller);
                        }
                    }
                }
            }

            GUI.color = transitionCoordinator.GetFadeColor();
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        public void DrawConfirmationDialogs()
        {
            DrawDeleteDialog();
            DrawUnassignDialog();
        }

        public void ProcessZoomLateUpdate()
        {
            if (!showCameraPanel) return;

            slotManager.CheckExternalDeactivation();

            // REMOVED: cameraManager.ProcessPendingFixups(); 
            // This was causing the delayed FOV change

            // Use live lookup for active slot
            var activeSlot = slotManager.ActiveSlot;
            if (activeSlot != null && activeSlot.isCameraToolsSlot)
            {
                var ctCam = cameraManager.ActiveCamera as CameraToolsCamera;
                if (ctCam != null && activeSlot.ctSettings.UseConsistentAutoZoom)
                {
                    // Continuous update for consistent framing while active
                    var controller = new CameraToolsCameraController();
                    controller.ApplyConsistentAutoZoom(true, activeSlot.ctSettings.ZoomPadding);
                }
            }

            zoomService.UpdateHullCamZoom(Time.deltaTime);
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

            // Original logic: Check if ANY camera is active (managed or unmanaged)
            bool ctActive = new CameraToolsCameraController().IsActive;
            bool hullCamActive = HullCamBridge.IsAnyCameraActive();
            bool hasActiveCam = ctActive || hullCamActive;
            GUI.enabled = hasActiveCam;
            if (GUILayout.Button(CameraController.ReturnToMain, GUILayout.Height(CinematicUIResources.Layout.SpeedControl.BUTTON_HEIGHT)))
            {
                // Use transition coordinator for fade
                BeginCameraSwitch(() =>
                {
                    cameraManager.ReturnToMain(immediate: true);
                    slotManager.ClearActiveSlot();
                });
            }
            GUI.enabled = true;
            GUILayout.EndVertical();

            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            // RIGHT: Get fresh slot reference here for this frame
            GUILayout.BeginVertical(GUILayout.Width(CinematicUIResources.Layout.Camera.GRID_TEXT_COLUMN_WIDTH));

            var activeSlot = slotManager.ActiveSlot;
            bool showAutoZoom = activeSlot != null && activeSlot.isCameraToolsSlot && activeSlot.ctSettings != null;

            if (showAutoZoom)
            {
                DrawAutoZoomControls(activeSlot);
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

        private void DrawAutoZoomControls(CameraSlot slot)
        {
            var settings = slot?.ctSettings;
            if (settings == null) return;

            bool useConsistent = settings.UseConsistentAutoZoom;

            // Check if THIS specific slot is the currently active CT camera
            bool isThisSlotActive = (slotManager.ActiveSlot == slot) &&
                                   (cameraManager.ActiveCamera is CameraToolsCamera);

            float currentFOV = settings.ManualFOV;
            if (isThisSlotActive && cameraManager.ActiveCamera != null)
            {
                currentFOV = cameraManager.ActiveCamera.FieldOfView;
            }

            GUIStyle header = CinematicUIResources.Styles.Header();
            GUILayout.Label(CameraController.AutoZoomHeader, header);
            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);

            // Consistent Framing Toggle - ALWAYS SHOW if CT slot
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

            if (newUseConsistent != settings.UseConsistentAutoZoom)
            {
                settings.UseConsistentAutoZoom = newUseConsistent;

                // Apply immediately if this camera is currently active
                if (isThisSlotActive)
                {
                    var controller = new CameraToolsCameraController();
                    if (newUseConsistent)
                    {
                        controller.ApplyConsistentAutoZoom(true, settings.ZoomPadding);
                    }
                    else if (settings.AutoZoom)
                    {
                        CameraToolsReflectionProvider.SetBool(CameraToolsReflectionProvider.AutoZoomStationaryField, true);
                    }
                }
            }

            // Padding controls - show if consistent zoom enabled
            if (settings.UseConsistentAutoZoom)
            {
                GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
                GUILayout.Label(string.Format(CameraController.PaddingLabel, settings.ZoomPadding),
                    HighLogic.Skin.label);

                float newPadding = GUILayout.HorizontalSlider(settings.ZoomPadding, 0.5f, 3.0f);
                if (!Mathf.Approximately(newPadding, settings.ZoomPadding))
                {
                    settings.ZoomPadding = newPadding;

                    // Apply immediately if this is the active camera
                    if (isThisSlotActive)
                    {
                        var controller = new CameraToolsCameraController();
                        controller.ApplyConsistentAutoZoom(true, newPadding);
                    }
                }

                GUIStyle helpStyle = CinematicUIResources.Styles.Help();
                GUILayout.Label(CameraController.PaddingTooltip, helpStyle);
            }

            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            // Live FOV display if active
            if (isThisSlotActive)
            {
                GUIStyle infoStyle = CinematicUIResources.Styles.Label(CinematicUIResources.Colors.INFO_ORANGE,
                    fontSize: CinematicUIResources.Typography.INFO);
                GUILayout.Label(string.Format("Current FOV: {0:F1}°", currentFOV), infoStyle);
            }
            else
            {
                GUILayout.Label(" ");
            }
        }

        private void DrawZoomControls()
        {
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);
            GUILayout.BeginVertical(GUI.skin.box);

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

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(CameraController.ResetZoom, GUILayout.Width(CinematicUIResources.Layout.Zoom.RESET_BUTTON_WIDTH)))
            {
                zoomService.ResetZoom(maxFov);
            }

            GUILayout.FlexibleSpace();

            GUIStyle autoStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (zoomService.AutoDistanceTracking)
            {
                autoStyle.normal.textColor = CinematicUIResources.Colors.AUTO_TRACK_BLUE;
                autoStyle.fontStyle = FontStyle.Bold;
            }
            zoomService.AutoDistanceTracking = GUILayout.Toggle(zoomService.AutoDistanceTracking, CameraController.AutoDistanceToggle, autoStyle);

            GUILayout.EndHorizontal();

            if (zoomService.AutoDistanceTracking)
            {
                GUIStyle distStyle = CinematicUIResources.Styles.Label(
                    CinematicUIResources.Colors.INFO_ORANGE,
                    fontSize: CinematicUIResources.Typography.HELP
                );
                GUILayout.Label(CameraController.AutoDistanceTooltip, distStyle);
            }

            GUILayout.EndVertical();
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
            var slot = slotManager.GetSlot(index);

            if (slot?.isCameraToolsSlot == true)
            {
                if (slot.ctSettings == null) return;

                if (slot.ctSettings.Mode == ToolModes.Pathing)
                {
                    if (slot.ctSettings.SelectedPathIndex < 0)
                    {
                        ScreenMessages.PostScreenMessage("Cannot activate - invalid path index", 2f);
                        return;
                    }
                    var controller = new CameraToolsCameraController();
                    if (!controller.PathExists(slot.ctSettings.SelectedPathIndex))
                    {
                        ScreenMessages.PostScreenMessage("Saved path no longer exists", 2f);
                        return;
                    }
                }

                BeginCameraSwitch(() =>
                {
                    // CRITICAL: Set active slot FIRST so UI updates immediately
                    slotManager.SetActiveSlot(index);

                    // Activate the camera
                    cameraManager.SwitchToCamera(slot, immediate: true);

                    // Apply zoom settings immediately for snap (no interpolation)
                    if (slot.ctSettings.UseConsistentAutoZoom)
                    {
                        var controller = new CameraToolsCameraController();
                        // Immediate snap, no lerp
                        controller.EnforceAutoZoomFOVImmediate(
                            controller.CalculateConsistentAutoZoom(
                                FlightGlobals.ActiveVessel,
                                FlightCamera.fetch.transform.position,
                                slot.ctSettings.ZoomPadding
                            )
                        );
                    }
                    else if (slot.ctSettings.AutoZoom)
                    {
                        var controller = new CameraToolsCameraController();
                        ApplyNativeAutoZoom(slot, controller);
                    }

                    // Force position fixup if geographic (immediate, not deferred)
                    var ctCam = cameraManager.ActiveCamera as CameraToolsCamera;
                    if (ctCam != null && slot.ctSettings.UseGeographicPosition)
                    {
                        var controller = new CameraToolsCameraController();
                        if (controller.HasPendingGeographicRestoration())
                        {
                            controller.PostActivationPositionFixup();
                        }
                    }
                });
                return;
            }

            // HullCam handling
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
        }

        private void BeginCameraSwitch(Action cameraAction)
        {
            transitionCoordinator.BeginTransition(cameraAction);
        }

        private void AssignCurrentToSlot(int index)
        {
            // Check CameraTools first via CameraManager capability check
            var ctController = new CameraToolsCameraController();
            if (ctController.IsAvailable && ctController.IsActive)
            {
                var settings = ctController.CaptureCurrentSettings();
                if (settings != null)
                {
                    if (slotManager.AssignCameraToolsToSlot(index, settings))
                    {
                        ScreenMessages.PostScreenMessage(string.Format(CameraController.SavedCameraToolsFormat, settings.GetDisplayName()), 2f);
                    }
                    return;
                }
            }

            // HullCam assignment
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

            // IMMEDIATE SNAP - bypass CameraTools' 0.1f lerp
            controller.EnforceAutoZoomFOVImmediate(nativeFOV);

            // Force FlightCamera to accept it immediately
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

            nameToSave = GetUniquePresetName(nameToSave);

            scenario.SavePreset(nameToSave, false, new List<CameraSlot>(slotManager.Slots),
                parentWindowRect.x, parentWindowRect.y);
            presetNameBuffer = nameToSave;
        }

        private string GetDefaultPresetName()
        {
            return _cachedVessel?.vesselName ?? CameraController.Preset;
        }

        private string GetUniquePresetName(string baseName)
        {
            var scenario = CameraPanelConfig.Instance;
            if (scenario == null) return baseName;

            var existing = scenario.GetPresetNames();
            if (!existing.Contains(baseName)) return baseName;

            int counter = 1;
            string candidate;
            do
            {
                candidate = string.Format(CameraController.PresetNameUniqueFormat, baseName, counter);
                counter++;
            } while (existing.Contains(candidate));

            return candidate;
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