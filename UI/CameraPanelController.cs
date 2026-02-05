using CinematicRecorder.Core;
using CinematicRecorder.Integration;
using static CinematicRecorder.UI.CinematicUIStrings;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Manages the camera assignment grid, zoom controls, and preset management.
    /// Designed to be hosted inside RecordingControlsWindow or operate standalone.
    /// </summary>
    public class CameraPanelController
    {
        #region Fields & State
        private readonly List<CameraSlot> cameraSlots = new List<CameraSlot>();
        private readonly GUIStyle[] cameraButtonStyles = new GUIStyle[7];

        private bool cameraPanelStylesInitialized = false;
        private bool showCameraPanel = false;
        private bool showPresetList = false;
        private bool showDeleteConfirm = false;
        private int pendingUnassignSlot = -1;
        private string presetNameBuffer = "";

        // Explicit activation tracking
        private int _activeSlotIndex = -1;
        private bool _wasCameraToolsActive = false;
        private ToolModes _lastCameraToolsMode = ToolModes.StationaryCamera;

        // Zoom State
        private float zoomIntentSlider = 0f;
        private float zoomSmoothVelocity = 0f;
        private bool autoDistanceTracking = false;
        private float autoZoomDistanceRef = 100f;
        private float targetFoV = 60f;
        private float currentFoV = 60f;
        private object zoomControlledCamera = null;

        // Crossfade State (coordinated with parent window)
        private float screenFadeAlpha = 0f;
        private bool isFading = false;
        private float fadeSpeed = 8f;
        private Action pendingCameraAction;
        private bool useFadeOnSwap = true;
        private float fadeDurationSlider = 0.5f;
        private bool _cameraSwitchPending = false;

        // External Dependencies
        private readonly MonoBehaviour host;
        private Rect parentWindowRect;
        #endregion

        #region Properties
        public bool IsVisible => showCameraPanel;
        public bool IsFading => isFading;
        public float FadeAlpha => screenFadeAlpha;
        public bool UseFadeOnSwap => useFadeOnSwap;
        #endregion

        #region Constructor
        public CameraPanelController(MonoBehaviour hostBehaviour)
        {
            host = hostBehaviour ?? throw new ArgumentNullException(nameof(hostBehaviour));
            InitializeSlots();
            SubscribeToEvents();
        }
        #endregion

        #region Initialization
        private void InitializeSlots()
        {
            cameraSlots.Clear();
            for (int i = 0; i < CinematicUIResources.Layout.Camera.TOTAL_SLOTS; i++)
            {
                cameraSlots.Add(new CameraSlot { buttonID = string.Format(CameraController.ButtonIdFormat, i) });
            }

            if (CameraPanelConfig.Instance != null)
            {
                CameraPanelConfig.Instance.OnPresetLoaded += OnPresetLoaded;
            }
        }

        private void OnPresetLoaded(CameraPanelPreset preset)
        {
            if (preset?.buttonAssignments != null && preset.buttonAssignments.Count == CinematicUIResources.Layout.Camera.TOTAL_SLOTS)
            {
                cameraSlots.Clear();
                cameraSlots.AddRange(preset.buttonAssignments);
                presetNameBuffer = preset.presetName;
            }
        }

        public void InitializeStyles()
        {
            if (cameraPanelStylesInitialized) return;

            for (int i = 0; i < 7; i++)
            {
                cameraButtonStyles[i] = CinematicUIResources.Styles.CameraButton(i);
            }

            cameraPanelStylesInitialized = true;
        }

        public void Shutdown()
        {
            UnsubscribeFromEvents();
            if (CameraPanelConfig.Instance != null)
            {
                CameraPanelConfig.Instance.OnPresetLoaded -= OnPresetLoaded;
            }
        }
        #endregion

        #region Event Subscription
        private void SubscribeToEvents()
        {
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);
            // NEW: Detect vessel changes to clear active slot (Step 5)
            GameEvents.onVesselChange.Add(OnVesselChange);
        }

        private void UnsubscribeFromEvents()
        {
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
            GameEvents.onVesselChange.Remove(OnVesselChange);
        }

        private void OnVesselWillDestroy(Vessel v)
        {
            if (v == FlightGlobals.ActiveVessel && HullCamBridge.IsAnyCameraActive())
            {
                HullCamBridge.ClearHullCamStaticState();
            }
        }

        private void OnGameSceneLoadRequested(GameScenes scene)
        {
            HullCamBridge.ClearHullCamStaticState();
            _activeSlotIndex = -1;
            _wasCameraToolsActive = false;
        }

        // NEW: Clear active slot when vessel changes (Step 5)
        private void OnVesselChange(Vessel v)
        {
            _activeSlotIndex = -1;
            _wasCameraToolsActive = false;
        }
        #endregion

        #region Main Rendering Entry Point
        public void Draw(Rect parentWindowRect)
        {
            this.parentWindowRect = parentWindowRect;

            if (!HullCamBridge.IsAvailable)
            {
                DrawDisabledPanel();
                return;
            }

            InitializeStyles();

            GUILayout.Space(CinematicUIResources.Spacing.SECTION);
            DrawFoldoutButton();

            if (!showCameraPanel) return;

            DrawFadeControls();
            DrawGridContainer();
            DrawZoomControlsIfActive();
            GUILayout.Space(CinematicUIResources.Spacing.SECTION);
            DrawProfilesInterface();
            UpdateMonitoring();
        }

        public void DrawFadeOverlay()
        {
            if (!isFading) return;

            screenFadeAlpha += Time.unscaledDeltaTime * fadeSpeed;

            if (screenFadeAlpha >= 1f && pendingCameraAction != null)
            {
                screenFadeAlpha = 1f;
                pendingCameraAction?.Invoke();
                pendingCameraAction = null;
                _cameraSwitchPending = true;
            }
            else if (_cameraSwitchPending)
            {
                _cameraSwitchPending = false;
                fadeSpeed = -Mathf.Abs(fadeSpeed);

                if (_activeSlotIndex >= 0 && _activeSlotIndex < cameraSlots.Count && cameraSlots[_activeSlotIndex].isCameraToolsSlot)
                {
                    var slot = cameraSlots[_activeSlotIndex];
                    if (slot.ctSettings != null)
                    {
                        var adapter = CameraToolsAdapter.Instance;
                        if (slot.ctSettings.UseConsistentAutoZoom)
                        {
                            adapter.ApplyConsistentAutoZoom(true, slot.ctSettings.ZoomPadding);
                        }
                        else if (slot.ctSettings.AutoZoom)
                        {
                            Vessel v = FlightGlobals.ActiveVessel;
                            if (v != null && FlightCamera.fetch != null)
                            {
                                Vector3 targetPos = (slot.ctSettings.HasTarget && !slot.ctSettings.TargetSelf)
                                    ? adapter.CamTarget?.transform.position ?? v.CoM
                                    : v.CoM;
                                float distance = Vector3.Distance(FlightCamera.fetch.transform.position, targetPos);
                                float margin = 30f;
                                float nativeFOV = (7000f / (distance + 100f)) - 14f + margin;
                                nativeFOV = Mathf.Clamp(nativeFOV, 2f, 60f);

                                adapter.EnforceAutoZoomFOVImmediate(nativeFOV);
                            }
                        }
                    }
                }
            }
            else if (screenFadeAlpha <= 0f && fadeSpeed < 0)
            {
                screenFadeAlpha = 0f;
                isFading = false;
                fadeSpeed = Mathf.Abs(fadeSpeed);
            }

            GUI.color = new Color(0, 0, 0, screenFadeAlpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
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
            if (useFadeOnSwap)
            {
                toggleStyle.normal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.onNormal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.fontStyle = FontStyle.Bold;
                toggleStyle.alignment = TextAnchor.MiddleLeft;
            }
            useFadeOnSwap = GUILayout.Toggle(useFadeOnSwap, CameraController.FadeOnSwapToggle, toggleStyle);
            GUILayout.EndHorizontal();

            if (useFadeOnSwap)
            {
                float duration = Mathf.Lerp(
                    CinematicUIResources.Layout.Crossfade.DURATION_MIN,
                    CinematicUIResources.Layout.Crossfade.DURATION_MAX,
                    fadeDurationSlider
                );
                GUILayout.Label(string.Format(CameraController.FadeDurationFormat, duration), HighLogic.Skin.label);
                fadeDurationSlider = GUILayout.HorizontalSlider(
                    fadeDurationSlider,
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

            GUILayout.FlexibleSpace();

            var adapter = CameraToolsAdapter.Instance;
            bool hasActiveCam = HullCamBridge.IsAnyCameraActive() || adapter.IsActive;
            GUI.enabled = hasActiveCam;
            if (GUILayout.Button(CameraController.ReturnToMain, GUILayout.Height(CinematicUIResources.Layout.SpeedControl.BUTTON_HEIGHT)))
            {
                TriggerSwitchWithFade(() =>
                {
                    if (adapter.IsActive)
                    {
                        adapter.Revert();
                    }
                    else if (HullCamBridge.IsAnyCameraActive())
                    {
                        HullCamBridge.RestoreMain();
                    }
                    // Clear explicit activation and tracking on revert
                    _activeSlotIndex = -1;
                    _wasCameraToolsActive = false;
                });
            }
            GUI.enabled = true;
            GUILayout.EndVertical();

            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            // RIGHT: Instructions OR Auto-Zoom Controls (Step 5)
            GUILayout.BeginVertical(GUILayout.Width(CinematicUIResources.Layout.Camera.GRID_TEXT_COLUMN_WIDTH));

            // Show auto-zoom controls if a CameraTools slot is explicitly active
            if (_activeSlotIndex >= 0 && cameraSlots[_activeSlotIndex].isCameraToolsSlot)
            {
                DrawAutoZoomControls(_activeSlotIndex);
            }
            else
            {
                DrawInstructions();
            }

            GUILayout.FlexibleSpace();

            GUI.enabled = HullCamBridge.GetCurrentCamera() != null;
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
            Vessel currentVessel = FlightGlobals.ActiveVessel;

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
            CameraSlot slot = cameraSlots[index];
            // NEW: Pass explicit activation state (Step 4/5)
            CameraSlot.SlotStatus status = slot.GetStatus(currentVessel, index == _activeSlotIndex);
            int styleIndex = GetStyleIndexForStatus(status, slot.isCameraToolsSlot);
            string buttonLabel = (index + 1).ToString();

            // Reserve rect first so we can use it for hit detection
            Rect buttonRect = GUILayoutUtility.GetRect(
                CinematicUIResources.Layout.Camera.BUTTON_SIZE,
                CinematicUIResources.Layout.Camera.BUTTON_HEIGHT,
                cameraButtonStyles[styleIndex]);

            // Check right-click BEFORE drawing the button
            Event evt = Event.current;
            if (evt.type == EventType.MouseDown &&
                evt.button == 1 &&
                buttonRect.Contains(evt.mousePosition))
            {
                if (status != CameraSlot.SlotStatus.Unassigned)
                {
                    pendingUnassignSlot = index;
                    evt.Use(); // Consume the event so button doesn't see it
                }
            }
            // Then draw the button and handle left-clicks
            else if (GUI.Button(buttonRect, buttonLabel, cameraButtonStyles[styleIndex]))
            {
                OnButtonClicked(index);
            }
        }

        // NEW: Draw Instructions (unchanged)
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

        // NEW: Auto-Zoom Controls UI (Step 5)
        private void DrawAutoZoomControls(int slotIndex)
        {
            CameraSlot slot = cameraSlots[slotIndex];
            if (slot.ctSettings == null) return;

            GUIStyle header = CinematicUIResources.Styles.Header();
            GUILayout.Label(CameraController.AutoZoomHeader, header);
            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);

            // Consistent Framing Toggle
            GUIStyle toggleStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (slot.ctSettings.UseConsistentAutoZoom)
            {
                toggleStyle.normal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.onNormal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.fontStyle = FontStyle.Bold;
            }

            bool newUseConsistent = GUILayout.Toggle(slot.ctSettings.UseConsistentAutoZoom,
                CameraController.ConsistentFramingToggle, toggleStyle);

            if (newUseConsistent != slot.ctSettings.UseConsistentAutoZoom)
            {
                slot.ctSettings.UseConsistentAutoZoom = newUseConsistent;
                // Immediately apply to CameraTools
                if (newUseConsistent)
                {
                    CameraToolsAdapter.Instance.ApplyConsistentAutoZoom(true, slot.ctSettings.ZoomPadding);
                }
                else
                {
                    // Re-enable native auto-zoom if it was enabled in settings
                    CameraToolsAdapter.Instance.ApplyConsistentAutoZoom(false, slot.ctSettings.ZoomPadding);
                    if (slot.ctSettings.AutoZoom)
                    {
                        CameraToolsAdapter.Instance.AutoZoomStationary = true;
                    }
                }
            }

            // Padding Slider (only show if consistent framing is enabled)
            if (slot.ctSettings.UseConsistentAutoZoom)
            {
                GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
                GUILayout.Label(string.Format(CameraController.PaddingLabel, slot.ctSettings.ZoomPadding),
                    HighLogic.Skin.label);

                float newPadding = GUILayout.HorizontalSlider(slot.ctSettings.ZoomPadding, 0.5f, 3.0f);
                if (!Mathf.Approximately(newPadding, slot.ctSettings.ZoomPadding))
                {
                    slot.ctSettings.ZoomPadding = newPadding;
                    // Update immediately
                    CameraToolsAdapter.Instance.ApplyConsistentAutoZoom(true, newPadding);
                }

                GUIStyle helpStyle = CinematicUIResources.Styles.Help();
                GUILayout.Label(CameraController.PaddingTooltip, helpStyle);
            }

            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            // Show current FOV if CameraTools is active
            var adapter = CameraToolsAdapter.Instance;
            if (adapter.IsActive && adapter.CurrentMode == ToolModes.StationaryCamera)
            {
                GUIStyle infoStyle = CinematicUIResources.Styles.Label(CinematicUIResources.Colors.INFO_ORANGE,
                    fontSize: CinematicUIResources.Typography.INFO);
                GUILayout.Label(string.Format("Current FOV: {0:F1}°", adapter.ManualFOV), infoStyle);
            }
        }

        private void DrawZoomControlsIfActive()
        {
            if (!HullCamBridge.IsAnyCameraActive()) return;

            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label(CameraController.ZoomControlLabel, HighLogic.Skin.label);

            GUILayout.BeginHorizontal();
            GUILayout.Label(CameraController.ZoomOut, GUILayout.Width(CinematicUIResources.Layout.Zoom.LABEL_WIDTH));

            GUIStyle intentStyle = new GUIStyle(HighLogic.Skin.horizontalSlider);
            GUIStyle thumbStyle = new GUIStyle(HighLogic.Skin.horizontalSliderThumb);
            zoomIntentSlider = GUILayout.HorizontalSlider(zoomIntentSlider, -1f, 1f, intentStyle, thumbStyle);

            GUILayout.Label(CameraController.ZoomIn, GUILayout.Width(CinematicUIResources.Layout.Zoom.LABEL_WIDTH));
            GUILayout.EndHorizontal();

            float maxFov = HullCamBridge.GetCameraFoVMax(HullCamBridge.GetCurrentCamera());
            GUILayout.Label(string.Format(CameraController.FOVFormat, currentFoV, maxFov), HighLogic.Skin.label);

            GUILayout.BeginHorizontal();
            DrawZoomResetButton(maxFov);
            GUILayout.FlexibleSpace();
            DrawAutoDistanceToggle();
            GUILayout.EndHorizontal();

            if (autoDistanceTracking)
            {
                GUIStyle distStyle = CinematicUIResources.Styles.Label(
                    CinematicUIResources.Colors.INFO_ORANGE,
                    fontSize: CinematicUIResources.Typography.HELP
                );
                GUILayout.Label(CameraController.AutoDistanceTooltip, distStyle);
            }

            GUILayout.EndVertical();
        }

        private void DrawZoomResetButton(float maxFov)
        {
            if (GUILayout.Button(CameraController.ResetZoom, GUILayout.Width(CinematicUIResources.Layout.Zoom.RESET_BUTTON_WIDTH)))
            {
                targetFoV = maxFov;
                zoomIntentSlider = 0f;
            }
        }

        private void DrawAutoDistanceToggle()
        {
            GUIStyle autoStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (autoDistanceTracking)
            {
                autoStyle.normal.textColor = CinematicUIResources.Colors.AUTO_TRACK_BLUE;
                autoStyle.fontStyle = FontStyle.Bold;
            }
            autoDistanceTracking = GUILayout.Toggle(autoDistanceTracking, CameraController.AutoDistanceToggle, autoStyle);
        }

        private void DrawProfilesInterface()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);

            GUILayout.BeginHorizontal();

            CameraPanelConfig scenario = CameraPanelConfig.Instance;
            bool hasPresets = scenario != null && scenario.GetPresetNames().Count > 0;
            CameraPanelPreset activePreset = scenario?.GetActivePreset();

            EnsurePresetNameBuffer();

            presetNameBuffer = GUILayout.TextField(presetNameBuffer, GUILayout.Width(150));

            if (GUILayout.Button(CameraController.SavePreset, GUILayout.Width(50)))
            {
                SavePreset(scenario);
            }

            GUI.enabled = activePreset != null;
            if (GUILayout.Button(CameraController.DeletePreset, GUILayout.Width(50)))
            {
                if (activePreset != null) showDeleteConfirm = true;
            }
            GUI.enabled = true;

            // REMOVED: LOG CAM button (Step 5)
            /*
            if (GUILayout.Button("LOG CAM", GUILayout.Width(60)))
            {
                CameraToolsAdapter.Instance.LogCurrentCameraState("DebugCheck");
            }
            */

            GUILayout.FlexibleSpace();

            if (hasPresets && GUILayout.Button(CameraController.LoadPreset, GUILayout.Width(60)))
            {
                showPresetList = !showPresetList;
            }

            GUILayout.EndHorizontal();

            if (showPresetList && scenario != null)
            {
                DrawPresetDropdown(scenario);
            }

            GUILayout.EndVertical();
        }

        private void DrawPresetDropdown(CameraPanelConfig scenario)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            List<string> presetNames = scenario.GetPresetNames();
            foreach (string name in presetNames)
            {
                if (GUILayout.Button(name))
                {
                    scenario.LoadPreset(name);
                    showPresetList = false;
                }
            }
            GUILayout.EndVertical();
        }
        #endregion

        #region Dialog Rendering (Call from parent's OnGUI)
        public void DrawConfirmationDialogs()
        {
            DrawDeleteDialog();
            DrawUnassignDialog();
        }

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
                    CameraPanelConfig.Instance?.DeletePreset(presetNameBuffer);
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
                    ClearSlot(slotIndex);
                    // NEW: Clear active slot if unassigning the active one (Step 5)
                    if (_activeSlotIndex == slotIndex)
                        _activeSlotIndex = -1;
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

        #region Camera Interaction Logic
        private void OnButtonClicked(int index)
        {
            CameraSlot slot = cameraSlots[index];
            var adapter = CameraToolsAdapter.Instance;

            if (slot.isCameraToolsSlot)
            {
                if (slot.ctSettings == null)
                {
                    Debug.LogWarning($"[CamPanel] Slot {index} is CT type but has null settings");
                    return;
                }

                if (slot.ctSettings.Mode == ToolModes.Pathing)
                {
                    if (slot.ctSettings.SelectedPathIndex < 0)
                    {
                        ScreenMessages.PostScreenMessage("Cannot activate - invalid path index", 2f);
                        return;
                    }
                    if (!adapter.PathExists(slot.ctSettings.SelectedPathIndex))
                    {
                        ScreenMessages.PostScreenMessage("Saved path no longer exists", 2f);
                        return;
                    }
                }

                TriggerSwitchWithFade(() =>
                {
                    if (adapter.IsActive)
                    {
                        adapter.Revert();
                    }

                    if (HullCamBridge.IsAnyCameraActive())
                    {
                        HullCamBridge.ClearHullCamStaticState();
                    }

                    adapter.ActivateMode(slot.ctSettings.Mode, slot.ctSettings);

                    if (adapter.HasPendingGeographicRestoration())
                    {
                        adapter.PostActivationPositionFixup();
                    }

                    _activeSlotIndex = index;

                    adapter.AutoZoomStationary = false;

                    if (slot.ctSettings.UseConsistentAutoZoom)
                    {
                        adapter.ApplyConsistentAutoZoom(true, slot.ctSettings.ZoomPadding);
                    }
                    else if (slot.ctSettings.AutoZoom)
                    {
                        Vessel currentVessel = FlightGlobals.ActiveVessel;
                        if (currentVessel != null && FlightCamera.fetch != null)
                        {
                            Vector3 cameraPos = FlightCamera.fetch.transform.position;
                            Vector3 targetPos = (slot.ctSettings.HasTarget && !slot.ctSettings.TargetSelf)
                                ? adapter.CamTarget?.transform.position ?? currentVessel.CoM
                                : currentVessel.CoM;

                            float distance = Vector3.Distance(cameraPos, targetPos);
                            float margin = 30f;
                            float nativeFOV = (7000f / (distance + 100f)) - 14f + margin;
                            nativeFOV = Mathf.Clamp(nativeFOV, 2f, 60f);

                            adapter.EnforceAutoZoomFOVImmediate(nativeFOV);
                        }
                    }
                });
                return;
            }

            Vessel currentVesselExt = FlightGlobals.ActiveVessel;
            CameraSlot.SlotStatus status = slot.GetStatus(currentVesselExt);

            switch (status)
            {
                case CameraSlot.SlotStatus.Unassigned:
                    AssignCurrentToSlot(index);
                    break;
                case CameraSlot.SlotStatus.Active:
                    return;
                case CameraSlot.SlotStatus.Assigned:
                case CameraSlot.SlotStatus.Remote:
                    ActivateSlotCamera(slot, currentVesselExt, index);
                    break;
                case CameraSlot.SlotStatus.Unavailable:
                    ScreenMessages.PostScreenMessage(CameraController.CameraUnavailable, 2f);
                    break;
            }
        }

        /// <summary>
        /// Coroutine helper that executes the given action on the next frame.
        /// </summary>
        private IEnumerator ExecuteNextFrame(System.Action action)
        {
            yield return null;
            action?.Invoke();
        }

        private void ActivateSlotCamera(CameraSlot slot, Vessel currentVessel, int slotIndex)
        {
            object cam = HullCamBridge.ResolveCameraSlot(slot, currentVessel);
            var adapter = CameraToolsAdapter.Instance;

            if (cam != null && cam != HullCamBridge.GetCurrentCamera())
            {
                zoomIntentSlider = 0f;
                TriggerSwitchWithFade(() =>
                {
                    if (adapter.IsActive)
                    {
                        adapter.ReleaseControlWithoutReverting();
                    }

                    HullCamBridge.Activate(cam);

                    _activeSlotIndex = slotIndex;
                });
            }
        }

        private void AssignCurrentToFirstOpenSlot()
        {
            for (int i = 0; i < CinematicUIResources.Layout.Camera.TOTAL_SLOTS; i++)
            {
                if (cameraSlots[i].GetStatus() == CameraSlot.SlotStatus.Unassigned)
                {
                    AssignCurrentToSlot(i);
                    break;
                }
            }
        }

        private void AssignCurrentToSlot(int index)
        {
            var adapter = CameraToolsAdapter.Instance;

            // Check if CameraTools is currently active first
            if (adapter.IsAvailable && adapter.IsActive)
            {
                var settings = adapter.CaptureSettings();
                if (settings != null)
                {
                    // VALIDATION: Ensure Pathing mode has a valid path selected
                    if (settings.Mode == ToolModes.Pathing && settings.SelectedPathIndex < 0)
                    {
                        ScreenMessages.PostScreenMessage("Cannot save: No path selected in CameraTools", 2f);
                        return;
                    }

                    cameraSlots[index] = new CameraSlot
                    {
                        buttonID = string.Format(CameraController.ButtonIdFormat, index),
                        isCameraToolsSlot = true,
                        ctSettings = settings,
                        cameraName = settings.GetDisplayName()
                    };

                    ScreenMessages.PostScreenMessage(string.Format(CameraController.SavedCameraToolsFormat, settings.GetDisplayName()), 2f);
                    return;
                }
            }

            // Existing HullCam assignment
            if (!ValidateAssignmentPrerequisites(out object currentCam, out Vessel vessel)) return;

            Part part = GetPartFromCamera(currentCam);
            string camName = HullCamBridge.GetCameraName(currentCam) ?? "";

            cameraSlots[index] = new CameraSlot
            {
                buttonID = string.Format(CameraController.ButtonIdFormat, index),
                cameraName = camName,
                partPersistentId = part != null ? part.persistentId : 0u,
                vesselId = vessel.id.ToString(),
                allowAnyVessel = false,
                isCameraToolsSlot = false,
                ctSettings = null
            };

            ScreenMessages.PostScreenMessage(string.Format(CameraController.SavedHullCamFormat, camName), 2f);
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

            vessel = FlightGlobals.ActiveVessel;
            return vessel != null;
        }

        private Part GetPartFromCamera(object cameraModule)
        {
            try
            {
                Component comp = cameraModule as Component;
                if (comp == null) return null;

                Transform current = comp.transform;
                while (current != null)
                {
                    Part part = current.GetComponent<Part>();
                    if (part != null) return part;
                    current = current.parent;
                }
            }
            catch (Exception ex)
            {
                Debug.Log(CameraController.GetPartFromCameraFail + ex.Message);
            }
            return null;
        }

        private void ClearSlot(int index)
        {
            cameraSlots[index] = new CameraSlot { buttonID = string.Format(CameraController.ButtonIdFormat, index) };
        }

        private void UpdateMonitoring()
        {
            if (!HullCamBridge.IsAvailable) return;
            _ = HullCamBridge.GetCurrentCamera();
        }

        private int GetStyleIndexForStatus(CameraSlot.SlotStatus status)
        {
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

        #region Zoom Logic
        public void ProcessZoomLateUpdate()
        {
            if (!showCameraPanel) return;

            var adapter = CameraToolsAdapter.Instance;

            // External deactivation detection - only when not fading
            if (!isFading && pendingCameraAction == null && _activeSlotIndex >= 0)
            {
                if (cameraSlots[_activeSlotIndex].isCameraToolsSlot)
                {
                    if (_wasCameraToolsActive && !adapter.IsActive)
                    {
                        _activeSlotIndex = -1;
                    }
                    else if (adapter.IsActive && adapter.CurrentMode != cameraSlots[_activeSlotIndex].ctSettings.Mode)
                    {
                        _activeSlotIndex = -1;
                    }
                }
                else if (!cameraSlots[_activeSlotIndex].isCameraToolsSlot)
                {
                    if (_wasCameraToolsActive && !HullCamBridge.IsAnyCameraActive())
                    {
                        _activeSlotIndex = -1;
                    }
                }
            }

            _wasCameraToolsActive = adapter.IsActive;
            if (adapter.IsActive)
                _lastCameraToolsMode = adapter.CurrentMode;

            // ENFORCEMENT: Always enforce FOV when we have an active CT slot.
            // During fade-to-black: _activeSlotIndex still points to OLD slot, keeping old camera stable.
            // At peak black: _activeSlotIndex updates to NEW slot inside the lambda.
            // During fade-from-black: _activeSlotIndex points to NEW slot, keeping new camera stable.
            if (_activeSlotIndex >= 0 && _activeSlotIndex < cameraSlots.Count && cameraSlots[_activeSlotIndex].isCameraToolsSlot)
            {
                var slot = cameraSlots[_activeSlotIndex];
                if (slot.ctSettings != null && adapter.IsActive)
                {
                    if (slot.ctSettings.UseConsistentAutoZoom)
                    {
                        adapter.ApplyConsistentAutoZoom(true, slot.ctSettings.ZoomPadding);
                    }
                    else if (slot.ctSettings.AutoZoom)
                    {
                        Vessel vessel = FlightGlobals.ActiveVessel;
                        if (vessel != null && FlightCamera.fetch != null)
                        {
                            Vector3 cameraPos = FlightCamera.fetch.transform.position;
                            Vector3 targetPos = (slot.ctSettings.HasTarget && !slot.ctSettings.TargetSelf)
                                ? adapter.CamTarget?.transform.position ?? vessel.CoM
                                : vessel.CoM;

                            float distance = Vector3.Distance(cameraPos, targetPos);
                            float margin = 30f;
                            float nativeFOV = (7000f / (distance + 100f)) - 14f + margin;
                            nativeFOV = Mathf.Clamp(nativeFOV, 2f, 60f);

                            adapter.ManualFOV = nativeFOV;
                            FlightCamera.fetch.SetFoV(nativeFOV);
                        }
                    }
                }
            }

            // HullCam zoom logic (unchanged)
            if (!HullCamBridge.IsAvailable) return;

            var activeCam = HullCamBridge.GetCurrentCamera();
            if (activeCam == null)
            {
                zoomControlledCamera = null;
                return;
            }

            InitializeZoomForCamera(activeCam);
            ProcessZoomIntent(activeCam);
            ApplyZoom(activeCam);
            DecayZoomIntent();
        }

        private void InitializeZoomForCamera(object activeCam)
        {
            if (activeCam == zoomControlledCamera) return;

            zoomControlledCamera = activeCam;
            currentFoV = HullCamBridge.GetCameraFoV(activeCam);
            targetFoV = currentFoV;
            zoomIntentSlider = 0f;
        }

        private void ProcessZoomIntent(object activeCam)
        {
            float minFoV = HullCamBridge.GetCameraFoVMin(activeCam);
            float maxFoV = HullCamBridge.GetCameraFoVMax(activeCam);

            if (autoDistanceTracking && FlightGlobals.ActiveVessel != null)
            {
                ApplyAutoZoom(activeCam, minFoV, maxFoV);
            }
            else
            {
                ApplyManualZoom(minFoV, maxFoV);
            }

            targetFoV = Mathf.Clamp(targetFoV, minFoV, maxFoV);
            currentFoV = Mathf.SmoothDamp(currentFoV, targetFoV, ref zoomSmoothVelocity,
                CinematicUIResources.Layout.Zoom.SMOOTH_TIME, Mathf.Infinity, Time.deltaTime);
        }

        private void ApplyAutoZoom(object activeCam, float minFoV, float maxFoV)
        {
            var camTransform = HullCamBridge.GetCameraTransform(activeCam);
            if (camTransform == null || FlightGlobals.ActiveVessel == null) return;

            float distance = Vector3.Distance(camTransform.position, FlightGlobals.ActiveVessel.transform.position);
            float t = Mathf.Clamp01(Mathf.Log(distance / 10f + 1f) / Mathf.Log(autoZoomDistanceRef / 10f + 1f));
            float autoTarget = Mathf.Lerp(maxFoV, minFoV, t);

            targetFoV = Mathf.Lerp(autoTarget, targetFoV, Mathf.Abs(zoomIntentSlider));
        }

        private void ApplyManualZoom(float minFoV, float maxFoV)
        {
            if (Mathf.Abs(zoomIntentSlider) > CinematicUIResources.Layout.Zoom.INTENT_THRESHOLD)
            {
                float zoomDelta = -zoomIntentSlider * CinematicUIResources.Layout.Zoom.MAX_SPEED * Time.deltaTime;
                targetFoV += zoomDelta;
            }
            else
            {
                zoomIntentSlider = Mathf.MoveTowards(zoomIntentSlider, 0f, Time.deltaTime * 2f);
            }
        }

        private void ApplyZoom(object activeCam)
        {
            HullCamBridge.SetCameraFoV(activeCam, currentFoV);
        }

        private void DecayZoomIntent()
        {
            if (!Input.GetMouseButton(0))
            {
                zoomIntentSlider = Mathf.MoveTowards(zoomIntentSlider, 0f,
                    Time.deltaTime * CinematicUIResources.Layout.Zoom.RETURN_SPEED);
            }
        }
        #endregion

        #region Crossfade
        private void TriggerSwitchWithFade(Action cameraAction)
        {
            if (!useFadeOnSwap)
            {
                cameraAction?.Invoke();
                return;
            }

            if (isFading) return;
            isFading = true;
            screenFadeAlpha = 0f;

            float duration = CinematicUIResources.Layout.Crossfade.DURATION_MIN +
                fadeDurationSlider * (CinematicUIResources.Layout.Crossfade.DURATION_MAX - CinematicUIResources.Layout.Crossfade.DURATION_MIN);
            fadeSpeed = 1f / duration;

            pendingCameraAction = cameraAction;
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

            scenario.SavePreset(nameToSave, false, cameraSlots, parentWindowRect.x, parentWindowRect.y);
            presetNameBuffer = nameToSave;
        }

        private string GetDefaultPresetName()
        {
            return FlightGlobals.ActiveVessel?.vesselName ?? CameraController.Preset;
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

        #region Helper Methods

        private int GetStyleIndexForStatus(CameraSlot.SlotStatus status, bool isCameraTools)
        {
            if (isCameraTools)
            {
                switch (status)
                {
                    case CameraSlot.SlotStatus.Active: return 5; // Orange - CT Active
                    case CameraSlot.SlotStatus.Assigned: return 6; // Dark Orange - CT Inactive
                    default: return 3; // Gray
                }
            }

            switch (status)
            {
                case CameraSlot.SlotStatus.Active: return 0; // Green
                case CameraSlot.SlotStatus.Assigned: return 1; // Yellow
                case CameraSlot.SlotStatus.Unavailable: return 2; // Red
                case CameraSlot.SlotStatus.Remote: return 4; // Aqua
                default: return 3; // Gray
            }
        }

        #endregion
    }
}